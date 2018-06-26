using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Net.Http;
using System.Net.Http.Headers;

namespace SMSIntegration
{
    internal class Utility
    {

        /* Funtion retrieve Configuration value By  Configuration Name 
         * input is Configuration name (dis_name) And organization service from caller function
         * output is Configuration Value String from the entity
         */
        internal String GetConfigVal(string ConfigName, IOrganizationService service)
        {
            var returnValue = "";//declare return value variable

            try
            {
                QueryExpression query = new QueryExpression("dis_umnconfiguration"); //Query Declaration for 'dis_umnconfiguration' entity
                query.ColumnSet = new ColumnSet("dis_value"); //Column to Retrieve
                query.Criteria.AddCondition("dis_name",ConditionOperator.Equal,ConfigName); //Filter configuuration by Configuration Name from parameter
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); //Filter configuuration by Active State Code
                EntityCollection collection = service.RetrieveMultiple(query);  //Retrieve multiple based on Filter
                
                //get first value from collection when collection if member more then 0
                if(collection.Entities.Count> 0)
                {
                    var configEntity = collection.Entities.First(); // Get First Entity From Collection
                    if (configEntity.Contains("dis_value")) //check value is null
                        returnValue = configEntity.GetAttributeValue<string>("dis_value"); //assign value to variable
                }
            }
            catch //return empty String if function error
            {
                returnValue =  "";
            }
            return returnValue;
        }

        /* Function to retrieve ALL Column from any entity based entity Target
          * input is Entity Target and organization service from caller class and  output is the entity with all column listed
        */
        internal Entity GetEntityByTargetAllColumn(EntityReference entity, IOrganizationService service)
        {
            Entity smsEntity = service.Retrieve(entity.LogicalName, entity.Id, new ColumnSet(true));
            return smsEntity;
        }

        /* function to post request to UMN SMS API */
        internal async Task<string> RunPostAsync(string configEndpoint, string configUsername, string configPassword, string configAPIKey, string jsonRequest)
        {
            //http client object for push records to gatweway
            var client = new HttpClient();

            //Authentication to connect to API with username and password 
            var byteArray = Encoding.ASCII.GetBytes(configUsername + ":" + configPassword);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            //authorization key for connection to SMS api gatway
            client.DefaultRequestHeaders.Add("sq-api-key", configAPIKey);
            client.BaseAddress = new Uri(configEndpoint);

            //Message to submit with phone number to submit
            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("sq_batch_msg", jsonRequest)
            });

            //Post to gateway and get result to return to post sms workflow
            HttpResponseMessage response = await client.PostAsync("", formContent).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {

                string result = await response.Content.ReadAsStringAsync(); 
                return ((string)result);

            }
            else
            {
                return ""; // return empty string if any issue during API post
            }

        }
    }
}
