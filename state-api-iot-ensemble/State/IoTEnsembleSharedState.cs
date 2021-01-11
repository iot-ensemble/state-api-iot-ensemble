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
    public class IoTEnsembleSharedState
    {
        #region Constants
        public const string HUB_NAME = "iotensemble";
        #endregion

        [DataMember]
        public virtual string AccessLicenseType { get; set; }

        [DataMember]
        public virtual string AccessPlanGroup { get; set; }

        [DataMember]
        public virtual IoTEnsembleDashboardConfiguration Dashboard { get; set; }

        [DataMember]
        public virtual IoTEnsembleConnectedDevicesConfig Devices { get; set; }

        [DataMember]
        public virtual IoTEnsembleDrawersConfig Drawers { get; set; }

        [DataMember]
        public virtual EmulatedDeviceInfo Emulated { get; set; }

        [DataMember]
        public virtual ErrorContext Error { get; set; }

        [DataMember]
        public virtual bool HasAccess { get; set; }

        [DataMember]
        public virtual bool Loading { get; set; }

        [DataMember]
        public virtual List<string> SelectedDeviceIDs { get; set; }

        [DataMember]
        public virtual IoTEnsembleStorageConfiguration Storage { get; set; }

        [DataMember]
        public virtual IoTEnsembleTelemetry Telemetry { get; set; }

        [DataMember]
        public virtual string UserEnterpriseLookup { get; set; }
    }

    [Serializable]
    [DataContract]
    public class EmulatedDeviceInfo
    {
        [DataMember]
        public virtual bool Enabled { get; set; }

        [DataMember]
        public virtual bool Loading { get; set; }
    }

    [Serializable]
    [DataContract]
    public class ErrorContext
    {
        [DataMember]
        public virtual string ActionPath { get; set; }

        [DataMember]
        public virtual string ActionTarget { get; set; }

        [DataMember]
        public virtual string ActionText { get; set; }

        [DataMember]
        public virtual string Message { get; set; }

        [DataMember]
        public virtual string Title { get; set; }
    }

    [Serializable]
    [DataContract]
    public class IoTEnsembleDashboardConfiguration
    {
        [DataMember]
        public virtual MetadataModel FreeboardConfig { get; set; }

        [DataMember]
        public virtual MetadataModel PowerBIConfig { get; set; }
    }

    [Serializable]
    [DataContract]
    public class IoTEnsembleConnectedDevicesConfig
    {
        [DataMember]
        public virtual List<IoTEnsembleDeviceInfo> Devices { get; set; }

        [DataMember]
        public virtual bool Loading { get; set; }

        [DataMember]
        public virtual int MaxDevicesCount { get; set; }

        [DataMember]
        public virtual string Page { get; set; }

        [DataMember]
        public virtual int PageSize { get; set; }

        [DataMember]
        public virtual Dictionary<string, string> SASTokens { get; set; }
    }

    [Serializable]
    [DataContract]
    public class IoTEnsembleDeviceEnrollment
    {
        [DataMember]
        public virtual string DeviceName { get; set; }
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
    }

    [Serializable]
    [DataContract]
    public class IoTEnsembleTelemetryResponse : BaseResponse
    {
        [DataMember]
        public virtual List<IoTEnsembleTelemetryPayload> Payloads { get; set; }
    }

    [Serializable]
    [DataContract]
    public class IoTEnsembleTelemetryPayload : MetadataModel
    {
        [DataMember]
        public virtual long _ts { get; set; }

        [DataMember]
        public virtual MetadataModel DeviceData { get; set; }

        [DataMember]
        public virtual string DeviceID { get; set; }

        [DataMember]
        public virtual string DeviceType { get; set; }

        [DataMember]
        public virtual string EnterpriseLookup { get; set; }

        [DataMember]
        [JsonProperty("id")]
        public virtual string ID { get; set; }

        [DataMember]
        public virtual MetadataModel SensorMetadata { get; set; }

        [DataMember]
        public virtual MetadataModel SensorReadings { get; set; }

        [DataMember]
        public virtual DateTime Timestamp { get; set; }

        [DataMember]
        public virtual string Version { get; set; }
    }

    [Serializable]
    [DataContract]
    public class IoTEnsembleDrawersConfig
    {
        [DataMember]
        public virtual bool DetailsActive { get; set; }

        [DataMember]
        public virtual bool HasBackdrop { get; set; }

        [DataMember]
        public virtual bool NavActive { get; set; }
    }

    [Serializable]
    [DataContract]
    public class IoTEnsembleStorageConfiguration
    {
        [DataMember]
        public virtual List<IoTEnsembleAPIKeyData> APIKeys { get; set; }

        [DataMember]
        public virtual List<IoTEnsembleAPIOption> APIOptions { get; set; }
    }

    [Serializable]
    [DataContract]
    public class IoTEnsembleAPIKeyData
    {
        [DataMember]
        public virtual string Key { get; set; }

        [DataMember]
        public virtual string KeyName { get; set; }
    }

    [Serializable]
    [DataContract]
    public class IoTEnsembleAPIOption
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Method { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual string Path { get; set; }
    }

    [Serializable]
    [DataContract]
    public class IoTEnsembleEnterpriseReferenceData
    {
        [DataMember]
        public virtual int Devices { get; set; }

        [DataMember]
        public virtual int DataInterval { get; set; }

        [DataMember]
        public virtual int DataRetention { get; set; }

        [DataMember]
        public virtual string EnterpriseLookup { get; set; }
    }
}
