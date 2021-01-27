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

namespace LCU.State.API.IoTEnsemble.State
{
    public class IoTEnsembleAdminStateHarness : LCUStateHarness<IoTEnsembleAdminState>
    {
        #region Constants
        #endregion

        #region Fields
        protected readonly string deviceEnv;

        protected readonly string telemetryRoot;

        protected readonly string warmTelemetryContainer;

        protected readonly string warmTelemetryDatabase;
        #endregion

        #region Properties
        #endregion

        #region Constructors
        public IoTEnsembleAdminStateHarness(IoTEnsembleAdminState state, ILogger logger)
            : base(state ?? new IoTEnsembleAdminState(), logger)
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
        public virtual async Task LoadChildEnterprises(EnterpriseManagerClient entMgr, string parentEntLookup)
        {
            var childEnts = await entMgr.ListChildEnterprises(parentEntLookup);

            State.ChildEnterprises = childEnts.Model ?? new List<Graphs.Registry.Enterprises.Enterprise>();

            State.Loading = false;
        }
        
        public virtual async Task Refresh(EnterpriseManagerClient entMgr)
        {

            State.Loading = false;
        }
        #endregion

        #region Helpers
        #endregion
    }
}
