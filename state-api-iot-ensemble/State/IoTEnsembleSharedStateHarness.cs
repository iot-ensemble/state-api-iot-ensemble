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
    public class IoTEnsembleSharedStateHarness : LCUStateHarness<IoTEnsembleSharedState>
    {
        #region Constants
        const string DETAILS_PANE_ENABLED = "IoTEnsemble:DetailsPaneEnabled";

        const string EMULATED_DEVICE_ENABLED = "IoTEnsemble:EmulatedDeviceEnabled";

        const string DEVICE_DASHBOARD_FREEBOARD_CONFIG = "IoTEnsemble:DeviceDashboardFreeboardConfig";

        const string TELEMETRY_SYNC_ENABLED = "IoTEnsemble:TelemetrySyncEnabled";
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
        public IoTEnsembleSharedStateHarness(IoTEnsembleSharedState state, ILogger logger)
            : base(state ?? new IoTEnsembleSharedState(), logger)
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
        public virtual async Task<bool> EnrollDevice(ApplicationArchitectClient appArch, IoTEnsembleDeviceEnrollment device)
        {
            var enrollResp = new EnrollDeviceResponse();

            var deviceId = $"{State.UserEnterpriseLookup}-{device.DeviceName}";

            if (State.Devices.Devices.Count() < State.Devices.MaxDevicesCount)
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            log.LogInformation($"Enrolling new device with id {deviceId}");

                            enrollResp = await appArch.EnrollDevice(new EnrollDeviceRequest()
                            {
                                DeviceID = deviceId,
                                EnrollmentOptions = new
                                {
                                    Tags = new Dictionary<string, string>()
                                    {
                                        { "Environment", deviceEnv }
                                    }
                                }.JSONConvert<MetadataModel>()
                            }, State.UserEnterpriseLookup, DeviceAttestationTypes.SymmetricKey, DeviceEnrollmentTypes.Individual, envLookup: null);

                            State.Devices.Status = enrollResp.Status;

                            log.LogInformation($"Enroll device status {State.Devices.Status.ToJSON()}");

                            return !State.Devices.Status;
                        }
                        catch (Exception ex)
                        {
                            log.LogError(ex, "Failed enrollling device");

                            return true;
                        }
                    })
                    .SetCycles(5)
                    .SetThrottle(25)
                    .SetThrottleScale(2)
                    .Run();
            }
            else
            {
                State.Devices.Status = Status.Conflict.Clone("Max Device Count Reached");

                log.LogInformation($"Max Device Count Reached while enrolling {deviceId}");
            }

            await LoadDevices(appArch);

            return false;
        }

        public virtual async Task<Status> EnsureAPISubscription(EnterpriseArchitectClient entArch, string entLookup, string username)
        {
            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var resp = await entArch.EnsureAPISubscription(new EnsureAPISubscriptionRequset()
                        {
                            SubscriptionType = $"{State.AccessLicenseType}-{State.AccessPlanGroup}".ToLower()
                        }, entLookup, username);

                        //  TODO:  Handle API error

                        return !resp.Status;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed ensuring API subscription");

                        return true;
                    }
                })
                .SetCycles(5)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();

            return await LoadAPIKeys(entArch, entLookup, username);
        }

        public virtual async Task EnsureDevicesDashboard(SecurityManagerClient secMgr)
        {
            if (State.Dashboard == null)
                State.Dashboard = new IoTEnsembleDashboardConfiguration();

            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                if (State.Dashboard.FreeboardConfig == null)
                {
                    await DesignOutline.Instance.Retry()
                        .SetActionAsync(async () =>
                        {
                            try
                            {
                                var tpd = await secMgr.RetrieveEnterpriseThirdPartyData(State.UserEnterpriseLookup, DEVICE_DASHBOARD_FREEBOARD_CONFIG);

                                if (tpd.Status && tpd.Model.ContainsKey(DEVICE_DASHBOARD_FREEBOARD_CONFIG) && !tpd.Model[DEVICE_DASHBOARD_FREEBOARD_CONFIG].IsNullOrEmpty())
                                    State.Dashboard.FreeboardConfig = tpd.Model[DEVICE_DASHBOARD_FREEBOARD_CONFIG].FromJSON<MetadataModel>();

                                if (State.Dashboard.FreeboardConfig != null)
                                    return State.Dashboard.FreeboardConfig == null;
                            }
                            catch (Exception ex)
                            {
                                log.LogError(ex, "Failed loading freeboard");
                            }

                            if (State.Dashboard.FreeboardConfig == null)
                            {
                                try
                                {
                                    var freeboardConfig = loadDefaultFreeboardConfig();

                                    var resp = await secMgr.SetEnterpriseThirdPartyData(State.UserEnterpriseLookup, new Dictionary<string, string>()
                                    {
                                        { DEVICE_DASHBOARD_FREEBOARD_CONFIG, freeboardConfig.ToJSON() }
                                    });

                                    if (resp.Status)
                                        State.Dashboard.FreeboardConfig = freeboardConfig;
                                }
                                catch (Exception ex)
                                {
                                    log.LogError(ex, "Failed setting default freeboard");
                                }
                            }

                            return State.Dashboard.FreeboardConfig == null;
                        })
                        .SetCycles(5)
                        .SetThrottle(25)
                        .SetThrottleScale(2)
                        .Run();
                }
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        public virtual async Task EnsureDrawersConfig(SecurityManagerClient secMgr)
        {
            if (State.Drawers == null)
                State.Drawers = new IoTEnsembleDrawersConfig();

            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            var tpd = await secMgr.RetrieveEnterpriseThirdPartyData(State.UserEnterpriseLookup, DETAILS_PANE_ENABLED);

                            if (tpd.Status && tpd.Model.ContainsKey(DETAILS_PANE_ENABLED) && !tpd.Model[DETAILS_PANE_ENABLED].IsNullOrEmpty())
                                State.Drawers.DetailsActive = tpd.Model[DETAILS_PANE_ENABLED].As<bool>();
                            else
                            {
                                var resp = await secMgr.SetEnterpriseThirdPartyData(State.UserEnterpriseLookup, new Dictionary<string, string>()
                                {
                                    { DETAILS_PANE_ENABLED, true.ToString() }
                                });

                                if (resp.Status)
                                    State.Drawers.DetailsActive = true;
                                else
                                    return true;
                            }

                            return false;
                        }
                        catch (Exception ex)
                        {
                            log.LogError(ex, "Failed setting details pane");

                            return true;
                        }
                    })
                    .SetCycles(5)
                    .SetThrottle(25)
                    .SetThrottleScale(2)
                    .Run();
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        public virtual async Task EnsureEmulatedDeviceInfo(IDurableOrchestrationClient starter, StateDetails stateDetails,
            ExecuteActionRequest exActReq, SecurityManagerClient secMgr, DocumentClient client)
        {
            if (State.Emulated == null)
                State.Emulated = new EmulatedDeviceInfo();

            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            var tpd = await secMgr.RetrieveEnterpriseThirdPartyData(State.UserEnterpriseLookup, EMULATED_DEVICE_ENABLED);

                            if (tpd.Status && tpd.Model.ContainsKey(EMULATED_DEVICE_ENABLED) && !tpd.Model[EMULATED_DEVICE_ENABLED].IsNullOrEmpty())
                                State.Emulated.Enabled = tpd.Model[EMULATED_DEVICE_ENABLED].As<bool>();
                            else
                            {
                                State.Emulated.Enabled = true;

                                await ToggleEmulatedEnabled(starter, stateDetails, exActReq, secMgr, client, skipTelem: true);
                            }

                            return false;
                        }
                        catch (Exception ex)
                        {
                            log.LogError(ex, "Failed setting emulated device enabled");

                            return true;
                        }
                    })
                    .SetCycles(5)
                    .SetThrottle(25)
                    .SetThrottleScale(2)
                    .Run();
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        public virtual async Task EnsureTelemetry(IDurableOrchestrationClient starter, StateDetails stateDetails,
            ExecuteActionRequest exActReq, SecurityManagerClient secMgr, DocumentClient docClient)
        {
            if (State.Telemetry == null)
                State.Telemetry = new IoTEnsembleTelemetry();

            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            var tpd = await secMgr.RetrieveEnterpriseThirdPartyData(State.UserEnterpriseLookup, TELEMETRY_SYNC_ENABLED);

                            if (tpd.Status && tpd.Model.ContainsKey(TELEMETRY_SYNC_ENABLED) && !tpd.Model[TELEMETRY_SYNC_ENABLED].IsNullOrEmpty())
                                State.Telemetry.Enabled = tpd.Model[TELEMETRY_SYNC_ENABLED].As<bool>();
                            else
                                State.Telemetry.Enabled = false;

                            return false;
                        }
                        catch (Exception ex)
                        {
                            log.LogError(ex, "Failed setting telemetry sync enabled");

                            return true;
                        }
                    })
                    .SetCycles(5)
                    .SetThrottle(25)
                    .SetThrottleScale(2)
                    .Run();

                await setTelemetryEnabled(secMgr, State.Telemetry.Enabled);

                await EnsureTelemetrySyncState(starter, stateDetails, exActReq);

                await LoadTelemetry(secMgr, docClient);
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        public virtual async Task EnsureTelemetrySyncState(IDurableOrchestrationClient starter, StateDetails stateDetails,
            ExecuteActionRequest exActReq)
        {
            var instanceId = $"{stateDetails.EnterpriseLookup}-{stateDetails.HubName}-{stateDetails.Username}-{stateDetails.StateKey}-{State.UserEnterpriseLookup}";

            var existingOrch = await starter.GetStatusAsync(instanceId);

            var isStartState = existingOrch?.RuntimeStatus == OrchestrationRuntimeStatus.Running ||
                existingOrch?.RuntimeStatus == OrchestrationRuntimeStatus.Pending;

            log.LogInformation($"Is telemetry sync enabled ({State.Telemetry.Enabled}) vs start state ({isStartState})...");

            if (State.Telemetry.Enabled && !isStartState)
            {
                log.LogInformation($"Sarting TelemetrySyncOrchestration: {instanceId}");

                await starter.StartAction("TelemetrySyncOrchestration", stateDetails, exActReq, log, instanceId: instanceId);
            }
            else if (!State.Telemetry.Enabled && isStartState)
            {
                log.LogInformation($"Terminating TelemetrySyncOrchestration: {instanceId}");

                await starter.TerminateAsync(instanceId, "Device Telemetry has been disbaled.");
            }
        }

        public virtual async Task EnsureUserEnterprise(EnterpriseArchitectClient entArch, EnterpriseManagerClient entMgr,
            SecurityManagerClient secMgr, string parentEntLookup, string username)
        {
            if (State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            var hostLookup = $"{parentEntLookup}|{username}";

                            if (deviceEnv != "prd")
                                hostLookup += $"|{deviceEnv}";

                            log.LogInformation($"Ensuring user enterprise for {hostLookup}...");

                            var getResp = await entMgr.ResolveHost(hostLookup, false);

                            if (!getResp.Status || getResp.Model == null)
                            {
                                var createResp = await entArch.CreateEnterprise(new CreateEnterpriseRequest()
                                {
                                    Name = username,
                                    Description = username,
                                    Host = hostLookup
                                }, parentEntLookup, username);

                                if (createResp.Status)
                                    State.UserEnterpriseLookup = createResp.Model.EnterpriseLookup;
                            }
                            else
                                State.UserEnterpriseLookup = getResp.Model.EnterpriseLookup;

                            return State.UserEnterpriseLookup.IsNullOrEmpty();
                        }
                        catch (Exception ex)
                        {
                            log.LogError(ex, "Failed ensuring user enterprise");

                            return true;
                        }
                    })
                    .SetCycles(5)
                    .SetThrottle(25)
                    .SetThrottleScale(2)
                    .Run();
            }

            if (State.UserEnterpriseLookup.IsNullOrEmpty())
                throw new Exception("Unable to establish the user's enterprise, please try again.");
        }

        public virtual async Task<Status> HasLicenseAccess(IdentityManagerClient idMgr, string entLookup, string username)
        {
            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var hasAccess = await idMgr.HasLicenseAccess(entLookup, username, Personas.AllAnyTypes.All, new List<string>() { "iot" });

                        State.HasAccess = hasAccess.Status;

                        if (State.HasAccess)
                        {
                            if (hasAccess.Model.Metadata.ContainsKey("LicenseType"))
                                State.AccessLicenseType = hasAccess.Model.Metadata["LicenseType"].ToString();

                            if (hasAccess.Model.Metadata.ContainsKey("PlanGroup"))
                                State.AccessPlanGroup = hasAccess.Model.Metadata["PlanGroup"].ToString();

                            if (hasAccess.Model.Metadata.ContainsKey("Devices"))
                                State.Devices.MaxDevicesCount = hasAccess.Model.Metadata["Devices"].ToString().As<int>();
                        }
                        else
                        {
                            State.AccessLicenseType = "iot";

                            State.AccessPlanGroup = "explorer";

                            State.Devices.MaxDevicesCount = 1;
                        }

                        return false;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed checking has license access type");

                        return true;
                    }
                })
                .SetCycles(5)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();

            return Status.Success;
        }

        public virtual async Task IssueDeviceSASToken(ApplicationArchitectClient appArch, string deviceName, int expiryInSeconds)
        {
            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var deviceSasResp = await appArch.IssueDeviceSASToken(State.UserEnterpriseLookup, deviceName, expiryInSeconds: expiryInSeconds,
                            envLookup: null);

                        if (deviceSasResp.Status)
                        {
                            if (State.Devices.SASTokens == null)
                                State.Devices.SASTokens = new Dictionary<string, string>();

                            State.Devices.SASTokens[deviceName] = deviceSasResp.Model;
                        }

                        return State.Devices.SASTokens == null;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed issuing device SAS Token");

                        return true;
                    }
                })
                .SetCycles(5)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();
        }

        public virtual async Task<Status> LoadAPIKeys(EnterpriseArchitectClient entArch, string entLookup, string username)
        {
            if (State.Storage == null)
                State.Storage = new IoTEnsembleStorageConfiguration();

            State.Storage.APIKeys = new List<IoTEnsembleAPIKeyData>();

            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var resp = await entArch.LoadAPIKeys(entLookup, username);

                        //  TODO:  Handle API error

                        State.Storage.APIKeys = resp.Model?.Metadata.Select(m => new IoTEnsembleAPIKeyData()
                        {
                            Key = m.Value.ToString(),
                            KeyName = m.Key
                        }).ToList();

                        return !resp.Status;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed loading API Keys");

                        return true;
                    }
                })
                .SetCycles(5)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();

            return Status.Success;
        }

        public virtual async Task<Status> LoadAPIOptions()
        {
            if (State.Storage == null)
                State.Storage = new IoTEnsembleStorageConfiguration();

            State.Storage.APIOptions = new List<IoTEnsembleAPIOption>();

            State.Storage.APIOptions.Add(new IoTEnsembleAPIOption()
            {
                Name = "Cold Query",
                Description = "The cold query is used to access the records in your cold storage.",
                Method = "GET",
                Path = "https://fathym-prd.portal.azure-api.net/docs/services/iot-ensemble-state-api/operations/coldquery",
            });

            State.Storage.APIOptions.Add(new IoTEnsembleAPIOption()
            {
                Name = "Warm Query",
                Description = "The warm query is used to access the telemetry records in your warm storage.",
                Method = "GET",
                Path = "https://fathym-prd.portal.azure-api.net/docs/services/iot-ensemble-state-api/operations/warmquery",
            });

            return Status.Success;
        }

        public virtual async Task LoadDevices(ApplicationArchitectClient appArch)
        {
            if (State.Devices == null)
                State.Devices = new IoTEnsembleConnectedDevicesConfig();

            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var devicesResp = await appArch.ListEnrolledDevices(State.UserEnterpriseLookup, envLookup: null,
                            page: State.Devices.Page.Value, pageSize: State.Devices.PageSize);

                        if (devicesResp.Status)
                        {
                            State.Devices.Devices = devicesResp.Model?.Items?.Select(m =>
                            {
                                var devInfo = m.JSONConvert<IoTEnsembleDeviceInfo>();

                                devInfo.DeviceName = devInfo.DeviceID.Replace($"{State.UserEnterpriseLookup}-", String.Empty);

                                return devInfo;

                            }).JSONConvert<List<IoTEnsembleDeviceInfo>>() ?? new List<IoTEnsembleDeviceInfo>();

                            State.Devices.TotalDevices = devicesResp.Model.TotalRecords;

                            State.Devices.SASTokens = null;
                        }
                        else if (State.Devices.Devices.IsNullOrEmpty() || devicesResp.Status == Status.NotLocated)
                            State.Devices.Devices = new List<IoTEnsembleDeviceInfo>();

                        log.LogInformation($"Load devices status {devicesResp.Status.ToJSON()}");

                        return !devicesResp.Status && devicesResp.Status != Status.NotLocated;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed loading devices");

                        return true;
                    }
                })
                .SetCycles(5)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();
        }

        public virtual async Task<Status> LoadTelemetry(SecurityManagerClient secMgr, DocumentClient client)
        {
            var status = Status.Success;

            if (State.Telemetry.Page < 1)
                State.Telemetry.Page = 1;

            if (State.Telemetry.PageSize < 1)
                State.Telemetry.PageSize = 10;

            if (State.Telemetry.Enabled)
            {
                State.Telemetry.Payloads = new List<IoTEnsembleTelemetryPayload>();

                try
                {
                    var payloads = await queryTelemetryPayloads(client, State.UserEnterpriseLookup,
                            State.SelectedDeviceIDs, State.Telemetry.PageSize, State.Telemetry.Page, State.Emulated.Enabled);

                    if (!payloads.Items.IsNullOrEmpty())
                        State.Telemetry.Payloads.AddRange(payloads.Items);

                    State.Telemetry.TotalPayloads = payloads.TotalRecords;

                    status.Metadata["RefreshRate"] = State.Telemetry.RefreshRate >= 10 ? State.Telemetry.RefreshRate : 30;

                    State.Telemetry.RefreshRate = status.Metadata["RefreshRate"].ToString().As<int>();

                    State.Telemetry.LastSyncedAt = DateTime.Now;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "There was an issue loading your device telemetry.");

                    status = Status.GeneralError.Clone("There was an issue loading your device telemetry.");
                }
            }
            else
                status = Status.GeneralError.Clone("Device Telemetry is Disabled");

            return status;
        }

        public virtual async Task Refresh(IDurableOrchestrationClient starter, StateDetails stateDetails, ExecuteActionRequest exActReq,
            ApplicationArchitectClient appArch, EnterpriseArchitectClient entArch, EnterpriseManagerClient entMgr, IdentityManagerClient idMgr,
            SecurityManagerClient secMgr, DocumentClient client)
        {
            await EnsureUserEnterprise(entArch, entMgr, secMgr, stateDetails.EnterpriseLookup, stateDetails.Username);

            await Task.WhenAll(
                LoadDevices(appArch),
                HasLicenseAccess(idMgr, stateDetails.EnterpriseLookup, stateDetails.Username),
                EnsureEmulatedDeviceInfo(starter, stateDetails, exActReq, secMgr, client)
            );
            await Task.WhenAll(
                EnsureAPISubscription(entArch, stateDetails.EnterpriseLookup, stateDetails.Username),
                EnsureDevicesDashboard(secMgr),
                EnsureDrawersConfig(secMgr),
                LoadAPIOptions(),
                EnsureTelemetry(starter, stateDetails, exActReq, secMgr, client)
            );

            State.Loading = false;

            State.Devices.Loading = false;

            State.Emulated.Loading = false;

            State.Telemetry.Loading = false;
        }

        public virtual async Task<bool> RevokeDeviceEnrollment(ApplicationArchitectClient appArch, string deviceId)
        {
            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var revokeResp = await appArch.RevokeDeviceEnrollment(deviceId, State.UserEnterpriseLookup, envLookup: null);

                        return !revokeResp.Status;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed revoking device enrollment");

                        return true;
                    }
                })
                .SetCycles(5)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();

            await LoadDevices(appArch);

            return false;
        }

        public virtual async Task<Status> SendDeviceMessage(ApplicationArchitectClient appArch, SecurityManagerClient secMgr,
            DocumentClient client, string deviceName, MetadataModel payload)
        {
            if (payload.Metadata.ContainsKey("id"))
                payload.Metadata.Remove("id");

            var status = Status.Initialized;

            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var sendResp = await appArch.SendDeviceMessage(payload, State.UserEnterpriseLookup,
                            deviceName, envLookup: null);

                        status = sendResp.Status;

                        return !status;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed sending device message");

                        return true;
                    }
                })
                .SetCycles(5)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();

            // if (sendResp.Status)
            // {
            //     await LoadTelemetry(secMgr, client);
            // }

            return status;
        }

        public virtual async Task ToggleDetailsPane(SecurityManagerClient secMgr)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            var active = !State.Drawers.DetailsActive;

                            var resp = await secMgr.SetEnterpriseThirdPartyData(State.UserEnterpriseLookup, new Dictionary<string, string>()
                            {
                                { DETAILS_PANE_ENABLED, active.ToString() }
                            });

                            if (resp.Status)
                                State.Drawers.DetailsActive = active;

                            return !resp.Status;
                        }
                        catch (Exception ex)
                        {
                            log.LogError(ex, "Failed toggling details pane");

                            return true;
                        }
                    })
                    .SetCycles(5)
                    .SetThrottle(25)
                    .SetThrottleScale(2)
                    .Run();
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        public virtual async Task ToggleEmulatedEnabled(IDurableOrchestrationClient starter, StateDetails stateDetails,
            ExecuteActionRequest exActReq, SecurityManagerClient secMgr, DocumentClient client, bool skipTelem = false)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                var status = Status.Initialized;

                var enabled = !State.Emulated.Enabled;

                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            var resp = await secMgr.SetEnterpriseThirdPartyData(State.UserEnterpriseLookup, new Dictionary<string, string>()
                            {
                                { EMULATED_DEVICE_ENABLED, enabled.ToString() }
                            });

                            status = resp.Status;

                            return !status;
                        }
                        catch (Exception ex)
                        {
                            log.LogError(ex, "Failed toggling emulated enabled");

                            return true;
                        }
                    })
                    .SetCycles(5)
                    .SetThrottle(25)
                    .SetThrottleScale(2)
                    .Run();

                if (status)
                {
                    State.Emulated.Enabled = enabled;

                    if (State.Devices.Devices.IsNullOrEmpty() && !skipTelem)
                    {
                        await setTelemetryEnabled(secMgr, enabled);

                        await EnsureTelemetrySyncState(starter, stateDetails, exActReq);

                        await LoadTelemetry(secMgr, client);
                    }
                }
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        public virtual async Task ToggleTelemetrySyncEnabled(IDurableOrchestrationClient starter, StateDetails stateDetails,
            ExecuteActionRequest exActReq, SecurityManagerClient secMgr, DocumentClient client)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                if (State.Telemetry == null)
                    State.Telemetry = new IoTEnsembleTelemetry();

                await setTelemetryEnabled(secMgr, !State.Telemetry.Enabled);

                await EnsureTelemetrySyncState(starter, stateDetails, exActReq);

                await LoadTelemetry(secMgr, client);
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        public virtual async Task UpdateTelemetrySync(SecurityManagerClient secMgr, DocumentClient client, int refreshRate, int page, int pageSize)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                State.Telemetry.RefreshRate = refreshRate;

                State.Telemetry.Page = page;

                State.Telemetry.PageSize = pageSize;

                await LoadTelemetry(secMgr, client);
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        public virtual async Task UpdateConnectedDevicesSync(int page, int pageSize)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                State.Devices.Page = page;

                State.Devices.PageSize = pageSize;
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        #region Storage Access
        public virtual async Task<HttpResponseMessage> ColdQuery(CloudBlobDirectory coldBlob, List<string> selectedDeviceIds, int pageSize, int page,
            bool includeEmulated, DateTime? startDate, DateTime? endDate, ColdQueryResultTypes resultType, bool flatten,
            ColdQueryDataTypes dataType, bool zip)
        {
            var status = Status.GeneralError;

            HttpContent content = null;

            if (coldBlob != null)
            {
                try
                {
                    var fileExtension = getFileExtension(resultType);

                    var fileName = buildFileName(dataType, startDate.Value, endDate.Value, fileExtension);

                    log.LogInformation($"Loaded {fileName} with extension {fileExtension}");

                    var downloadedData = await downloadData(coldBlob, dataType, State.UserEnterpriseLookup, startDate, endDate);

                    log.LogInformation($"Downloaded data records: {downloadedData.Count}");

                    if (flatten)
                    {
                        log.LogInformation($"Flattening Downloaded Telemetry");

                        downloadedData = flattenDownloadedData(downloadedData);
                    }

                    var bytes = await processToResultType(downloadedData, resultType);

                    var contentType = String.Empty;

                    if (resultType == ColdQueryResultTypes.CSV)
                        contentType = "text/csv";
                    else if (resultType == ColdQueryResultTypes.JSON)
                        contentType = "application/json";
                    else if (resultType == ColdQueryResultTypes.JSONLines)
                        contentType = "application/jsonl";

                    if (zip)
                    {
                        log.LogInformation($"Zipping response data");

                        bytes = await zipFileContent(bytes, fileName, fileExtension);

                        fileName = fileName.Replace($".{fileExtension}", "zip");

                        contentType = "application/zip";
                    }

                    content = new ByteArrayContent(bytes);

                    content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment");

                    content.Headers.ContentDisposition.FileName = fileName;

                    content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

                    status = Status.Success;
                }
                catch (Exception ex)
                {
                    var resp = new BaseResponse() { Status = Status.GeneralError };

                    content = new StringContent(resp.ToJSON(), Encoding.UTF8, "application/json");

                    status = Status.GeneralError.Clone(ex.ToString());
                }
            }

            if (content == null || !status)
            {
                var resp = new BaseResponse() { Status = status };

                content = new StringContent(resp.ToJSON(), Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response = null;

            var statusCode = status ? HttpStatusCode.OK : HttpStatusCode.InternalServerError;

            log.LogInformation($"Returning content message with status {statusCode}");

            response = new HttpResponseMessage(statusCode)
            {
                Content = content
            };

            return response;
        }

        public virtual async Task<IoTEnsembleTelemetryResponse> WarmQuery(DocumentClient telemClient, List<string> selectedDeviceIds,
            int? pageSize, int? page, bool includeEmulated, DateTime? startDate, DateTime? endDate)
        {
            var response = new IoTEnsembleTelemetryResponse()
            {
                Payloads = new List<IoTEnsembleTelemetryPayload>(),
                Status = Status.Initialized
            };

            if (!page.HasValue || page.Value < 1)
                page = 1;

            if (!pageSize.HasValue || pageSize.Value < 1)
                pageSize = 1;

            try
            {
                var payloads = await queryTelemetryPayloads(telemClient, State.UserEnterpriseLookup, selectedDeviceIds, pageSize.Value,
                    page.Value, includeEmulated);

                response.Payloads = payloads.Items.ToList();

                response.Status = Status.Success;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "There was an issue loading your device telemetry.");

                response.Status = Status.GeneralError.Clone("There was an issue loading your device telemetry.",
                    new { Exception = ex.ToString() });
            }

            return response;
        }
        #endregion
        #endregion

        #region Helpers
        protected virtual string buildFileName(ColdQueryDataTypes dataType, DateTime startDate, DateTime endDate, string fileExtension)
        {
            var dtTypeStr = dataType.ToString().ToLower();

            var startStr = startDate.ToString("yyyyMMddHHmmss");

            var endStr = endDate.ToString("yyyyMMddHHmmss");

            var fileName = $"{dtTypeStr}-{startStr}-{endStr}.{fileExtension}";

            return fileName;
        }

        protected virtual async Task<List<JObject>> downloadData(CloudBlobDirectory coldBlob, ColdQueryDataTypes dataType, string entLookup,
            DateTime? startDate, DateTime? endDate)
        {
            BlobContinuationToken contToken = null;

            var downloadedData = new List<JObject>();

            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        downloadedData = new List<JObject>();

                        do
                        {
                            var dataTypeColdBlob = coldBlob.GetDirectoryReference(dataType.ToString().ToLower());

                            var entColdBlob = dataTypeColdBlob.GetDirectoryReference(State.UserEnterpriseLookup);

                            log.LogInformation($"Listing blob segments...");

                            var blobSeg = await entColdBlob.ListBlobsSegmentedAsync(true, BlobListingDetails.Metadata, null, contToken, null, null);

                            contToken = blobSeg.ContinuationToken;

                            // foreach (var item in blobSeg.Results)
                            await blobSeg.Results.Each(async item =>
                            {
                                var blob = (CloudBlockBlob)item;

                                await blob.FetchAttributesAsync();

                                var minTime = DateTime.Parse(blob.Metadata["MinTime"]);

                                var maxTime = DateTime.Parse(blob.Metadata["MaxTime"]);

                                if ((startDate <= minTime && minTime <= endDate) || (startDate <= maxTime && maxTime <= endDate))
                                {
                                    var blobContents = await blob.DownloadTextAsync();

                                    var blobData = JsonConvert.DeserializeObject<JArray>(blobContents);

                                    if (downloadedData.Count == 0)
                                        downloadedData.AddRange(blobData.ToObject<List<JObject>>());
                                }
                            }, parallel: true);
                        } while (contToken != null);

                        return false;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed downloading telemetry");

                        return true;
                    }
                })
                .SetCycles(5)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();

            return downloadedData;
        }

        protected virtual List<JObject> flattenDownloadedData(List<JObject> downloadedData)
        {
            var tempList = new List<JObject>(downloadedData);

            var flatData = tempList.Select(dt =>
            {
                var props = dt.Properties().ToList();

                foreach (var prop in props)
                {
                    if (prop.Value is JObject)
                    {
                        flattenObject(prop.Value as JObject, dt, prop.Name);
                    }
                }

                return dt;
            }).ToList();

            return flatData;
        }

        protected virtual void flattenObject(JObject token, JObject root, string parentPropName)
        {
            var childProps = token.Properties();

            foreach (var childProp in childProps)
            {
                var propName = $"{parentPropName}_{childProp.Name}";

                if (childProp.Value is JObject)
                    flattenObject(childProp.Value as JObject, root, propName);
                else
                    root.Add(propName, childProp.Value);
            }

            root.Remove(parentPropName);
        }

        protected virtual async Task<byte[]> generateCsv(List<JObject> downloadedData, string delimiter = ",")
        {
            using (var writer = new StringWriter())
            {
                using (var csv = new CsvWriter(writer, System.Globalization.CultureInfo.CurrentCulture))
                {
                    csv.Configuration.Delimiter = delimiter;

                    await csv.WriteRecordsAsync(downloadedData);
                }

                return Encoding.UTF8.GetBytes(writer.ToString());
            }
        }

        protected virtual async Task<byte[]> generateJsonLines(List<JObject> downloadedData)
        {
            var uniEncoding = new UnicodeEncoding();

            var jsonLines = new MemoryStream();

            var sw = new StreamWriter(jsonLines, uniEncoding);

            try
            {
                foreach (var dt in downloadedData)
                    sw.WriteLine(dt.ToJSON());

                sw.Flush();

                // Test and work with the stream here. 
                // If you need to start back at the beginning, be sure to Seek again.
            }
            finally
            {
                sw.Dispose();
            }

            using (var sr = new StreamReader(new MemoryStream(jsonLines.ToArray())))
                return Encoding.UTF8.GetBytes(await sr.ReadToEndAsync());
        }

        protected virtual string getFileExtension(ColdQueryResultTypes? resultType)
        {
            var fileExtension = String.Empty;

            if (resultType == null || resultType == ColdQueryResultTypes.JSON)
                fileExtension = "json";
            else if (resultType == ColdQueryResultTypes.JSONLines)
                fileExtension = "jsonl";
            else if (resultType == ColdQueryResultTypes.CSV)
                fileExtension = "csv";

            return fileExtension;
        }

        protected virtual MetadataModel loadDefaultFreeboardConfig()
        {
            return ("{\r\n\t\"version\": 1,\r\n\t\"allow_edit\": true,\r\n\t\"plugins\": [],\r\n\t\"panes\": [\r\n\t\t{\r\n\t\t\t\"title\": \"\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 1\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 10,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"\",\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"Device Insights & Monitoring\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Sensor Placement\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 5,\r\n\t\t\t\t\"4\": 5,\r\n\t\t\t\t\"5\": 5,\r\n\t\t\t\t\"11\": 5\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 3,\r\n\t\t\t\t\"4\": -8,\r\n\t\t\t\t\"5\": -8,\r\n\t\t\t\t\"11\": -8\r\n\t\t\t},\r\n\t\t\t\"col_width\": 1,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Floor\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Floor\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Room\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": [\r\n\t\t\t\t\t\t\t\"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Room\\\"]\"\r\n\t\t\t\t\t\t],\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Occupancy\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Occupancy\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Occupied\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Occupied\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Sensor Data\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 5,\r\n\t\t\t\t\"4\": 1,\r\n\t\t\t\t\"7\": 1,\r\n\t\t\t\t\"26\": 1\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 2,\r\n\t\t\t\t\"7\": 2,\r\n\t\t\t\t\"26\": 2\r\n\t\t\t},\r\n\t\t\t\"col_width\": 2,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Device ID\",\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceID\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"gauge\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Temperature\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Temperature\\\"]\",\r\n\t\t\t\t\t\t\"units\": \"\u00B0F\",\r\n\t\t\t\t\t\t\"min_value\": 0,\r\n\t\t\t\t\t\t\"max_value\": \"150\"\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"gauge\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Humidity\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Humidity\\\"]\",\r\n\t\t\t\t\t\t\"units\": \"%\",\r\n\t\t\t\t\t\t\"min_value\": 0,\r\n\t\t\t\t\t\t\"max_value\": 100\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Temperature History\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 23,\r\n\t\t\t\t\"11\": 23\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"11\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 3,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Temperature\\\"]\",\r\n\t\t\t\t\t\t\"sparkline\": true,\r\n\t\t\t\t\t\t\"animate\": true,\r\n\t\t\t\t\t\t\"units\": \"\u00B0F\"\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Map\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 29,\r\n\t\t\t\t\"4\": 9,\r\n\t\t\t\t\"11\": 9,\r\n\t\t\t\t\"15\": 9,\r\n\t\t\t\t\"27\": 9\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 1,\r\n\t\t\t\t\"11\": 1,\r\n\t\t\t\t\"15\": 1,\r\n\t\t\t\t\"27\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 10,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"google_map\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"lat\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Latitude\\\"]\",\r\n\t\t\t\t\t\t\"lon\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Longitude\\\"]\"\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Last Processed Device Data\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 39,\r\n\t\t\t\t\"21\": 25,\r\n\t\t\t\t\"24\": 25,\r\n\t\t\t\t\"36\": 25\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"21\": 1,\r\n\t\t\t\t\"24\": 1,\r\n\t\t\t\t\"36\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 2,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Device ID\",\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"deviceid\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"html\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"html\": \"JSON.stringify(datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1])\",\r\n\t\t\t\t\t\t\"height\": 4\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Connected Devices (Last 3 Days)\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 39,\r\n\t\t\t\t\"22\": 25,\r\n\t\t\t\t\"25\": 25,\r\n\t\t\t\t\"37\": 25\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 3,\r\n\t\t\t\t\"22\": 3,\r\n\t\t\t\t\"25\": 3,\r\n\t\t\t\t\"37\": 3\r\n\t\t\t},\r\n\t\t\t\"col_width\": 1,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"html\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"html\": \"JSON.stringify(Array.from(new Set(datasources[\\\"Fathym Device Data\\\"].map((q) => q.DeviceID))))\",\r\n\t\t\t\t\t\t\"height\": 4\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t}\r\n\t],\r\n\t\"datasources\": [\r\n\t\t{\r\n\t\t\t\"name\": \"Fathym Device Data\",\r\n\t\t\t\"type\": \"JSON\",\r\n\t\t\t\"settings\": {\r\n\t\t\t\t\"url\": \"" + $"{telemetryRoot}\\/api\\/iot-ensemble\\/devices\\/telemetry" + "\",\r\n\t\t\t\t\"use_thingproxy\": true,\r\n\t\t\t\t\"refresh\": 30,\r\n\t\t\t\t\"method\": \"GET\"\r\n\t\t\t}\r\n\t\t}\r\n\t],\r\n\t\"columns\": 3\r\n}").FromJSON<MetadataModel>();
            // return "{\r\n\t\"version\": 1,\r\n\t\"allow_edit\": true,\r\n\t\"plugins\": [],\r\n\t\"panes\": [\r\n\t\t{\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 1\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 3,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"Device Insights & Monitoring\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Last Processed Device Data\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 5\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 2,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Query\\\"][datasources[\\\"Query\\\"].length - 1][\\\"DeviceID\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"html\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"html\": \"JSON.stringify(datasources[\\\"Query\\\"][datasources[\\\"Query\\\"].length - 1])\",\r\n\t\t\t\t\t\t\"height\": 4\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Connected Devices (Last 3 Days)\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 5\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 3\r\n\t\t\t},\r\n\t\t\t\"col_width\": 1,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"html\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"html\": \"JSON.stringify(Array.from(new Set(datasources[\\\"Query\\\"].map((q) => q.DeviceID))))\",\r\n\t\t\t\t\t\t\"height\": 4\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t}\r\n\t],\r\n\t\"datasources\": [\r\n\t\t{\r\n\t\t\t\"name\": \"Query\",\r\n\t\t\t\"type\": \"JSON\",\r\n\t\t\t\"settings\": {\r\n\t\t\t\t\"url\": \"\\/api\\/iot-ensemble\\/devices\\/telemetry\",\r\n\t\t\t\t\"use_thingproxy\": false,\r\n\t\t\t\t\"refresh\": 30,\r\n\t\t\t\t\"method\": \"GET\"\r\n\t\t\t}\r\n\t\t}\r\n\t],\r\n\t\"columns\": 3\r\n}".FromJSON<MetadataModel>();
        }

        protected virtual async Task<byte[]> processToResultType(List<JObject> downloadedData, ColdQueryResultTypes resultType)
        {
            var response = new byte[] { };

            log.LogInformation($"Determining result type...");

            if (resultType == ColdQueryResultTypes.JSON)
            {
                log.LogInformation($"Returning JSON result");

                response = Encoding.UTF8.GetBytes(downloadedData.ToJSON());
            }
            else if (resultType == ColdQueryResultTypes.JSONLines)
            {
                log.LogInformation($"Returning JSON Lines result");

                response = await generateJsonLines(downloadedData);
            }
            else if (resultType == ColdQueryResultTypes.CSV)
            {
                log.LogInformation($"Returning CSV result");

                response = await generateCsv(downloadedData);
            }

            return response;
        }

        protected virtual async Task<Pageable<IoTEnsembleTelemetryPayload>> queryTelemetryPayloads(DocumentClient client, string entLookup,
            List<string> selectedDeviceIds, int pageSize, int page, bool emulatedEnabled, bool count = true)
        {
            if (page < 1)
                page = 1;

            if (pageSize < 1)
                pageSize = 1;

            var payloads = new Pageable<IoTEnsembleTelemetryPayload>();

            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        Uri colUri = UriFactory.CreateDocumentCollectionUri(warmTelemetryDatabase, warmTelemetryContainer);

                        IQueryable<IoTEnsembleTelemetryPayload> docsQueryBldr =
                            client.CreateDocumentQuery<IoTEnsembleTelemetryPayload>(colUri, new FeedOptions()
                            {
                                EnableCrossPartitionQuery = true
                            })
                            .Where(payload => payload.EnterpriseLookup == entLookup || (emulatedEnabled && payload.EnterpriseLookup == "EMULATED"));

                        if (!selectedDeviceIds.IsNullOrEmpty())
                            docsQueryBldr = docsQueryBldr.Where(payload => selectedDeviceIds.Contains(payload.DeviceID));

                        docsQueryBldr = docsQueryBldr
                            .OrderByDescending(payload => payload._ts);

                        if (count)
                        {
                            payloads.TotalRecords = await docsQueryBldr.CountAsync();
                        }

                        docsQueryBldr = docsQueryBldr
                            .Skip((pageSize * page) - pageSize)
                            .Take(pageSize);

                        var docsQuery = docsQueryBldr.AsDocumentQuery();

                        var tempPayloads = new List<IoTEnsembleTelemetryPayload>();

                        while (docsQuery.HasMoreResults)
                            tempPayloads.AddRange(await docsQuery.ExecuteNextAsync<IoTEnsembleTelemetryPayload>());

                        payloads.Items = tempPayloads;

                        return false;
                    }
                    catch (Exception ex)
                    {
                        var retriable = false;

                        var retriableExceptionCodes = new List<int>() { 409, 412, 429, 1007, 1008 };

                        if (ex is ResponseException rex)
                        {
                            var code = rex.StatusAttributes["x-ms-status-code"].As<int>();

                            retriable = retriableExceptionCodes.Contains(code);

                            if (retriable && rex.StatusAttributes.ContainsKey("x-ms-retry-after-ms"))
                            {
                                var retryMsWait = rex.StatusAttributes["x-ms-retry-after-ms"].As<int>();

                                await Task.Delay(retryMsWait);
                            }
                        }

                        if (!retriable)
                            throw;

                        return retriable;
                    }
                })
                .SetCycles(10)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();

            return payloads;
        }

        protected virtual async Task setTelemetryEnabled(SecurityManagerClient secMgr, bool enabled)
        {
            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var resp = await secMgr.SetEnterpriseThirdPartyData(State.UserEnterpriseLookup, new Dictionary<string, string>()
                        {
                            { TELEMETRY_SYNC_ENABLED, enabled.ToString() }
                        });

                        if (resp.Status)
                            State.Telemetry.Enabled = enabled;

                        return !resp.Status;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed setting telemetry enabled");

                        return true;
                    }
                })
                .SetCycles(5)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();
        }

        protected virtual async Task<byte[]> zipFileContent(byte[] response, string fileName, string fileExtension)
        {
            using (var memoryStream = new MemoryStream())
            {
                using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    var demoFile = zipArchive.CreateEntry($"{fileName}.{fileExtension}");

                    using (var entryStream = demoFile.Open())
                    {
                        using (var streamWriter = new StreamWriter(entryStream))
                            await streamWriter.WriteAsync(Encoding.UTF8.GetString(response));
                    }
                }

                return memoryStream.ToArray();
            }
        }
        #endregion
    }

    [Serializable]
    [DataContract]
    public enum ColdQueryResultTypes
    {
        [EnumMember]
        CSV,

        [EnumMember]
        JSON,

        [EnumMember]
        JSONLines
    }

    [Serializable]
    [DataContract]
    public enum ColdQueryDataTypes
    {
        [EnumMember]
        Telemetry,

        [EnumMember]
        Observations,

        [EnumMember]
        SensorMetadata
    }
}
