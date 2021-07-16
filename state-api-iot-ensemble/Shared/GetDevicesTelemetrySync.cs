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
using System.Net.Http;
using System.Net;

namespace LCU.State.API.IoTEnsemble.Shared
{
    [Serializable]
    [DataContract]
    public class GetDevicesTelemetrySyncRequest : BaseRequest
    {
        [DataMember]
        public virtual IoTEnsembleDeviceEnrollment Device { get; set; }
    }

    public class GetDevicesTelemetrySync
    {
        protected ILogger log;

        public GetDevicesTelemetrySync(ILogger log){
            this.log = log;
        }

        [FunctionName("GetDevicesTelemetrySync")]
        public virtual async Task<HttpResponseMessage> Run([HttpTrigger] HttpRequest req,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/iotensemble/{headers.x-ms-client-principal-id}/shared", FileAccess.ReadWrite)] CloudBlockBlob stateBlob)
        {
            List<IoTEnsembleTelemetryPayload> payloads = null;

            req.Headers.Add("lcu-hub-name", "iotensemble");

            req.Headers.Add("lcu-state-key", "shared");

            var status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, GetDevicesTelemetrySyncRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                async (harness, enrollReq, actReq) =>
            {
                log.LogInformation($"GetDevicesTelemetrySync");

                var stateDetails = StateUtils.LoadStateDetails(req);

                payloads = harness.State.Telemetry?.Payloads?.OrderByDescending(p => p.Timestamp).ToList() ?? new List<IoTEnsembleTelemetryPayload>();

                return Status.Success;
            }, withLock: false);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    content: payloads.ToJSON(),
                    encoding: System.Text.Encoding.UTF8,
                    mediaType: "application/json")
            };
        }
    }
}
