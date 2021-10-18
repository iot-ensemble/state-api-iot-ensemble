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
using LCU.Personas.API;
using LCU.State.API.IoTEnsemble.Host.TempRefit;

namespace LCU.State.API.IoTEnsemble.State
{
    public class IoTEnsembleSharedStateHarness : IoTEnsembleStateHarness<IoTEnsembleSharedState>
    {
        #region Constants
        const string DETAILS_PANE_ENABLED = "IoTEnsemble:DetailsPaneEnabled";

        const string EMULATED_DEVICE_ENABLED = "IoTEnsemble:EmulatedDeviceEnabled";

        const string DEVICE_DASHBOARD_FREEBOARD_CONFIG = "IoTEnsemble:DeviceDashboardFreeboardConfig";

        const string TELEMETRY_SYNC_ENABLED = "IoTEnsemble:TelemetrySyncEnabled";
        #endregion

        #region Fields
        #endregion

        #region Properties
        #endregion

        #region Constructors
        public IoTEnsembleSharedStateHarness(IoTEnsembleSharedState state, ILogger logger)
            : base(state ?? new IoTEnsembleSharedState(), logger)
        { }
        #endregion

        #region API Methods
        public virtual async Task<bool> EnrollDevice(IApplicationsIoTService appIoTArch, IoTEnsembleDeviceEnrollment device)
        {
            var enrollResp = new EnrollDeviceResponse();

            var deviceId = $"{State.UserEnterpriseLookup}-{device.DeviceName}";

            log.LogInformation($"Enrolling new device with id {deviceId}");

            if (State.DevicesConfig.Devices.IsNullOrEmpty() || State.DevicesConfig.Devices.Count() < State.DevicesConfig.MaxDevicesCount)
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            enrollResp = await appIoTArch.EnrollDevice(new EnrollDeviceRequest()
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

                            State.DevicesConfig.Status = enrollResp.Status;

                            log.LogInformation($"Enroll device status {State.DevicesConfig.Status.ToJSON()}");

                            return !State.DevicesConfig.Status;
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
                State.DevicesConfig.Status = Status.Conflict.Clone("Max Device Count Reached");

                log.LogInformation($"Max Device Count Reached while enrolling {deviceId}");
            }

            await Task.Delay(2500);

            await LoadDevices(appIoTArch);

            return false;
        }

        public virtual async Task<Status> EnsureAPISubscription(IEnterprisesAPIManagementService entApiArch, string entLookup, string username)
        {
            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var resp = await entApiArch.EnsureAPISubscription(new EnsureAPISubscriptionRequest()
                        {
                            SubscriptionType = buildSubscriptionType()
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

            return await LoadAPIKeys(entApiArch, entLookup, username);
        }

        public virtual async Task EnsureDevicesDashboard(ISecurityDataTokenService secMgr)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                if (State.Dashboard.FreeboardConfig == null)
                {
                    await DesignOutline.Instance.Retry()
                        .SetActionAsync(async () =>
                        {
                            try
                            {
                                var tpd = await secMgr.GetDataToken(DEVICE_DASHBOARD_FREEBOARD_CONFIG, State.UserEnterpriseLookup);

                                if (tpd.Status && tpd.Model.Lookup=="DEVICE_DASHBOARD_FREEBOARD_CONFIG" && !tpd.Model.Lookup.IsNullOrEmpty())
                                    State.Dashboard.FreeboardConfig = tpd.Model.Value.FromJSON<MetadataModel>();

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
                                    var freeboardConfig = await loadDefaultFreeboardConfig();

                                    var resp = await secMgr.SetDataToken(new DataToken()
                                    {
                                        Lookup="DEVICE_DASHBOARD_FREEBOARD_CONFIG", 
                                        Value= freeboardConfig.ToJSON()
                                    }, State.UserEnterpriseLookup);
                                
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

        public virtual async Task EnsureDrawersConfig(ISecurityDataTokenService secMgr)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            var tpd = await secMgr.GetDataToken(DETAILS_PANE_ENABLED, State.UserEnterpriseLookup);

                            if (tpd.Status && tpd.Model.Lookup=="DETAILS_PANE_ENABLED" && !tpd.Model.Lookup.IsNullOrEmpty())
                                State.Drawers.DetailsActive = tpd.Model.Value.As<bool>();
                            else
                            {
                                var resp = await secMgr.SetDataToken(new DataToken()
                                {
                                    Lookup="DETAILS_PANE_ENABLED",
                                    Value="true"
                                }, State.UserEnterpriseLookup);

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
            ExecuteActionRequest exActReq, ISecurityDataTokenService secMgr, DocumentClient client)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            var tpd = await secMgr.GetDataToken(EMULATED_DEVICE_ENABLED, State.UserEnterpriseLookup);


                            if (tpd.Status && tpd.Model.Lookup=="EMULATED_DEVICE_ENABLED" && !tpd.Model.Lookup.IsNullOrEmpty())
                                State.Drawers.DetailsActive = tpd.Model.Value.As<bool>();
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
            ExecuteActionRequest exActReq, ISecurityDataTokenService secMgr, DocumentClient docClient)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            var tpd = await secMgr.GetDataToken(TELEMETRY_SYNC_ENABLED, State.UserEnterpriseLookup);

                            if (tpd.Status && tpd.Model.Lookup=="TELEMETRY_SYNC_ENABLED" && !tpd.Model.Lookup.IsNullOrEmpty())
                                State.Drawers.DetailsActive = tpd.Model.Value.As<bool>();
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

        public virtual async Task EnsureUserEnterprise(IEnterprisesAsCodeService eacSvc, IEnterprisesHostingManagerService hostMgrSvc,
            ISecurityDataTokenService dataTokenSvc, string parentEntLookup, string username)
        {
            if (State.DevicesConfig != null)
                State.DevicesConfig.Status = null;

            if (State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                            {
                            var userHost = $"{parentEntLookup}|{username}";

                            log.LogInformation($"Ensuring child enterprise for {userHost}.");

                            var hostResp = await hostMgrSvc.ResolveHost(userHost);

                            if (hostResp.Model == null)
                            {
                                var commitReq = new CommitEnterpriseAsCodeRequest()
                                {
                                    EaC = new EnterpriseAsCode()
                                    {
                                        Enterprise = new EaCEnterpriseDetails()
                                        {
                                            Name = $"{username} Enterprise",
                                            Description = $"{username} Enterprise",
                                            ParentEnterpriseLookup = parentEntLookup,
                                            PrimaryEnvironment = userHost,
                                            PrimaryHost = userHost
                                        },
                                        Hosts = new Dictionary<string, EaCHost>()
                                        {
                                            {userHost, new EaCHost()}                                           
                                        },
                                        AccessRights = new Dictionary<string, EaCAccessRight>()
                                        {
                                            {
                                                "Fathym.Global.Admin",
                                                new EaCAccessRight()
                                                {
                                                    Name = "Fathym.Global.Admin",
                                                    Description = "Fathym.Global.Admin",
                                                }
                                            },
                                            {
                                                "Fathym.User",
                                                new EaCAccessRight()
                                                {
                                                    Name = "Fathym.User",
                                                    Description = "Fathym.User",
                                                }
                                            }
                                        },
                                        DataTokens = new Dictionary<string, EaCDataToken>(),
                                        Providers = new Dictionary<string, EaCProvider>()
                                        {
                                            {
                                                "ADB2C", 
                                                new EaCProvider()
                                                {
                                                    Name = "ADB2C",
                                                    Description = "ADB2C Provider",
                                                    Type = "ADB2C"
                                                } 
                                            }
                                        },
                                        Environments = new Dictionary<string, EaCEnvironmentAsCode>()
                                        {
                                            {
                                                userHost,
                                                new EaCEnvironmentAsCode()
                                                {
                                                    Environment = new EaCEnvironmentDetails()
                                                    {
                                                        Name = $"{username} Environment",
                                                        Description = $"{username} Environment"
                                                    }
                                                }
                                            }
                                        }
                                    },

                                    Username = username
                                };

                                // string adb2cAppId = null;

                                // if (hostResp.Status)
                                // {
                                //     var adB2cAppIdToken = await dataTokenSvc.GetDataToken(EnterpriseContext.AD_B2C_APPLICATION_ID_LOOKUP, entLookup: hostResp.Model?.Lookup);

                                //     adb2cAppId = adB2cAppIdToken?.Model?.Value;
                                // }

                                // if (adb2cAppId.IsNullOrEmpty() && !parentEntLookup.IsNullOrEmpty())
                                // {
                                //     //  TODO:  Create unique application in ADB2C to allow for multi tenant control of sign in

                                //     var adB2cAppIdToken = await dataTokenSvc.GetDataToken(EnterpriseContext.AD_B2C_APPLICATION_ID_LOOKUP, entLookup: parentEntLookup);

                                //     adb2cAppId = adB2cAppIdToken?.Model?.Value;

                                //     commitReq.EaC.Providers.Add("ADB2C", new EaCProvider()
                                //     {
                                //         Name = "ADB2C",
                                //         Description = "ADB2C Provider",
                                //         Type = "ADB2C",
                                //         Metadata = new Dictionary<string, JToken>()
                                //         {
                                //             { "ApplicationID", EnterpriseContext.AD_B2C_APPLICATION_ID_LOOKUP },
                                //             { "Authority", "fathymcloudprd.onmicrosoft.com" }
                                //         }
                                //     });

                                //     commitReq.EaC.DataTokens[EnterpriseContext.AD_B2C_APPLICATION_ID_LOOKUP] = new EaCDataToken()
                                //     {
                                //         Value = adb2cAppId,
                                //         Name = "AD B2C Application ID",
                                //         Description = "The AD B2C application ID used with authentication."
                                //     };
                                // }

                                var commitResp = await eacSvc.Commit(commitReq);

                                if (commitResp.Status)
                                {
                                    log.LogInformation($"Ensured child enterprise for {userHost}.");

                                    hostResp = await hostMgrSvc.ResolveHost(userHost);

                                    var parentGitHubDataToken = await dataTokenSvc.GetDataToken("LCU-GITHUB-ACCESS-TOKEN", entLookup: parentEntLookup, email: username);

                                    if (parentGitHubDataToken.Model != null)
                                    {
                                        log.LogInformation($"Transferring GitHub access to child enterprise for {hostResp.Model.Lookup}.");

                                        var setDTResp = await dataTokenSvc.SetDataToken(new DataToken()
                                        {
                                            Name = parentGitHubDataToken.Model.Name,
                                            Description = parentGitHubDataToken.Model.Description,
                                            Lookup = parentGitHubDataToken.Model.Lookup,
                                            Value = parentGitHubDataToken.Model.Value
                                        }, entLookup: hostResp.Model.Lookup, email: username);
                                    }
                                    State.UserEnterpriseLookup = hostResp.Model.Lookup;
                                }
                            }

                            log.LogInformation($"Ensuring child enterprise for {userHost}");

                            State.UserEnterpriseLookup = hostResp.Model.Lookup;

                            return true;
                        }

                        catch (Exception ex)
                        {
                            log.LogError(ex, "Failed ensuring user enterprise");

                            return false;
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

        public virtual async Task<Status> HasLicenseAccess(IIdentityAccessService idMgr, string entLookup, string username)
        {
            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var hasAccess = await idMgr.HasLicenseAccess(entLookup, username, AllAnyTypes.All, new List<string>() { "iot" });

                        State.HasAccess = hasAccess.Status;

                        if (State.HasAccess)
                        {
                            if (hasAccess.Model.Metadata.ContainsKey("LicenseType"))
                                State.AccessLicenseType = hasAccess.Model.Metadata["LicenseType"].ToString();

                            if (hasAccess.Model.Metadata.ContainsKey("PlanGroup"))
                                State.AccessPlanGroup = hasAccess.Model.Metadata["PlanGroup"].ToString();

                            if (hasAccess.Model.Metadata.ContainsKey("Devices"))

                            if(hasAccess.Model.Metadata.ContainsKey("DataInterval"))
                                State.DataInterval = (int) hasAccess.Model.Metadata["DataInterval"];

                            if(hasAccess.Model.Metadata.ContainsKey("DataRetention"))
                                State.DataRetention = (int) hasAccess.Model.Metadata["DataRetention"];
                        }
                        else
                        {
                            State.AccessLicenseType = "iot";

                            State.AccessPlanGroup = "explorer";

                            State.DevicesConfig.MaxDevicesCount = 1;
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

        public virtual async Task IssueDeviceSASToken(IApplicationsIoTService appIoTArch, string deviceName, int expiryInSeconds)
        {
            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var deviceSasResp = await appIoTArch.IssueDeviceSASToken(State.UserEnterpriseLookup, deviceName, expiryInSeconds: expiryInSeconds,
                            envLookup: null);

                        if (deviceSasResp.Status)
                        {
                            if (State.DevicesConfig.SASTokens == null)
                                State.DevicesConfig.SASTokens = new Dictionary<string, string>();

                            State.DevicesConfig.SASTokens[deviceName] = deviceSasResp.Model;
                        }

                        return State.DevicesConfig.SASTokens == null;
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

        public virtual async Task<List<string>> ListAllDeviceNames(IApplicationsIoTService appIoTArch, string childEntLookup, string filter)
        {
            var deviceNames = new List<string>();

            var devices = await loadDevices(appIoTArch, childEntLookup, 1, 100);

            deviceNames = devices?.Items?.Select(device => device.DeviceName).Where(deviceName =>
            {
                return filter.IsNullOrEmpty() || deviceName.Contains(filter);
            }).ToList();

            return deviceNames;
        }

        public virtual async Task<Status> LoadAPIKeys(IEnterprisesAPIManagementService entApiArch, string entLookup, string username)
        {
            State.Storage.APIKeys = new List<APIAccessKeyData>();

            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var resp = await entApiArch.LoadAPIKeys(entLookup, buildSubscriptionType(), username);

                        //  TODO:  Handle API error

                        State.Storage.APIKeys = resp.Model?.Metadata.Select(m => new APIAccessKeyData()
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
            State.Storage.OpenAPISource = Environment.GetEnvironmentVariable("OPEN-API-SOURCE-URL");

            return Status.Success;
        }

        public virtual async Task LoadDevices(IApplicationsIoTService appIoTArch)
        {
            var devices = await loadDevices(appIoTArch, State.UserEnterpriseLookup, State.DevicesConfig.Page, State.DevicesConfig.PageSize);
            if (devices != null)
            {
                State.DevicesConfig.Devices = devices.Items.ToList();

                State.DevicesConfig.TotalDevices = devices.TotalRecords;
            }

            State.DevicesConfig.SASTokens = null;
        }

        public virtual async Task<Status> LoadTelemetry(ISecurityDataTokenService secMgr, DocumentClient client)
        {
            var status = Status.Success;

            if (State.Telemetry.Page < 1)
                State.Telemetry.Page = 1;

            if (State.Telemetry.PageSize < 1)
                State.Telemetry.PageSize = 10;

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

            return status;
        }

        public virtual async Task Refresh(IDurableOrchestrationClient starter, StateDetails stateDetails, ExecuteActionRequest exActReq,
            IApplicationsIoTService appIoTArch, IEnterprisesAPIManagementService entApiArch, IEnterprisesAsCodeService eacSvc, IEnterprisesHostingManagerService entHostMgr, IIdentityAccessService idMgr,
            ISecurityDataTokenService secMgr, DocumentClient client)
        {
            await EnsureUserEnterprise(eacSvc, entHostMgr, secMgr, stateDetails.EnterpriseLookup, stateDetails.Username);

            await Task.WhenAll(
                LoadDevices(appIoTArch),
                HasLicenseAccess(idMgr, stateDetails.EnterpriseLookup, stateDetails.Username),
                EnsureEmulatedDeviceInfo(starter, stateDetails, exActReq, secMgr, client)
            );

            await Task.WhenAll(
                EnsureAPISubscription(entApiArch, stateDetails.EnterpriseLookup, stateDetails.Username),
                EnsureDevicesDashboard(secMgr),
                EnsureDrawersConfig(secMgr),
                LoadAPIOptions(),
                EnsureTelemetry(starter, stateDetails, exActReq, secMgr, client)
            );

            State.Loading = false;

            State.DevicesConfig.Loading = false;

            State.Emulated.Loading = false;

            State.Telemetry.Loading = false;
        }

        public virtual async Task<bool> RevokeDeviceEnrollment(IApplicationsIoTService appIoTArch, string deviceId)
        {
            var revoked = await revokeDeviceEnrollment(appIoTArch, State.UserEnterpriseLookup, deviceId);

            await LoadDevices(appIoTArch);

            return revoked;
        }

        public virtual async Task<Status> SendCloudMessage(IApplicationsIoTService appIoTArch, string deviceName, MetadataModel message)
        {
            var status = Status.Initialized;

            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var sendResp = await appIoTArch.SendCloudMessage(message, State.UserEnterpriseLookup, deviceName, envLookup: null);

                        status = sendResp.Status;

                        return !status;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, $"Failed sending cloud to device ({deviceName}) message");

                        return true;
                    }
                })
                .SetCycles(5)
                .SetThrottle(25)
                .SetThrottleScale(2)
                .Run();

            return status;
        }

        public virtual async Task<Status> SendDeviceMessage(IApplicationsIoTService appIoTArch, ISecurityDataTokenService secMgr,
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
                        var sendResp = await appIoTArch.SendDeviceMessage(payload, State.UserEnterpriseLookup,
                            deviceName, envLookup: null);

                        log.LogInformation($"Send Device ({deviceName}) Message Response {sendResp?.Status?.ToJSON()}: {payload?.ToJSON()}");

                        status = sendResp?.Status ?? Status.GeneralError.Clone("Send device returned no response.");

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

            await LoadTelemetry(secMgr, client);

            return status;
        }

        public virtual async Task ToggleDetailsPane(ISecurityDataTokenService secMgr)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                await DesignOutline.Instance.Retry()
                    .SetActionAsync(async () =>
                    {
                        try
                        {
                            var active = !State.Drawers.DetailsActive;

                            var resp = await secMgr.SetDataToken(new DataToken()
                            {
                                Lookup= "DETAILS_PANE_ENABLED", 
                                Value= active.ToString()
                            }, State.UserEnterpriseLookup);

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
            ExecuteActionRequest exActReq, ISecurityDataTokenService secMgr, DocumentClient client, bool skipTelem = false)
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
                            var resp = await secMgr.SetDataToken(new DataToken()
                            {
                                Lookup="EMULATED_DEVICE_ENABLED", 
                                Value=enabled.ToString()
                            }, State.UserEnterpriseLookup);

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

                    if (State.DevicesConfig.Devices.IsNullOrEmpty() && !skipTelem)
                    {
                        await setTelemetryEnabled(secMgr, enabled);

                        await EnsureTelemetrySyncState(starter, stateDetails, exActReq);
                    }
                }

                await LoadTelemetry(secMgr, client);
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        public virtual async Task ToggleTelemetrySyncEnabled(IDurableOrchestrationClient starter, StateDetails stateDetails,
            ExecuteActionRequest exActReq, ISecurityDataTokenService secMgr, DocumentClient client)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                await setTelemetryEnabled(secMgr, !State.Telemetry.Enabled);

                await EnsureTelemetrySyncState(starter, stateDetails, exActReq);

                await LoadTelemetry(secMgr, client);
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        public virtual async Task UpdateTelemetrySync(ISecurityDataTokenService secMgr, DocumentClient client, int refreshRate, int page, int pageSize, string payloadId=null)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                State.Telemetry.RefreshRate = refreshRate;

                State.Telemetry.Page = page;

                State.Telemetry.PageSize = pageSize;

                if(!payloadId.IsNullOrEmpty()){
                    State.ExpandedPayloadID = payloadId;
                    return;
                }

                await LoadTelemetry(secMgr, client);
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        public virtual async Task UpdateConnectedDevicesSync(IApplicationsIoTService appIoTArch, int page, int pageSize)
        {
            if (!State.UserEnterpriseLookup.IsNullOrEmpty())
            {
                State.DevicesConfig.Page = page;

                State.DevicesConfig.PageSize = pageSize;

                await LoadDevices(appIoTArch);
            }
            else
                throw new Exception("Unable to load the user's enterprise, please try again or contact support.");
        }

        #region Storage Access
        public virtual async Task<HttpResponseMessage> ColdQuery(CloudBlobDirectory coldBlob, List<string> selectedDeviceIds, int pageSize, int page,
            bool includeEmulated, DateTime? startDate, DateTime? endDate, ColdQueryResultTypes resultType, bool flatten,
            ColdQueryDataTypes dataType, bool zip, bool asFile)
        {
            var status = Status.GeneralError;

            HttpContent content = null;

            if (coldBlob != null)
            {
                try
                {
                    var fileExtension = getFileExtension(resultType);

                    var fileName = buildFileName(dataType, startDate.Value, endDate.Value, fileExtension);

                    log.LogInformation($"Loading {fileName} with extension {fileExtension}");

                    var entLookups = new List<string>() { State.UserEnterpriseLookup };

                    if (includeEmulated)
                        entLookups.Add("EMULATED");

                    var downloadedData = await downloadData(coldBlob, dataType, entLookups, startDate, endDate);

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

                    if (asFile)
                    {
                        content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment");

                        content.Headers.ContentDisposition.FileName = fileName;

                    }

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

            if (!pageSize.HasValue || pageSize.Value < 1){
                pageSize = 100;
                // pageSize = (int)State.Telemetry.TotalPayloads;
            }
                

            try
            {
                var payloads = await queryTelemetryPayloads(telemClient, State.UserEnterpriseLookup, selectedDeviceIds, pageSize.Value,
                    page.Value, includeEmulated);

                response.Payloads = payloads.Items.ToList();

                response.TotalPayloads = payloads.TotalRecords;

                response.Status = Status.Success;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "There was an issue loading your warm query.");

                response.Status = Status.GeneralError.Clone("There was an issue loading your warm query.",
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

        protected virtual string buildSubscriptionType()
        {
            return $"{State.AccessLicenseType}-{State.AccessPlanGroup}".ToLower();
        }

        protected virtual async Task<List<JObject>> downloadData(CloudBlobDirectory coldBlob, ColdQueryDataTypes dataType, List<string> entLookups,
            DateTime? startDate, DateTime? endDate)
        {
            BlobContinuationToken contToken = null;

            var downloadedData = new List<JObject>();

            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var downloadedDataDict = new Dictionary<DateTime, List<JObject>>();

                        await entLookups.Each(async entLookup =>
                        {
                            do
                            {
                                var dataTypeColdBlob = coldBlob.GetDirectoryReference(dataType.ToString().ToLower());

                                var entColdBlob = dataTypeColdBlob.GetDirectoryReference(entLookup);

                                log.LogInformation($"Listing blob segments for {entLookup} and continuation {contToken}...");

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
                                        log.LogInformation($"Adding blobs for {entLookup} and continuation {contToken} to downloads at {minTime}/{maxTime}");

                                        var blobContents = await blob.DownloadTextAsync();

                                        var blobData = JsonConvert.DeserializeObject<JArray>(blobContents);

                                        lock (downloadedDataDict)
                                        {
                                            if (downloadedDataDict.ContainsKey(maxTime))
                                                downloadedDataDict[maxTime].AddRange(blobData.ToObject<List<JObject>>());
                                            else
                                                downloadedDataDict.Add(maxTime, blobData.ToObject<List<JObject>>());
                                        }
                                    }
                                }, parallel: true);
                            } while (contToken != null);
                        }, parallel: true);

                        downloadedData = downloadedDataDict.OrderBy(dd => dd.Key).SelectMany(dd => dd.Value).ToList();

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

        protected virtual async Task<MetadataModel> loadDefaultFreeboardConfig()
        {
            var client = new HttpClient();

            var freeboardConfigStr = await client.GetStringAsync(Environment.GetEnvironmentVariable("FREEBOARD-CONFIG"));

            return freeboardConfigStr?.FromJSON<MetadataModel>();

            // return ("{\"version\":1,\"allow_edit\":true,\"plugins\":[],\"panes\":[{\"title\":\"\",\"width\":1,\"row\":{\"3\":1,\"4\":1},\"col\":{\"3\":1,\"4\":1},\"col_width\":10,\"widgets\":[{\"type\":\"text_widget\",\"settings\":{\"title\":\"\",\"size\":\"regular\",\"value\":\"Device Insights & Monitoring\",\"animate\":true}}]},{\"title\":\"Sensor Data\",\"width\":1,\"row\":{\"3\":5,\"4\":1,\"7\":1,\"11\":1,\"26\":1},\"col\":{\"3\":1,\"4\":2,\"7\":2,\"11\":2,\"26\":2},\"col_width\":3,\"widgets\":[{\"type\":\"text_widget\",\"settings\":{\"title\":\"Device ID\",\"size\":\"regular\",\"value\":\"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceID\\\"]\",\"animate\":true}},{\"type\":\"gauge\",\"settings\":{\"title\":\"Temperature\",\"value\":\"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Temperature\\\"]\",\"units\":\"\u00B0F\",\"min_value\":0,\"max_value\":\"150\"}},{\"type\":\"gauge\",\"settings\":{\"title\":\"Humidity\",\"value\":\"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Humidity\\\"]\",\"units\":\"%\",\"min_value\":0,\"max_value\":100}}]},{\"title\":\"Sensor Placement\",\"width\":1,\"row\":{\"3\":21,\"4\":5,\"5\":5,\"11\":5,\"28\":5},\"col\":{\"3\":1,\"4\":-8,\"5\":-8,\"11\":-8,\"28\":-8},\"col_width\":3,\"widgets\":[{\"type\":\"text_widget\",\"settings\":{\"title\":\"Floor\",\"size\":\"big\",\"value\":\"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Floor\\\"]\",\"animate\":true}},{\"type\":\"text_widget\",\"settings\":{\"title\":\"Occupancy\",\"size\":\"big\",\"value\":\"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Occupancy\\\"]\",\"animate\":true}}]},{\"title\":\"Temperature History\",\"width\":1,\"row\":{\"3\":31,\"11\":23,\"30\":23},\"col\":{\"3\":1,\"11\":1,\"30\":1},\"col_width\":3,\"widgets\":[{\"type\":\"text_widget\",\"settings\":{\"size\":\"regular\",\"value\":\"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Temperature\\\"]\",\"sparkline\":true,\"animate\":true,\"units\":\"\u00B0F\"}}]},{\"title\":\"Last Processed Device Data\",\"width\":1,\"row\":{\"3\":37,\"21\":25,\"24\":25,\"32\":25,\"36\":25},\"col\":{\"3\":1,\"21\":1,\"24\":1,\"32\":1,\"36\":1},\"col_width\":3,\"widgets\":[{\"type\":\"text_widget\",\"settings\":{\"title\":\"Device ID\",\"size\":\"regular\",\"value\":\"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"deviceid\\\"]\",\"animate\":true}},{\"type\":\"html\",\"settings\":{\"html\":\"JSON.stringify(datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1])\",\"height\":4}}]},{\"title\":\"Connected Devices (Last 3 Days)\",\"width\":1,\"row\":{\"3\":49,\"22\":25,\"25\":25,\"34\":25,\"37\":25},\"col\":{\"3\":1,\"22\":3,\"25\":3,\"34\":3,\"37\":3},\"col_width\":3,\"widgets\":[{\"type\":\"html\",\"settings\":{\"html\":\"JSON.stringify(Array.from(new Set(datasources[\\\"Fathym Device Data\\\"].map((q) => q.DeviceID))))\",\"height\":4}}]}],\"datasources\":[{\"name\":\"Fathym Device Data\",\"type\":\"JSON\",\"settings\":{\"url\":\"" + $"{telemetryRoot}\\/api\\/iot-ensemble\\/devices\\/telemetry" + "\",\"use_thingproxy\":true,\"refresh\":30,\"method\":\"GET\"}}],\"columns\":3}").JSONConvert<MetadataModel>();
            // return ("{\r\n\t\"version\": 1,\r\n\t\"allow_edit\": true,\r\n\t\"plugins\": [],\r\n\t\"panes\": [\r\n\t\t{\r\n\t\t\t\"title\": \"\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 1\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 10,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"\",\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"Device Insights & Monitoring\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Sensor Placement\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 5,\r\n\t\t\t\t\"4\": 5,\r\n\t\t\t\t\"5\": 5,\r\n\t\t\t\t\"11\": 5\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 3,\r\n\t\t\t\t\"4\": -8,\r\n\t\t\t\t\"5\": -8,\r\n\t\t\t\t\"11\": -8\r\n\t\t\t},\r\n\t\t\t\"col_width\": 1,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Floor\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Floor\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Room\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": [\r\n\t\t\t\t\t\t\t\"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Room\\\"]\"\r\n\t\t\t\t\t\t],\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Occupancy\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Occupancy\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Occupied\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Occupied\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Sensor Data\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 5,\r\n\t\t\t\t\"4\": 1,\r\n\t\t\t\t\"7\": 1,\r\n\t\t\t\t\"26\": 1\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 2,\r\n\t\t\t\t\"7\": 2,\r\n\t\t\t\t\"26\": 2\r\n\t\t\t},\r\n\t\t\t\"col_width\": 2,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Device ID\",\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceID\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"gauge\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Temperature\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Temperature\\\"]\",\r\n\t\t\t\t\t\t\"units\": \"\u00B0F\",\r\n\t\t\t\t\t\t\"min_value\": 0,\r\n\t\t\t\t\t\t\"max_value\": \"150\"\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"gauge\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Humidity\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Humidity\\\"]\",\r\n\t\t\t\t\t\t\"units\": \"%\",\r\n\t\t\t\t\t\t\"min_value\": 0,\r\n\t\t\t\t\t\t\"max_value\": 100\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Temperature History\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 23,\r\n\t\t\t\t\"11\": 23\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"11\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 3,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Temperature\\\"]\",\r\n\t\t\t\t\t\t\"sparkline\": true,\r\n\t\t\t\t\t\t\"animate\": true,\r\n\t\t\t\t\t\t\"units\": \"\u00B0F\"\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Map\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 29,\r\n\t\t\t\t\"4\": 9,\r\n\t\t\t\t\"11\": 9,\r\n\t\t\t\t\"15\": 9,\r\n\t\t\t\t\"27\": 9\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 1,\r\n\t\t\t\t\"11\": 1,\r\n\t\t\t\t\"15\": 1,\r\n\t\t\t\t\"27\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 10,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"google_map\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"lat\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Latitude\\\"]\",\r\n\t\t\t\t\t\t\"lon\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Longitude\\\"]\"\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Last Processed Device Data\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 39,\r\n\t\t\t\t\"21\": 25,\r\n\t\t\t\t\"24\": 25,\r\n\t\t\t\t\"36\": 25\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"21\": 1,\r\n\t\t\t\t\"24\": 1,\r\n\t\t\t\t\"36\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 2,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Device ID\",\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"deviceid\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"html\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"html\": \"JSON.stringify(datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1])\",\r\n\t\t\t\t\t\t\"height\": 4\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Connected Devices (Last 3 Days)\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 39,\r\n\t\t\t\t\"22\": 25,\r\n\t\t\t\t\"25\": 25,\r\n\t\t\t\t\"37\": 25\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 3,\r\n\t\t\t\t\"22\": 3,\r\n\t\t\t\t\"25\": 3,\r\n\t\t\t\t\"37\": 3\r\n\t\t\t},\r\n\t\t\t\"col_width\": 1,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"html\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"html\": \"JSON.stringify(Array.from(new Set(datasources[\\\"Fathym Device Data\\\"].map((q) => q.DeviceID))))\",\r\n\t\t\t\t\t\t\"height\": 4\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t}\r\n\t],\r\n\t\"datasources\": [\r\n\t\t{\r\n\t\t\t\"name\": \"Fathym Device Data\",\r\n\t\t\t\"type\": \"JSON\",\r\n\t\t\t\"settings\": {\r\n\t\t\t\t\"url\": \"" + $"{telemetryRoot}\\/api\\/iot-ensemble\\/devices\\/telemetry" + "\",\r\n\t\t\t\t\"use_thingproxy\": true,\r\n\t\t\t\t\"refresh\": 30,\r\n\t\t\t\t\"method\": \"GET\"\r\n\t\t\t}\r\n\t\t}\r\n\t],\r\n\t\"columns\": 3\r\n}").FromJSON<MetadataModel>();
            // return ("{\r\n\t\"version\": 1,\r\n\t\"allow_edit\": true,\r\n\t\"plugins\": [],\r\n\t\"panes\": [\r\n\t\t{\r\n\t\t\t\"title\": \"\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 1\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 10,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"\",\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"Device Insights & Monitoring\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Sensor Placement\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 5,\r\n\t\t\t\t\"4\": 5,\r\n\t\t\t\t\"5\": 5,\r\n\t\t\t\t\"11\": 5\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 3,\r\n\t\t\t\t\"4\": -8,\r\n\t\t\t\t\"5\": -8,\r\n\t\t\t\t\"11\": -8\r\n\t\t\t},\r\n\t\t\t\"col_width\": 1,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Floor\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Floor\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Room\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": [\r\n\t\t\t\t\t\t\t\"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Room\\\"]\"\r\n\t\t\t\t\t\t],\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Occupancy\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Occupancy\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Occupied\",\r\n\t\t\t\t\t\t\"size\": \"big\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Occupied\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Sensor Data\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 5,\r\n\t\t\t\t\"4\": 1,\r\n\t\t\t\t\"7\": 1,\r\n\t\t\t\t\"26\": 1\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 2,\r\n\t\t\t\t\"7\": 2,\r\n\t\t\t\t\"26\": 2\r\n\t\t\t},\r\n\t\t\t\"col_width\": 2,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Device ID\",\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceID\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"gauge\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Temperature\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Temperature\\\"]\",\r\n\t\t\t\t\t\t\"units\": \"\u00B0F\",\r\n\t\t\t\t\t\t\"min_value\": 0,\r\n\t\t\t\t\t\t\"max_value\": \"150\"\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"gauge\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Humidity\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Humidity\\\"]\",\r\n\t\t\t\t\t\t\"units\": \"%\",\r\n\t\t\t\t\t\t\"min_value\": 0,\r\n\t\t\t\t\t\t\"max_value\": 100\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Temperature History\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 23,\r\n\t\t\t\t\"11\": 23\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"11\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 3,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"SensorReadings\\\"][\\\"Temperature\\\"]\",\r\n\t\t\t\t\t\t\"sparkline\": true,\r\n\t\t\t\t\t\t\"animate\": true,\r\n\t\t\t\t\t\t\"units\": \"\u00B0F\"\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Map\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 29,\r\n\t\t\t\t\"4\": 9,\r\n\t\t\t\t\"11\": 9,\r\n\t\t\t\t\"15\": 9,\r\n\t\t\t\t\"27\": 9\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"4\": 1,\r\n\t\t\t\t\"11\": 1,\r\n\t\t\t\t\"15\": 1,\r\n\t\t\t\t\"27\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 10,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"google_map\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"lat\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Latitude\\\"]\",\r\n\t\t\t\t\t\t\"lon\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"DeviceData\\\"][\\\"Longitude\\\"]\"\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Last Processed Device Data\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 39,\r\n\t\t\t\t\"21\": 25,\r\n\t\t\t\t\"24\": 25,\r\n\t\t\t\t\"36\": 25\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1,\r\n\t\t\t\t\"21\": 1,\r\n\t\t\t\t\"24\": 1,\r\n\t\t\t\t\"36\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 2,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"title\": \"Device ID\",\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1][\\\"deviceid\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"html\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"html\": \"JSON.stringify(datasources[\\\"Fathym Device Data\\\"][datasources[\\\"Fathym Device Data\\\"].length - 1])\",\r\n\t\t\t\t\t\t\"height\": 4\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Connected Devices (Last 3 Days)\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 39,\r\n\t\t\t\t\"22\": 25,\r\n\t\t\t\t\"25\": 25,\r\n\t\t\t\t\"37\": 25\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 3,\r\n\t\t\t\t\"22\": 3,\r\n\t\t\t\t\"25\": 3,\r\n\t\t\t\t\"37\": 3\r\n\t\t\t},\r\n\t\t\t\"col_width\": 1,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"html\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"html\": \"JSON.stringify(Array.from(new Set(datasources[\\\"Fathym Device Data\\\"].map((q) => q.DeviceID))))\",\r\n\t\t\t\t\t\t\"height\": 4\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t}\r\n\t],\r\n\t\"datasources\": [\r\n\t\t{\r\n\t\t\t\"name\": \"Fathym Device Data\",\r\n\t\t\t\"type\": \"JSON\",\r\n\t\t\t\"settings\": {\r\n\t\t\t\t\"url\": \"" + $"{telemetryRoot}\\/api\\/iot-ensemble\\/devices\\/telemetry" + "\",\r\n\t\t\t\t\"use_thingproxy\": true,\r\n\t\t\t\t\"refresh\": 30,\r\n\t\t\t\t\"method\": \"GET\"\r\n\t\t\t}\r\n\t\t}\r\n\t],\r\n\t\"columns\": 3\r\n}").FromJSON<MetadataModel>();
            // return "{\r\n\t\"version\": 1,\r\n\t\"allow_edit\": true,\r\n\t\"plugins\": [],\r\n\t\"panes\": [\r\n\t\t{\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 1\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 3,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"Device Insights & Monitoring\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Last Processed Device Data\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 5\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 1\r\n\t\t\t},\r\n\t\t\t\"col_width\": 2,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"text_widget\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"size\": \"regular\",\r\n\t\t\t\t\t\t\"value\": \"datasources[\\\"Query\\\"][datasources[\\\"Query\\\"].length - 1][\\\"DeviceID\\\"]\",\r\n\t\t\t\t\t\t\"animate\": true\r\n\t\t\t\t\t}\r\n\t\t\t\t},\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"html\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"html\": \"JSON.stringify(datasources[\\\"Query\\\"][datasources[\\\"Query\\\"].length - 1])\",\r\n\t\t\t\t\t\t\"height\": 4\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t},\r\n\t\t{\r\n\t\t\t\"title\": \"Connected Devices (Last 3 Days)\",\r\n\t\t\t\"width\": 1,\r\n\t\t\t\"row\": {\r\n\t\t\t\t\"3\": 5\r\n\t\t\t},\r\n\t\t\t\"col\": {\r\n\t\t\t\t\"3\": 3\r\n\t\t\t},\r\n\t\t\t\"col_width\": 1,\r\n\t\t\t\"widgets\": [\r\n\t\t\t\t{\r\n\t\t\t\t\t\"type\": \"html\",\r\n\t\t\t\t\t\"settings\": {\r\n\t\t\t\t\t\t\"html\": \"JSON.stringify(Array.from(new Set(datasources[\\\"Query\\\"].map((q) => q.DeviceID))))\",\r\n\t\t\t\t\t\t\"height\": 4\r\n\t\t\t\t\t}\r\n\t\t\t\t}\r\n\t\t\t]\r\n\t\t}\r\n\t],\r\n\t\"datasources\": [\r\n\t\t{\r\n\t\t\t\"name\": \"Query\",\r\n\t\t\t\"type\": \"JSON\",\r\n\t\t\t\"settings\": {\r\n\t\t\t\t\"url\": \"\\/api\\/iot-ensemble\\/devices\\/telemetry\",\r\n\t\t\t\t\"use_thingproxy\": false,\r\n\t\t\t\t\"refresh\": 30,\r\n\t\t\t\t\"method\": \"GET\"\r\n\t\t\t}\r\n\t\t}\r\n\t],\r\n\t\"columns\": 3\r\n}".FromJSON<MetadataModel>();
        }

        protected virtual async Task<Pageable<IoTEnsembleDeviceInfo>> loadDevices(IApplicationsIoTService appIoTArch, string entLookup,
            int page, int pageSize)
        {
            var devices = new Pageable<IoTEnsembleDeviceInfo>()
            {
                Items = new List<IoTEnsembleDeviceInfo>(),
                TotalRecords = 0
            };

            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var devicesResp = await appIoTArch.ListEnrolledDevices(entLookup, envLookup: null,
                            page: page, pageSize: pageSize);

                        if (devicesResp.Status)
                        {
                            devices.Items = devicesResp.Model?.Items?.Select(m =>
                            {
                                var devInfo = m.JSONConvert<IoTEnsembleDeviceInfo>();

                                devInfo.DeviceName = devInfo.DeviceID.Replace($"{entLookup}-", String.Empty);

                                return devInfo;

                            }).JSONConvert<List<IoTEnsembleDeviceInfo>>() ?? new List<IoTEnsembleDeviceInfo>();

                            devices.TotalRecords = devicesResp.Model.TotalRecords;
                        }

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

            return devices;
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

        protected virtual async Task setTelemetryEnabled(ISecurityDataTokenService secMgr, bool enabled)
        {
            await DesignOutline.Instance.Retry()
                .SetActionAsync(async () =>
                {
                    try
                    {
                        var resp = await secMgr.SaveDataToken(new DataToken()
                        {
                            Lookup="TELEMETRY_SYNC_ENABLED",
                            Value=enabled.ToString()
                        }, State.UserEnterpriseLookup);

                        if (resp.Status)
                            State.Telemetry.Enabled = enabled;

                            State.Telemetry.IsTelemetryTimedOut = false;

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
