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
    public class IoTEnsembleAdminStateHarness : IoTEnsembleStateHarness<IoTEnsembleAdminState>
    {
        #region Constants
        #endregion

        #region Fields
        #endregion

        #region Properties
        #endregion

        #region Constructors
        public IoTEnsembleAdminStateHarness(IoTEnsembleAdminState state, ILogger logger)
            : base(state ?? new IoTEnsembleAdminState(), logger)
        { }
        #endregion

        #region API Methods
        public virtual async Task LoadChildEnterprises(EnterpriseManagerClient entMgr, string parentEntLookup)
        {
            var childEntsResp = await entMgr.ListChildEnterprises(parentEntLookup);

            var childEnts = childEntsResp.Model ?? new List<Graphs.Registry.Enterprises.Enterprise>();

            State.Enterprise.ChildEnterprises = childEnts.Select(ce => new IoTEnsembleChildEnterprise()
            {
                Name = ce.Name
            }).ToList();

            State.Loading = false;
        }

        public virtual async Task LoadActiveEnterpriseDetails(ApplicationArchitectClient appArch)
        {
            if (!State.Enterprise.ActiveEnterpriseLookup.IsNullOrEmpty())
            {
                var enrolledDevices = await appArch.ListEnrolledDevices(State.Enterprise.ActiveEnterpriseLookup);

                if (enrolledDevices.Status)
                {
                    var activeEnt = State.Enterprise.ChildEnterprises.FirstOrDefault(ce => ce.Lookup == State.Enterprise.ActiveEnterpriseLookup);

                    // activeEnt.Devices = enrolledDevices.Model.Items.Select(..see shared logic..).ToList();

                    activeEnt.DeviceCount = enrolledDevices.Model.TotalRecords;
                }
                else
                {
                    log.LogError($"Unable to load LoadActiveEnterpriseDetails: {enrolledDevices.Status}");
                }
            }
        }

        public virtual async Task Refresh(ApplicationArchitectClient appArch, EnterpriseManagerClient entMgr, string parentEntLookup)
        {
            await LoadChildEnterprises(entMgr, parentEntLookup);

            await LoadActiveEnterpriseDetails(appArch);

            State.Loading = false;
        }

        public virtual async Task SetActiveEnterprise(ApplicationArchitectClient appArch, string entLookup)
        {
            State.Enterprise.ActiveEnterpriseLookup = entLookup;

            await LoadActiveEnterpriseDetails(appArch);

            State.Loading = false;
        }
        #endregion

        #region Helpers
        #endregion
    }
}
