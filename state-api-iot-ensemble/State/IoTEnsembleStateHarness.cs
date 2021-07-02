using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Fathym;
using LCU.Presentation.State.ReqRes;
using LCU.StateAPI.Utilities;
using LCU.StateAPI;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;
using System.Collections.Generic;
using LCU.Personas.Client.Enterprises;
using LCU.Personas.Client.DevOps;
using LCU.Personas.Enterprises;
using LCU.Personas.Client.Applications;
using Fathym.API;
using LCU.Personas.Applications;
using LCU.Personas.Client.Security;
using System.Net.Http;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using LCU.Personas.Client.Identity;
using Microsoft.Azure.Storage.Blob;
using Newtonsoft.Json.Linq;
using System.Text;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net;
using CsvHelper;
using Fathym.Design;
using Gremlin.Net.Driver.Exceptions;
using LCU.State.API.IoTEnsemble.Host.TempRefit;

namespace LCU.State.API.IoTEnsemble.State
{
    public class IoTEnsembleStateHarness<TState> : LCUStateHarness<TState>
    {
        #region Fields
        protected readonly string deviceEnv;

        protected readonly string telemetryRoot;

        protected readonly string warmTelemetryContainer;

        protected readonly string warmTelemetryDatabase;
        #endregion

        #region Properties
        #endregion

        #region Constructors
        public IoTEnsembleStateHarness(TState state, ILogger logger)
            : base(state, logger)
        {
            deviceEnv = Environment.GetEnvironmentVariable("LCU-DEVICE-ENVIRONMENT") ?? String.Empty;

            telemetryRoot = Environment.GetEnvironmentVariable("LCU-TELEMETRY-ROOT");

            if (telemetryRoot.IsNullOrEmpty())
                telemetryRoot = String.Empty;

            warmTelemetryContainer = Environment.GetEnvironmentVariable("LCU-WARM-STORAGE-TELEMETRY-CONTAINER");

            warmTelemetryDatabase = Environment.GetEnvironmentVariable("LCU-WARM-STORAGE-DATABASE");
        }
        #endregion

        #region API Methods
        #endregion

        #region Helpers
        protected virtual async Task<Status> revokeDeviceEnrollment(IApplicationsIoTService appArch, string entLookup, string deviceId)
        {
            var status = Status.GeneralError;

            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var revokeResp = await appArch.RevokeDeviceEnrollment(deviceId, entLookup, envLookup: null);

                        status = revokeResp.Status;

                        return !status;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed revoking device enrollment");

                        status = Status.GeneralError.Clone(ex.ToString());

                        return true;
                    }
                })
                .SetCycles(5)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();

            await Task.Delay(2500);

            return status;
        }
        #endregion
    }
}
