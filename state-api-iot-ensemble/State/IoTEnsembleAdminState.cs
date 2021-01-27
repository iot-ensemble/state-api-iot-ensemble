using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Fathym;
using LCU.Presentation.State.ReqRes;
using LCU.StateAPI.Utilities;
using LCU.StateAPI;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;
using System.Collections.Generic;
using LCU.Personas.Applications;
using Fathym.API;
using LCU.Graphs.Registry.Enterprises;

namespace LCU.State.API.IoTEnsemble.State
{
    [Serializable]
    [DataContract]
    public class IoTEnsembleAdminState
    {
        [DataMember]
        public virtual List<Enterprise> ChildEnterprises { get; set; }

        [DataMember]
        public virtual bool Loading { get; set; }
    }
}