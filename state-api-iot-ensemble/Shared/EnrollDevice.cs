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
using LCU.State.API.IoTEnsemble.Host.TempRefit;

namespace LCU.State.API.IoTEnsemble.Shared
{
    [Serializable]
    [DataContract]
    public class EnrollDeviceRequest : BaseRequest
    {
        [DataMember]
        public virtual IoTEnsembleDeviceEnrollment Device { get; set; }
    }

    public class EnrollDevice
    {
        protected IApplicationsIoTService appIotArch;

        protected ILogger log;

        public EnrollDevice(IApplicationsIoTService appIotArch, ILogger log)
        {
            this.appIotArch = appIotArch;

            this.log = log;
        }

        [FunctionName("EnrollDevice")]
        public virtual async Task<Status> Run([HttpTrigger] HttpRequest req,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob)
        {
            var status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, EnrollDeviceRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
                {
                    log.LogInformation($"Setting Loading for enroll device...");

                    harness.State.DevicesConfig.Loading = true;

                    return Status.Success;
                }, preventStatusException: true, withLock: false);

            if (status)
                status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, EnrollDeviceRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                    async (harness, enrollReq, actReq) =>
                    {
                        log.LogInformation($"EnrollDevice");

                        var stateDetails = StateUtils.LoadStateDetails(req);

                        await harness.EnrollDevice(appIotArch, enrollReq.Device);

                        harness.State.DevicesConfig.Loading = false;

                        return Status.Success;
                    }, withLock: false);

            return status;
        }
    }
}
