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

namespace LCU.State.API.IoTEnsemble.Shared
{
    [Serializable]
    [DataContract]
    public class IssueDeviceSASTokenRequest : BaseRequest
    {
        [DataMember]
        public virtual string DeviceName { get; set; }

        [DataMember]
        public virtual int ExpiryInSeconds { get; set; }
    }

    public class IssueDeviceSASToken
    {
        protected ApplicationArchitectClient appArch;

        public IssueDeviceSASToken(ApplicationArchitectClient appArch)
        {
            this.appArch = appArch;
        }

        [FunctionName("IssueDeviceSASToken")]
        public virtual async Task<Status> Run([HttpTrigger] HttpRequest req, ILogger log,
            [SignalR(HubName = IoTEnsembleSharedState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob)
        {
            var status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, UpdateTelemetrySyncRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
                {
                    log.LogInformation($"Setting Loading device telemetry from UpdateTelemetrySync...");

                    harness.State.DevicesConfig.Loading = true;

                    return Status.Success;
                }, preventStatusException: true);

            if (status)
                status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, IssueDeviceSASTokenRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                    async (harness, dataReq, actReq) =>
                    {
                        log.LogInformation($"IssueDeviceSASToken");

                        await harness.IssueDeviceSASToken(appArch, dataReq.DeviceName, dataReq.ExpiryInSeconds);

                        harness.State.DevicesConfig.Loading = false;

                        return Status.Success;
                    });

            return status;
        }
    }
}
