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
using LCU.State.API.IoTEnsemble.Shared;
using LCU.State.API.IoTEnsemble.Host.TempRefit;

namespace LCU.State.API.IoTEnsemble.Host
{
    [Serializable]
    [DataContract]
    public class RevokeDeviceEnrollmentRequest : BaseRequest
    {
        [DataMember]
        public virtual string DeviceID { get; set; }
    }

    public class RevokeDeviceEnrollment
    {
        protected IApplicationsIoTService appIotArch;

        protected IEnterprisesManagementService entMgr;

        protected IIdentityManagerClient idMgr;

        public RevokeDeviceEnrollment(IApplicationsIoTService appIotArch, IEnterprisesManagementService entMgr,
            IIdentityManagerClient idMgr)
        {
            this.appIotArch = appIotArch;

            this.entMgr = entMgr;

            this.idMgr = idMgr;
        }

        [FunctionName("RevokeDeviceEnrollment")]
        public virtual async Task<Status> Run([HttpTrigger] HttpRequest req, ILogger log,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob)
        {
            var stateDetails = StateUtils.LoadStateDetails(req);

            if (stateDetails.StateKey.StartsWith("admin"))
                return await handleAdminRevoke(req, log, signalRMessages, stateBlob);
            else
                return await handleSharedRevoke(req, log, signalRMessages, stateBlob);
        }

        protected virtual async Task<Status> handleAdminRevoke(HttpRequest req, ILogger log,
            IAsyncCollector<SignalRMessage> signalRMessages, CloudBlockBlob stateBlob)
        {
            var status = await stateBlob.WithStateHarness<IoTEnsembleAdminState, UpdateTelemetrySyncRequest, IoTEnsembleAdminStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
                {
                    log.LogInformation($"Setting Loading device telemetry from UpdateTelemetrySync...");

                    harness.State.Loading = true;

                    return Status.Success;
                }, preventStatusException: true, withLock: false);

            if (status)
                status = await stateBlob.WithStateHarness<IoTEnsembleAdminState, RevokeDeviceEnrollmentRequest, IoTEnsembleAdminStateHarness>(req, signalRMessages, log,
                    async (harness, enrollReq, actReq) =>
                    {
                        log.LogInformation($"RevokeDeviceEnrollment");

                        var stateDetails = StateUtils.LoadStateDetails(req);

                        await harness.RevokeDeviceEnrollment(appIotArch, entMgr, idMgr, stateDetails.EnterpriseLookup, enrollReq.DeviceID);

                        harness.State.Loading = false;

                        return Status.Success;
                    }, withLock: false);

            return status;
        }

        protected virtual async Task<Status> handleSharedRevoke(HttpRequest req, ILogger log,
            IAsyncCollector<SignalRMessage> signalRMessages, CloudBlockBlob stateBlob)
        {
            var status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, UpdateTelemetrySyncRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
                {
                    log.LogInformation($"Setting Loading device telemetry from UpdateTelemetrySync...");

                    harness.State.DevicesConfig.Loading = true;

                    return Status.Success;
                }, preventStatusException: true, withLock: false);

            if (status)
                status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, RevokeDeviceEnrollmentRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                    async (harness, enrollReq, actReq) =>
                    {
                        log.LogInformation($"RevokeDeviceEnrollment");

                        var stateDetails = StateUtils.LoadStateDetails(req);

                        await harness.RevokeDeviceEnrollment(appIotArch, enrollReq.DeviceID);

                        harness.State.DevicesConfig.Loading = false;

                        return Status.Success;
                    }, withLock: false);

            return status;
        }
    }
}
