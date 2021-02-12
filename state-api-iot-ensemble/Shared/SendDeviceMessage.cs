using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using Fathym;
using Microsoft.Azure.Storage.Blob;
using System.Runtime.Serialization;
using Fathym.API;
using System.Collections.Generic;
using System.Linq;
using LCU.Personas.Client.Applications;
using LCU.StateAPI.Utilities;
using System.Security.Claims;
using LCU.Personas.Client.Enterprises;
using LCU.State.API.IoTEnsemble.State;
using LCU.Personas.Client.Security;
using Microsoft.Azure.Documents.Client;

namespace LCU.State.API.IoTEnsemble.Shared
{
    [Serializable]
    [DataContract]
    public class SendDeviceMessageRequest : BaseRequest
    {
        [DataMember]
        public virtual string DeviceName { get; set; }

        [DataMember]
        public virtual MetadataModel Payload { get; set; }
    }

    public class SendDeviceMessage
    {
        protected ApplicationArchitectClient appArch;

        protected SecurityManagerClient secMgr;

        public SendDeviceMessage(ApplicationArchitectClient appArch, SecurityManagerClient secMgr)
        {
            this.appArch = appArch;

            this.secMgr = secMgr;
        }

        [FunctionName("SendDeviceMessage")]
        public virtual async Task<Status> Run([HttpTrigger] HttpRequest req, ILogger log,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob,
            [CosmosDB(
                databaseName: "%LCU-WARM-STORAGE-DATABASE%",
                collectionName: "%LCU-WARM-STORAGE-TELEMETRY-CONTAINER%",
                ConnectionStringSetting = "LCU-WARM-STORAGE-CONNECTION-STRING")]DocumentClient docClient)
        {
            var status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, SendDeviceMessageRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
                {
                    log.LogInformation($"Setting Loading device telemetry from UpdateTelemetrySync...");

                    harness.State.Telemetry.Loading = true;

                    return Status.Success;
                }, preventStatusException: true, withLock: false);

            if (status)
                status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, SendDeviceMessageRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                    async (harness, dataReq, actReq) =>
                    {
                        log.LogInformation($"SendDeviceMessage");

                        var stateDetails = StateUtils.LoadStateDetails(req);

                        await harness.SendDeviceMessage(appArch, secMgr, docClient, dataReq.DeviceName, dataReq.Payload);

                        harness.State.Telemetry.Loading = false;

                        return Status.Success;
                    }, withLock: false);

            return status;
        }
    }
}
