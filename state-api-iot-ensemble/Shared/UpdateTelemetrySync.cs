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
using System.Text;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.Documents.Client;
using LCU.State.API.IoTEnsemble.Host.TempRefit;

namespace LCU.State.API.IoTEnsemble.Shared
{
    [Serializable]
    [DataContract]
    public class UpdateTelemetrySyncRequest : BaseRequest
    {
        [DataMember]
        public virtual int RefreshRate { get; set; }

         [DataMember]
        public virtual int Page { get; set; }

        [DataMember]
        public virtual int PageSize { get; set; }
        
        [DataMember]
        public virtual string PayloadId { get; set; }
    }

    public class UpdateTelemetrySync
    {
        protected ILogger log;
        
        protected ISecurityDataTokenService secMgr;

        public UpdateTelemetrySync(ILogger log, ISecurityDataTokenService secMgr)
        {
            this.log = log;

            this.secMgr = secMgr;
        }

        [FunctionName("UpdateTelemetrySync")]
        public virtual async Task<Status> Run([HttpTrigger] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient starter,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob,
            [CosmosDB(
                databaseName: "%LCU-WARM-STORAGE-DATABASE%",
                collectionName: "%LCU-WARM-STORAGE-TELEMETRY-CONTAINER%",
                ConnectionStringSetting = "LCU-WARM-STORAGE-CONNECTION-STRING")]DocumentClient docClient)
        {
            var status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, UpdateTelemetrySyncRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
                {
                    log.LogInformation($"Setting Loading device telemetry from UpdateTelemetrySync...");

                    harness.State.Telemetry.Loading = true;

                    return Status.Success;
                }, preventStatusException: true, withLock: false);

            if (status)
                status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, UpdateTelemetrySyncRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                    async (harness, dataReq, actReq) =>
                    {
                        log.LogInformation($"UpdateTelemetrySync");

                        var stateDetails = StateUtils.LoadStateDetails(req);

                        await harness.UpdateTelemetrySync(secMgr, docClient, dataReq.RefreshRate, dataReq.Page, dataReq.PageSize, dataReq.PayloadId);

                        harness.State.Telemetry.Loading = false;

                        return Status.Success;
                    }, withLock: false);

            return status;
        }
    }
}
