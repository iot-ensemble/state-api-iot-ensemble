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

namespace LCU.State.API.IoTEnsemble.State
{
    [Serializable]
    [DataContract]
    public class IoTEnsembleState
    {
        #region Constants
        public const string HUB_NAME = "iotensemble";
        #endregion
    }
    
    [Serializable]
    [DataContract]
    public class IoTEnsembleDeviceInfo : DeviceInfo
    {
        [DataMember]
        public virtual string AuthenticationType { get; set; }

        [DataMember]
        public virtual int CloudToDeviceMessageCount { get; set; }

        [DataMember]
        public virtual string DeviceName { get; set; }

        [DataMember]
        public virtual Status LastStatusUpdate { get; set; }
    }

    [Serializable]
    [DataContract]
    public class IoTEnsembleTelemetry
    {
        [DataMember]
        public virtual bool Enabled { get; set; }

        [DataMember]
        public virtual DateTime LastSyncedAt { get; set; }

        [DataMember]
        public virtual bool Loading { get; set; }

        [DataMember]
        public virtual List<IoTEnsembleTelemetryPayload> Payloads { get; set; }

        [DataMember]
        public virtual int Page { get; set; }

        [DataMember]
        public virtual int PageSize { get; set; }

        [DataMember]
        public virtual int RefreshRate { get; set; }

        [DataMember]
        public virtual long TotalPayloads { get; set; }

        #region Constructors
        public IoTEnsembleTelemetry()
        {
            RefreshRate = 30;

            PageSize = 10;

            Page = 1;

            Payloads = new List<IoTEnsembleTelemetryPayload>();
        }
        #endregion
    }
}
