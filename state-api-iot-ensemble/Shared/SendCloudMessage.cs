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
    public class SendCloudMessageRequest : BaseRequest
    {
        [DataMember]
        public virtual string DeviceName { get; set; }

        [DataMember]
        public virtual string Message { get; set; }
    }

    public class SendCloudMessage
    {
        protected ApplicationArchitectClient appArch;

        protected SecurityManagerClient secMgr;

        public SendCloudMessage(ApplicationArchitectClient appArch)
        {
            this.appArch = appArch;
        }

        [FunctionName("SendCloudMessage")]
        public virtual async Task<Status> Run([HttpTrigger] HttpRequest req, ILogger log,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob)
        {
            var status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, SendCloudMessageRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
                {
                    log.LogInformation($"Setting Loading device telemetry from UpdateTelemetrySync...");

                    harness.State.Telemetry.Loading = true;

                    return Status.Success;
                }, preventStatusException: true, withLock: false);

            if (status)
                status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, SendCloudMessageRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                    async (harness, dataReq, actReq) =>
                    {
                        log.LogInformation($"SendCloudMessage");

                        var stateDetails = StateUtils.LoadStateDetails(req);

                        await harness.SendCloudMessage(appArch, dataReq.DeviceName, dataReq.Message);

                        harness.State.Telemetry.Loading = false;

                        return Status.Success;
                    }, withLock: false);

            return status;
        }
    }
}
