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
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using LCU.Personas.Client.Identity;
using Microsoft.Azure.Documents.Client;
using LCU.State.API.IoTEnsemble.Host.TempRefit;

namespace LCU.State.API.IoTEnsemble.Host
{
    [Serializable]
    [DataContract]
    public class RefreshRequest : BaseRequest
    { }

    public class Refresh
    {
        protected IApplicationsIoTService appIotArch;

        protected IEnterprisesAPIManagementService entApiArch;

        protected IEnterprisesBootService entBootArch;

        protected IEnterprisesManagementService entMgr;

        protected IEnterprisesHostingManagerService entHostMgr;

        protected IIdentityManagerClient idMgr;

        protected SecurityManagerClient secMgr;

        public Refresh(IApplicationsIoTService appIotArch, IEnterprisesAPIManagementService entApiArch, IEnterprisesBootService entBootArch, IEnterprisesManagementService entMgr, IEnterprisesHostingManagerService entHostMgr, 
            IdentityManagerClient idMgr, SecurityManagerClient secMgr)
        {
            this.appIotArch = appIotArch;

            this.entApiArch = entApiArch;

            this.entBootArch = entBootArch;

            this.entMgr = entMgr;

            this.entHostMgr = entHostMgr;

            this.idMgr = idMgr;

            this.secMgr = secMgr;
        }

        [FunctionName("Refresh")]
        public virtual async Task<Status> Run([HttpTrigger] HttpRequest req, ILogger log,
            [DurableClient] IDurableOrchestrationClient starter,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob,
            [CosmosDB(
                databaseName: "%LCU-WARM-STORAGE-DATABASE%",
                collectionName: "%LCU-WARM-STORAGE-TELEMETRY-CONTAINER%",
                ConnectionStringSetting = "LCU-WARM-STORAGE-CONNECTION-STRING")]DocumentClient docClient)
        {
            var stateDetails = StateUtils.LoadStateDetails(req);

            if (stateDetails.StateKey.StartsWith("admin"))
                return await stateBlob.WithStateHarness<IoTEnsembleAdminState, RefreshRequest, IoTEnsembleAdminStateHarness>(req, signalRMessages, log,
                    async (harness, refreshReq, actReq) =>
                {
                    log.LogInformation($"Refreshing admin state");

                    var stateDetails = StateUtils.LoadStateDetails(req);

                    await harness.Refresh(appIotArch, entMgr, idMgr, stateDetails.EnterpriseLookup);

                    return Status.Success;
                }, withLock: false);
            else
                return await stateBlob.WithStateHarness<IoTEnsembleSharedState, RefreshRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                    async (harness, refreshReq, actReq) =>
                {
                    log.LogInformation($"Refreshing Shared State");

                    var stateDetails = StateUtils.LoadStateDetails(req);

                    await harness.Refresh(starter, stateDetails, actReq, appIotArch, entApiArch, entBootArch, entHostMgr, idMgr, secMgr, docClient);

                    return Status.Success;
                }, withLock: false);
        }
    }
}
