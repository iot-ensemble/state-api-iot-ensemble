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
using LCU.Personas.Client.Identity;
using LCU.State.API.IoTEnsemble.Host.TempRefit;

namespace LCU.State.API.IoTEnsemble.Admin
{
    [Serializable]
    [DataContract]
    public class UpdateActiveEnterpriseSyncRequest : BaseRequest
    {
        [DataMember]
        public virtual int Page { get; set; }
        
        [DataMember]
        public virtual int PageSize { get; set; }
    }

    public class UpdateActiveEnterpriseSync
    {
        protected IApplicationsIoTService appIoTArch;

        protected IIdentityAccessService idMgr;

        protected ILogger log;
        
        public UpdateActiveEnterpriseSync(IApplicationsIoTService appIoTArch, IIdentityAccessService idMgr, ILogger<UpdateActiveEnterpriseSync> log)
        {
            this.appIoTArch = appIoTArch;

            this.idMgr = idMgr;

            this.log = log;
         }

        [FunctionName("UpdateActiveEnterpriseSync")]
        public virtual async Task<Status> Run([HttpTrigger] HttpRequest req,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob)
        {
            return await stateBlob.WithStateHarness<IoTEnsembleAdminState, UpdateActiveEnterpriseSyncRequest, IoTEnsembleAdminStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
            {
                log.LogInformation($"UpdateActiveEnterpriseSync");

                var stateDetails = StateUtils.LoadStateDetails(req);

                await harness.UpdateActiveEnterpriseSync(appIoTArch, idMgr, stateDetails.EnterpriseLookup, dataReq.Page, dataReq.PageSize);

                return Status.Success;
            }, withLock: false);
        }
    }
}
