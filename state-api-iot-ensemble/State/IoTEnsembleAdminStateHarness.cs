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
using LCU.State.API.IoTEnsemble.Host.TempRefit;

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

        public virtual async Task LoadChildEnterprises(IEnterprisesManagementService entMgr, string parentEntLookup,
            IApplicationsIoTService appIoTArch, IIdentityAccessService idMgr)
        {
            var childEntsResp = await entMgr.ListChildEnterprises(parentEntLookup);

            State.EnterpriseConfig.TotalChildEnterprisesCount = childEntsResp.Model?.Count;

            var pagedChildEnts = childEntsResp.Model?.Page(State.EnterpriseConfig.Page, State.EnterpriseConfig.PageSize);

            var iotChildEnts = new List<IoTEnsembleChildEnterprise>();

            await pagedChildEnts.Items.Each(async childEnt =>
            {
                
                var devicesResp = await appIoTArch.ListEnrolledDevices(childEnt.Lookup);

                var licenses = await idMgr.ListLicensesByUsername(parentEntLookup, childEnt.Name, new List<string>() { "iot" });

                DateTime? StartDate = null;

                foreach (License token in licenses.Model)
                {
                    if (token.AccessStartDate != null)
                    {

                        StartDate = token.AccessStartDate.UtcDateTime;

                    }

                }

                var iotChildEnt = new IoTEnsembleChildEnterprise()
                {
                    Name = childEnt.Name,
                    Lookup = childEnt.Lookup,
                    DeviceCount = devicesResp.Model?.TotalRecords ?? 0,
                    SignUpDate = StartDate

                };

                iotChildEnt.Devices = devicesResp.Model?.Items?.Select(device =>
                {
                    var devInfo = device.JSONConvert<IoTEnsembleDeviceInfo>();

                    devInfo.DeviceName = devInfo.DeviceID.Replace($"{childEnt.Lookup}-", String.Empty);

                    return devInfo;
                }).ToList();

                iotChildEnts.Add(iotChildEnt);
            });

            State.EnterpriseConfig.ChildEnterprises = iotChildEnts;

        }

        public virtual async Task LoadActiveEnterpriseDetails(IApplicationsIoTService appIoTArch, int page, int pageSize)
        {

            if (State.ActiveEnterpriseConfig?.ActiveEnterprise?.Lookup != null)
            {
                var enrolledDevices = await appIoTArch.ListEnrolledDevices(State.ActiveEnterpriseConfig.ActiveEnterprise.Lookup, envLookup: null, page: page, pageSize: pageSize);

                if (enrolledDevices.Status)
                {

                    State.ActiveEnterpriseConfig.ActiveEnterprise.Devices = enrolledDevices.Model.Items.Select(m =>
                    {
                        var devInfo = m.JSONConvert<IoTEnsembleDeviceInfo>();

                        devInfo.DeviceName = devInfo.DeviceID.Replace($"{State.ActiveEnterpriseConfig.ActiveEnterprise.Lookup}-", String.Empty);

                        return devInfo;

                    }).ToList();

                    State.ActiveEnterpriseConfig.ActiveEnterprise.DeviceCount = enrolledDevices.Model.TotalRecords;

                }
                else
                {
                    log.LogError($"Unable to load LoadActiveEnterpriseDetails: {enrolledDevices.Status}");
                }
            }
        }

        public virtual async Task Refresh(IApplicationsIoTService appIoTArch, IEnterprisesManagementService entMgr,
            IIdentityAccessService idMgr, string parentEntLookup)
        {
            await LoadChildEnterprises(entMgr, parentEntLookup, appIoTArch, idMgr);

            await LoadActiveEnterpriseDetails(appIoTArch, State.ActiveEnterpriseConfig.Page,State.ActiveEnterpriseConfig.PageSize );

            State.Loading = false;
        }

        public virtual async Task<Status> RemoveChildEnterprise(IApplicationsIoTService appIoTArch, 
            IEnterprisesManagementService entMgr, IIdentityAccessService idMgr, 
            string childEntLookup, string parentEntLookup)
        {
            var childEnt = State.EnterpriseConfig.ChildEnterprises.FirstOrDefault(ent => 
                ent.Lookup == childEntLookup
            );
            var devices = await appIoTArch.ListEnrolledDevices(childEntLookup);
          
            //Remove devices
            
                await devices.Model.Items.Each(async d =>{

                    await revokeDeviceEnrollment(appIoTArch, childEntLookup, d.DeviceID);

                }, parallel: true);
            

            //If its the active ent set active to null
            if(State.ActiveEnterpriseConfig.ActiveEnterprise != null && State.ActiveEnterpriseConfig?.ActiveEnterprise.Lookup == childEntLookup)
            {
                State.ActiveEnterpriseConfig.ActiveEnterprise = null;
            }

            var revokePassportRequest = await idMgr.RevokePassport(parentEntLookup, childEnt.Name);

            var revokeAccessCardRequest = await idMgr.RevokeAccessCard(new Host.TempRefit.RevokeAccessCardRequest(){
                AccessConfiguration = "LCU",
                Username = childEnt.Name
            }, childEntLookup);

            if(revokeAccessCardRequest.Status.Code == 1){
                log.LogError($"Unable to revoke access cards: {revokeAccessCardRequest.Status.Message}");
            }

            var revokeLicenceAccess = await idMgr.RevokeLicense(parentEntLookup, childEnt.Name, "iot" );

            if(revokeLicenceAccess.Status.Code == 1){
                log.LogError($"Unable to revoke license access: {revokeLicenceAccess.Status.Message}");
            }

            //TODO removing the API Management keys

            var cancelUserSubscription = await entMgr.CancelSubscriptionByUser(childEnt.Name, parentEntLookup, "iot");

            if(cancelUserSubscription.Status.Code == 1){
                log.LogError($"Unable to cancel subscription: {cancelUserSubscription.Status.Message}");
            }

            var deleteRequest = await entMgr.DeleteEnterpriseByLookup(childEntLookup, new Host.TempRefit.DeleteEnterpriseByLookupRequest(){
                Password= "F@thym!t"
            });

            await LoadChildEnterprises(entMgr, parentEntLookup, appIoTArch, idMgr);
            
            return Status.Success;
        }

        public virtual async Task<Status> RevokeDeviceEnrollment(IApplicationsIoTService appIoTArch, IEnterprisesManagementService entMgr,
            IIdentityAccessService idMgr, string parentEntLookup, string deviceId)
        {
            var revoked = await revokeDeviceEnrollment(appIoTArch, State.ActiveEnterpriseConfig.ActiveEnterprise.Lookup, deviceId);

            await LoadChildEnterprises(entMgr, parentEntLookup, appIoTArch, idMgr);

            return revoked;
        }

        public virtual async Task SetActiveEnterprise(IApplicationsIoTService appIoTArch, string entLookup)
        {
            State.ActiveEnterpriseConfig.ActiveEnterprise = State.EnterpriseConfig.ChildEnterprises.FirstOrDefault(ent => 
                ent.Lookup == entLookup
            );

            State.ActiveEnterpriseConfig.Page = 1;
                

            await LoadActiveEnterpriseDetails(appIoTArch, State.ActiveEnterpriseConfig.Page,State.ActiveEnterpriseConfig.PageSize );

        }

        public virtual async Task UpdateActiveEnterpriseSync(IApplicationsIoTService appIoTArch, IIdentityAccessService idMgr, string parentEntLookup, 
            int page, int pageSize)
        {

            if (State.ActiveEnterpriseConfig != null)
            {

                State.ActiveEnterpriseConfig.Page = page;

                State.ActiveEnterpriseConfig.PageSize = pageSize;

                await LoadActiveEnterpriseDetails(appIoTArch, State.ActiveEnterpriseConfig.Page, State.ActiveEnterpriseConfig.PageSize );


            }

            else
                throw new Exception("Unable to load the enterprise config, please try again or contact support.");
        }

        public virtual async Task UpdateEnterprisesSync(IEnterprisesManagementService entMgr,
            IApplicationsIoTService appIoTArch, IIdentityAccessService idMgr, string parentEntLookup, int page, int pageSize)
        {

            if (State.EnterpriseConfig != null)
            {

                State.EnterpriseConfig.Page = page;

                State.EnterpriseConfig.PageSize = pageSize;

                await LoadChildEnterprises(entMgr, parentEntLookup, appIoTArch, idMgr);

                if(State.ActiveEnterpriseConfig != null)
                {
                await LoadActiveEnterpriseDetails(appIoTArch, State.ActiveEnterpriseConfig.Page, State.ActiveEnterpriseConfig.PageSize);
                }
            }

            else
                throw new Exception("Unable to load the enterprise config, please try again or contact support.");
        }
        #endregion

        #region Helpers
        #endregion
    }
}
