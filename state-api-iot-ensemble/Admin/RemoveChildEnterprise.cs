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

namespace LCU.State.API.IoTEnsemble.Admin
{
    [Serializable]
    [DataContract]
    public class RemoveChildEnterpriseRequest : BaseRequest
    {
        [DataMember]
        public virtual string ChildEntLookup { get; set; }
    }

    public class RemoveChildEnterprise
    {
        protected ApplicationArchitectClient appArch;

        protected EnterpriseArchitectClient entArch;

        public RemoveChildEnterprise(ApplicationArchitectClient appArch, EnterpriseArchitectClient entArch)
        {
            this.appArch = appArch;

            this.entArch = entArch;
         }

        [FunctionName("RemoveChildEnterprise")]
        public virtual async Task<Status> Run([HttpTrigger] HttpRequest req, ILogger log,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob)
        {
            return await stateBlob.WithStateHarness<IoTEnsembleAdminState, RemoveChildEnterpriseRequest, IoTEnsembleAdminStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
            {
                log.LogInformation($"RemoveChildEnterprise");

                await harness.RemoveChildEnterprise(appArch, entArch, dataReq.ChildEntLookup);

                return Status.Success;
            }, withLock: false);
        }
    }
}
