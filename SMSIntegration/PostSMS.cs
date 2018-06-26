using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;


/*
 * This Plugin for post sms from dynamics CRM to UMN SMS Gateway
 * Created By Kadek: 2018-06-26
 */
namespace SMSIntegration
{
    public class PostSMS : IPlugin
    {
        //Dynamics CRM context & Tracing Object Declaration
        IPluginExecutionContext context = null;
        IOrganizationServiceFactory serviceFactory = null;
        IOrganizationService service = null;
        ITracingService trace = null;

        //sms Connection variable declaration
        String configEndpoint, configUsername, configPassword, configAPIKey;

        //Utility function declaration
        Utility util = null;

        /* This is method to be execute during plugin execution process on change status
         * this process will post sms based on user input on SMS Activity(dis_sms)
         * the configuration will setup on UMN Configuration (dis_umnconfiguration) entity
         * the config itself contain Endpoint, Username and Password as authentication and APi Key for partner validation
         */
        public void Execute(IServiceProvider serviceProvider)
        {

            //Dynamics CRM context & Tracing Object Instatiation
            this.context =
              (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            this.serviceFactory =
                (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            this.service =
                serviceFactory.CreateOrganizationService(context.UserId);
            this.trace =
                (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            this.trace.Trace("Build Context");


            //try
            //{
            //Utillity class instatiation
            util = new Utility();
            this.trace.Trace("Instatiate utility class");

            //get input parameter value
            var varEndpoint = (string)this.context.InputParameters["ConfigEndpoint"];
            var varUsername = (string)context.InputParameters["ConfigUsername"];
            var varPassword = (string)context.InputParameters["ConfigPassword"];
            var varAPIKey = (string)context.InputParameters["ConfigAPIKey"];

            //Get Api Endpoint string
            configEndpoint = util.GetConfigVal(varEndpoint, this.service);
            //Get Username string
            configUsername = util.GetConfigVal(varUsername, this.service);
            //Get Password string
            configPassword = util.GetConfigVal(varPassword, this.service);
            //Get Apikey string
            configAPIKey = util.GetConfigVal(varAPIKey, this.service);

            this.trace.Trace(" Endpoint: {0} \n Username: {1} \n Passwod: {2} \n Api Key: {3} \n", configEndpoint, configUsername, configPassword, configAPIKey);

            //Get SMS Activity 
            if (this.context.InputParameters.Contains("Target") && this.context.InputParameters["Target"] is EntityReference) //Check if Plugin Contains Target
            {

                Entity smsActivity = this.util.GetEntityByTargetAllColumn((EntityReference)this.context.InputParameters["Target"], this.service); // Get all column from the target entity

                if (smsActivity.LogicalName != "dis_sms") // check if entity name is sms and stop process if not
                    return;

                if (smsActivity.Contains("to") && smsActivity.Contains("description")) // check if activity contain to and description
                {
                    this.SendSMS(smsActivity); //Send SMS
                }

            }


            /*}
            catch(Exception e)
            {
                
            }*/
        }

       

        /* Get SMS Body and recepient of SMS activity and post to PostSMS Function
        */
        private void SendSMS(Entity smsEntity)
        {

            var smsBody = smsEntity.GetAttributeValue<string>("description"); // store SMS message to variable 

            foreach (var entity in smsEntity.GetAttributeValue<EntityCollection>("to").Entities) // loop based on the number of recepient 
            {

                if (entity.Contains("partyid")) //check if entity Contain partyid fields
                {
                    var toReference = entity.GetAttributeValue<EntityReference>("partyid"); 
                    var smsNumber = getPhoneNumberbyReference(toReference); // Get phone number from entity Reference Entity

                    if (smsNumber != "" && smsNumber != null) // check when number is not null
                    {
                        String jsonRequest = "[{\"number\": \"" + smsNumber + "\",\"message\": \"" + smsBody + "\"}]"; //build sms body to be post

                        this.PostSMSActity(jsonRequest, smsEntity); //Proceed to send sms 
                    }
                }
            }
        }

        /* Post requet to SMS API/SMS Gateway
         *Input is Json Request and SMS Activity Entity
             */
        private void PostSMSActity(String jsonRequest, Entity smsEntity)
        {
            try
            {
                //Post value to UMN SMS Gateway
                var outString = this.util.RunPostAsync(this.configEndpoint, this.configUsername, this.configPassword, this.configAPIKey, jsonRequest).GetAwaiter().GetResult();

                //check result from SMS Gateway is not empty 
                if (outString != "")
                {
                    //Deserialize sms gateway using .net core and get result called status
                    using (MemoryStream memoryStream = new MemoryStream(Encoding.Unicode.GetBytes(outString)))
                    {
                        DataContractJsonSerializer dezerialiser = new DataContractJsonSerializer(typeof(SMS));
                        SMS dezSMS = (SMS)dezerialiser.ReadObject(memoryStream);

                        if (dezSMS != null)
                        {
                            //update sms activity with status from SMS gateway
                            smsEntity["dis_integrationstatus"] = dezSMS.status;
                            service.Update(smsEntity);

                            //is the result is Queued/Berhasil then set activity to complete
                            if (dezSMS.status.ToLower() == "msg_queued")
                            {
                                smsEntity["dis_nextaction"] = new OptionSetValue(914260000);
                                service.Update(smsEntity);
                                this.ChangeStatusToCompleted(smsEntity.Id);
                            }
                            //is the result is Invalid atau data yg dipush tidak valid maka di rekomendasi untuk melakukan retry
                            else if (dezSMS.status.ToLower() == "invalid_data")
                            {
                                smsEntity["dis_nextaction"] = new OptionSetValue(914260001);
                                service.Update(smsEntity);
                            }
                            //is the result is unavailable atau gateway sedang down maka di rekomendasi untuk melakukan retry
                            else if (dezSMS.status.ToLower() == "service_unavailable")
                            {
                                smsEntity["dis_nextaction"] = new OptionSetValue(914260001);
                                service.Update(smsEntity);
                            }
                        }
                    }
                }
            } catch (Exception e){
                this.trace.Trace("Error on:{0}", e.Message);
            }
        }

        /* Retrieve Phone from specified entity and specifieds fields. */
        private string getPhoneNumber(EntityReference toReference, string phoneNumberFields)
        {
            Entity retEntity = this.service.Retrieve(toReference.LogicalName, toReference.Id, new ColumnSet(phoneNumberFields));
            if (retEntity.Contains(phoneNumberFields))
                return retEntity.GetAttributeValue<string>(phoneNumberFields);
            else
                return "";
        }

        /* Check if phone number from account, contact or leads. */
        private string getPhoneNumberbyReference(EntityReference toReference)
        { 
            var smsNumber = string.Empty;

            //Check Collection party type and get phone number baaseed on type
            if (toReference.LogicalName == "account")
                smsNumber = getPhoneNumber(toReference, "telephone1");
            else if (toReference.LogicalName == "contact")
                smsNumber = getPhoneNumber(toReference, "mobilephone");
            else if (toReference.LogicalName == "lead")
                smsNumber = getPhoneNumber(toReference, "mobilephone");

            return smsNumber;
        }

        /* Change status of sms Activity to complate based on input guid */
        private void ChangeStatusToCompleted(Guid smsGuid)
        {
            
            SetStateRequest statusChangeReq = new SetStateRequest()
            {
                State = new OptionSetValue(1),
                Status = new OptionSetValue(2),
                EntityMoniker = new EntityReference("dis_sms", smsGuid)
            };
            this.service.Execute(statusChangeReq);

        }

    }
}
