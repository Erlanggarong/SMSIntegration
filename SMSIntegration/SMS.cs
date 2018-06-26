using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace SMSIntegration
{
    [DataContract]
    public class SMS
    {
        [DataMember]
        public string status { get; set; }
    }
}
