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
using LCU.Graphs.Registry.Enterprises.Identity;

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
        public virtual async Task LoadChildEnterprises(EnterpriseManagerClient entMgr, string parentEntLookup,
            ApplicationArchitectClient appArch, IdentityManagerClient idMgr)
        {
            var childEntsResp = await entMgr.ListChildEnterprises(parentEntLookup);

            State.EnterpriseConfig.TotalChildEnterprisesCount = childEntsResp.Model?.Count;

            var pagedChildEnts = childEntsResp.Model?.Page(State.EnterpriseConfig.Page, State.EnterpriseConfig.PageSize);

            var iotChildEnts = new List<IoTEnsembleChildEnterprise>();

            await pagedChildEnts.Items.Each(async childEnt =>
            {

                var devicesResp = await appArch.ListEnrolledDevices(childEnt.EnterpriseLookup);

                var licenses = await idMgr.ListLicenseAccessTokens(parentEntLookup, childEnt.Name, new List<string>() { "iot" });

                DateTime? StartDate = null;

                foreach (LicenseAccessToken token in licenses.Model)
                {
                    if (token.AccessStartDate != null)
                    {

                        StartDate = token.AccessStartDate.UtcDateTime;

                    }

                }

                var iotChildEnt = new IoTEnsembleChildEnterprise()
                {
                    Name = childEnt.Name,
                    Lookup = childEnt.EnterpriseLookup,
                    DeviceCount = devicesResp.Model?.TotalRecords ?? 0,
                    SignUpDate = StartDate

                };

                iotChildEnt.Devices = devicesResp.Model?.Items?.Select(device =>
                {
                    var devInfo = device.JSONConvert<IoTEnsembleDeviceInfo>();

                    devInfo.DeviceName = devInfo.DeviceID.Replace($"{childEnt.EnterpriseLookup}-", String.Empty);

                    return devInfo;
                }).ToList();

                iotChildEnts.Add(iotChildEnt);
            });

            State.EnterpriseConfig.ChildEnterprises = iotChildEnts;

            var activeEnt = State.EnterpriseConfig.ChildEnterprises.FirstOrDefault(ce => ce.Lookup == State.EnterpriseConfig.ActiveEnterpriseLookup);

            if (activeEnt == null)
                State.EnterpriseConfig.ActiveEnterpriseLookup = null;

        }

        public virtual async Task LoadActiveEnterpriseDetails(ApplicationArchitectClient appArch)
        {
            if (!State.EnterpriseConfig.ActiveEnterpriseLookup.IsNullOrEmpty())
            {
                var enrolledDevices = await appArch.ListEnrolledDevices(State.EnterpriseConfig.ActiveEnterpriseLookup);

                if (enrolledDevices.Status)
                {
                    var activeEnt = State.EnterpriseConfig.ChildEnterprises.FirstOrDefault(ce => ce.Lookup == State.EnterpriseConfig.ActiveEnterpriseLookup);

                    activeEnt.Devices = enrolledDevices.Model.Items.Select(m =>
                    {
                        var devInfo = m.JSONConvert<IoTEnsembleDeviceInfo>();

                        devInfo.DeviceName = devInfo.DeviceID.Replace($"{State.EnterpriseConfig.ActiveEnterpriseLookup}-", String.Empty);

                        return devInfo;

                    }).ToList();

                    activeEnt.DeviceCount = enrolledDevices.Model.TotalRecords;


                }
                else
                {
                    log.LogError($"Unable to load LoadActiveEnterpriseDetails: {enrolledDevices.Status}");
                }
            }
        }

        public virtual async Task Refresh(ApplicationArchitectClient appArch, EnterpriseManagerClient entMgr,
            IdentityManagerClient idMgr, string parentEntLookup)
        {
            await LoadChildEnterprises(entMgr, parentEntLookup, appArch, idMgr);

            await LoadActiveEnterpriseDetails(appArch);

            State.Loading = false;
        }

        public virtual async Task<bool> RevokeDeviceEnrollment(ApplicationArchitectClient appArch, EnterpriseManagerClient entMgr,
            IdentityManagerClient idMgr, string parentEntLookup, string deviceId)
        {
            var revoked = await revokeDeviceEnrollment(appArch, State.EnterpriseConfig.ActiveEnterpriseLookup, deviceId);

            await LoadChildEnterprises(entMgr, parentEntLookup, appArch, idMgr);

            return revoked;
        }

        public virtual async Task SetActiveEnterprise(ApplicationArchitectClient appArch, string entLookup)
        {
            State.EnterpriseConfig.ActiveEnterpriseLookup = entLookup;

            await LoadActiveEnterpriseDetails(appArch);

        }

        public virtual async Task UpdateEnterprisesSync(EnterpriseManagerClient entMgr,
            ApplicationArchitectClient appArch, IdentityManagerClient idMgr, string parentEntLookup, int page, int pageSize)
        {

            if (State.EnterpriseConfig != null)
            {

                State.EnterpriseConfig.Page = page;

                State.EnterpriseConfig.PageSize = pageSize;

                await LoadChildEnterprises(entMgr, parentEntLookup, appArch, idMgr);


            }

            else
                throw new Exception("Unable to load the enterprise config, please try again or contact support.");
        }
        #endregion

        #region Helpers
        #endregion
    }
}
