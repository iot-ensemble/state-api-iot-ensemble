using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Fathym;
using Fathym.API;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Converters;
using Polly;
using Polly.Registry;
using Refit;


[assembly: FunctionsStartup(typeof(LCU.State.API.IoTEnsemble.Host.Startup))]

namespace LCU.State.API.IoTEnsemble.Host.TempRefit
{
    public static class LCUStartupServiceExtensions{
        public static IPolicyRegistry<string> AddLCUPollyRegistry(this IServiceCollection services,
            LCUStartupHTTPClientOptions httpOpts)
        {
            var registry = services.AddPolicyRegistry();

            if (httpOpts != null)
            {
                var timeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(httpOpts.TimeoutSeconds));

                registry.Add("regular", timeout);

                var longTimeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(httpOpts.LongTimeoutSeconds));

                registry.Add("long", longTimeout);
            }

            return registry;
        }

        public static IHttpClientBuilder AddLCUTimeoutPolicy(this IHttpClientBuilder httpClientBuilder,
            IPolicyRegistry<string> registry)
        {
            return httpClientBuilder
                .AddPolicyHandler(request =>
                {
                    var timeoutPolicy = "regular";

                    if (request.Method != HttpMethod.Get)
                        timeoutPolicy = "long";

                    return registry.Get<IAsyncPolicy<HttpResponseMessage>>(timeoutPolicy);
                });
        }

        public static IHttpClientBuilder AddLCUHTTPClient<TClient>(this IServiceCollection services,
            IPolicyRegistry<string> registry, LCUStartupHTTPClientOptions httpOpts)
            where TClient : class
        {
            var clientName = typeof(TClient).Name;

            var clientOptions = httpOpts.Options[clientName];

            return services.AddLCUHTTPClient<TClient>(registry, new Uri(clientOptions.BaseAddress));
        } 

        public static IHttpClientBuilder AddLCUHTTPClient<TClient>(this IServiceCollection services,
            IPolicyRegistry<string> registry, Uri baseAddress, int retryCycles = 3, int retrySleepDurationMilliseconds = 500,
            int circuitFailuresAllowed = 5, int circuitBreakDurationSeconds = 5)
            where TClient : class
        {
            return services
                .AddRefitClient<TClient>(services =>
                {
                    return new RefitSettings()
                    {
                        ContentSerializer = new NewtonsoftJsonContentSerializer()
                    };
                })
                .ConfigureHttpClient(client =>
                {
                    client.BaseAddress = baseAddress;
                });
                
                //.AddLCUTimeoutPolicy(registry)
                //.AddTransientHttpErrorPolicy(p =>
                //{
                //    return p.WaitAndRetryAsync(retryCycles, _ =>
                //    {
                //        return TimeSpan.FromMilliseconds(retrySleepDurationMilliseconds);
                //    });
                //})
                //.AddTransientHttpErrorPolicy(p =>
                //{
                //    return p.CircuitBreakerAsync(circuitFailuresAllowed,
                //        TimeSpan.FromSeconds(circuitBreakDurationSeconds));
                //});
        }
    }
    public interface IApplicationsIoTService
    {
        [Post("/iot/{entLookup}/devices/enroll/{attestationType}/{enrollmentType}")]
        Task<EnrollDeviceResponse> EnrollDevice([Body] EnrollDeviceRequest request, string entLookup, DeviceAttestationTypes attestationType,
             DeviceEnrollmentTypes enrollmentType, [Query] string envLookup = null);

        [Get("/iot/{entLookup}/devices/{deviceName}")]
        Task<BaseResponse<string>> IssueDeviceSASToken(string entLookup, string deviceName, [Query] int expiryInSeconds = 3600,
            [Query] string envLookup = null);

        [Get("/iot/{entLookup}/devices/list")]
        Task<BaseResponse<Pageable<DeviceInfo>>> ListEnrolledDevices(string entLookup, [Query] string envLookup = null,
            [Query] int page = 1, [Query] int pageSize = 100);

        [Delete("/iot/{entLookup}/devices/{deviceId}/revoke")]
        Task<BaseResponse> RevokeDeviceEnrollment(string deviceId, string entLookup, [Query] string envLookup = null);

        [Post("/iot/{entLookup}/devices/from/{deviceName}/send")]
        Task<BaseResponse> SendDeviceMessage([Body] MetadataModel payload, string entLookup, string deviceName,
            [Query] string connStrType = "primary", [Query] string envLookup = null);

        [Post("/iot/{entLookup}/devices/to/{deviceName}/send")]
        Task<BaseResponse> SendCloudMessage([Body] MetadataModel request, string entLookup, string deviceName,
            [Query] string envLookup = null);
    }   

    public interface IEnterprisesAPIManagementService
	{
		[Post("/api-management/{entLookup}/api/subscription")]
		Task<BaseResponse> EnsureAPISubscription([Body] EnsureAPISubscriptionRequest request, string entLookup, [Query] string username);

		[Post("/api-management/{entLookup}/api/{subType}/keys")]
		Task<BaseResponse> GenerateAPIKeys([Body] GenerateAPIKeysRequest request, string entLookup, string subType, [Query] string username);

		[Get("/api-management/{entLookup}/api/{subType}/keys")]
		Task<BaseResponse<MetadataModel>> LoadAPIKeys(string entLookup, string subType, [Query] string username);

		[Get("/api-management/{entLookup}/api/{subType}/keys/{apiKey}/validate")]
		Task<BaseResponse<MetadataModel>> VerifyAPIKey(string entLookup, string subType, string apiKey);
	}

    public interface IEnterprisesAsCodeService 
    {
        [Post("/eac/commit")]
        Task<BaseResponse> Commit([Body] CommitEnterpriseAsCodeRequest request);

        [Get("/eac/export/{entLookup}")]
        Task<BaseResponse<EnterpriseAsCode>> Export(string entLookup);
    }

    // public interface IEnterprisesBootService
    // {
    //     [Post("/boot/registry")]
    //     Task<BaseResponse<EnterpriseContext>> Boot([Body] BootEnterpriseRequest request, bool cleanBoot = false);
    // }

    public interface IEnterprisesHostingManagerService
    {
        [Get("/hosting/{entLookup}/hosts")]
        Task<BaseResponse<List<Host>>> ListHosts(string entLookup);

        [Get("/hosting/resolve/{host}")]
        Task<BaseResponse<EnterpriseContext>> ResolveHost(string host);
    }

    public interface IEnterprisesManagementService
    {
        //Management
        [Delete("/management/enterprises/by-host/{host}")]
        Task<BaseResponse> DeleteEnterpriseByHost(string host, [Body] DeleteEnterpriseByLookupRequest request);

		[Post("/management/enterprises/by-lookup/{entLookup}")]
		Task<BaseResponse> DeleteEnterpriseByLookup(string entLookup, [Body] DeleteEnterpriseByLookupRequest request);

		[Get("/management/enterprise/by-lookup/{entLookup}")]
		Task<BaseResponse<Enterprise>> GetEnterprise(string entLookup);

		[Get("/management/enterprises/{entLookup}/children")]
		Task<BaseResponse<List<Enterprise>>> ListChildEnterprises(string entLookup);

        //Billing
        [Post("/billing/{entLookup}/stripe/subscription/user/{username}/{licenseType}/cancel")]
        Task<BaseResponse> CancelSubscriptionByUser(string username, string entLookup, string licenseType);
	}

    public interface IIdentityAccessService
    {
        [Get("/access/{entLookup}/license/{username}/{allAny}")]
        Task<BaseResponse<MetadataModel>> HasLicenseAccess(string entLookup, string username, AllAnyTypes allAny, [Query] List<string> licenseTypes);

        [Get("/access/{entLookup}/{projectId}/access-rights/{username}")]
        Task<BaseResponse<List<AccessRight>>> ListAccessRights(string entLookup, Guid projectId, string username);

        [Get("/access/{entLookup}/licenses")]
        Task<BaseResponse<List<License>>> ListLicenses(string entLookup, [Query] List<string> licenseTypes = null);

        [Get("/access/{entLookup}/licenses/{username}")]
        Task<BaseResponse<List<License>>> ListLicensesByUsername(string entLookup, string username, [Query] List<string> licenseTypes = null);

        [Post("/access/{entLookup}/revoke")]
        Task<BaseResponse> RevokeAccessCard([Body] RevokeAccessCardRequest request, string entLookup);

        [Delete("/access/{entLookup}/license/{username}/{licenseType}")]
        Task<BaseResponse> RevokeLicense(string entLookup, string username, string licenseType);

        [Delete("/access/{entLookup}/passport/{username}")]
        Task<BaseResponse> RevokePassport(string entLookup, string username);
    }

    // public interface IIdentityManagerClient
    // {

    //     [Get("/access/{entLookup}/{projectId}/access-rights/{username}")]
    //     Task<BaseResponse<List<AccessRight>>> ListAccessRights(string entLookup, Guid projectId, string username);

    //     [Get("/access/{entLookup}/license/{username}/{allAny}")]
    //     Task<BaseResponse<Fathym.MetadataModel>> HasLicenseAccess(string entLookup,string username, Personas.AllAnyTypes allAny, [Query] List<string> licenseTypes);

    //     [Get("/access/{entLookup}/licenses/{username}")]
    //     Task<BaseResponse<List<License>>> ListLicenses(string entLookup, string username, [Query] List<string> licenseTypes = null);   

    //     [Post("/access/{entLookup}/revoke")]
    //     Task<BaseResponse> RevokeAccessCard([Body] RevokeAccessCardRequest request, string entLookup);

    //     [Delete("/access/{entLookup}/license/{username}/{licenseType}")]
    //     Task<BaseResponse> RevokeLicenseAccess(string entLookup, string username, string licenseType);

    //     [Delete("/access/{entLookup}/passport/{username}")]
	// 	Task<BaseResponse> RevokePassport(string entLookup, string username);
    // }  

    public interface ISecurityDataTokenService
    {
        [Get("/data-tokens/{tokenLookup}")]
        Task<BaseResponse<DataToken>> GetDataToken(string tokenLookup,
        [Query] string entLookup = null, [Query] string email = null, [Query] Guid? projectId = null,
        [Query] Guid? appId = null, [Query] Guid? passportId = null, [Query] Guid? licenseId = null,
        [Query] bool cascadeChecks = true);

        [Post("/data-tokens/save")]
        Task<BaseResponse<DataToken>> SaveDataToken([Body] DataToken dataToken,
        [Query] string entLookup = null, [Query] string email = null);

        [Post("/data-tokens")]
        Task<BaseResponse<DataToken>> SetDataToken([Body] DataToken dataToken,
        [Query] string entLookup = null, [Query] string email = null, [Query] Guid? projectId = null,
        [Query] Guid? appId = null, [Query] Guid? passportId = null, [Query] Guid? licenseId = null);
    }
    
    [DataContract]
    public class AccessRight : LCUVertex
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual string Name { get; set; }
    }

    [DataContract]
    public class APILowCodeUnit : LowCodeUnit
    {
        [DataMember]
        public virtual string APIRoot { get; set; }

        [DataMember]
        public virtual string Security { get; set; }

        //  TODO:  Fucntion mapping, AzureFunctionLowCodeUnit?
    }
	
    [DataContract]
    public class AzureCloud : Cloud
    {
        [DataMember]
        public virtual string ApplicationID { get; set; }

        [DataMember]
        public virtual string AuthKey { get; set; }

        [DataMember]
        public virtual string SubscriptionID { get; set; }

        [DataMember]
        public virtual string TenantID { get; set; }
    }
    
    [DataContract]
    public class BootEnterpriseRequest : BaseRequest
    {
        [DataMember]
        public virtual string ADB2CApplicationID { get; set; }

        [DataMember]
        public virtual AzureCloud Azure { get; set; }

        [DataMember]
        public virtual List<DataToken> DataTokens { get; set; }

        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string EnterpriseLookup { get; set; }

        [DataMember]
        public virtual LCUEnvironment Environment { get; set; }

        [DataMember]
        public virtual List<string> Hosts { get; set; }

        [DataMember]
        public virtual IDictionary<string, BootProject> Projects { get; set; }

        [DataMember]
        public virtual IDictionary<string, DFSModifier> Modifiers { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual string ParentEnterpriseLookup { get; set; }
    }

    [DataContract]
    public class BootProject
    {
        [DataMember]
        public virtual BootAccess Access { get; set; }

        [DataMember]
        public virtual IDictionary<string, BootApplication> Applications { get; set; }

        [DataMember]
        public virtual List<DataToken> DataTokens { get; set; }

        [DataMember]
        public virtual string Host { get; set; }

        [DataMember]
        public virtual Guid ID { get; set; }

        [DataMember]
        public virtual string Name { get; set; }
    }

    [DataContract]
    public class BootAccess
    {
        [DataMember]
        public virtual List<string> Rights { get; set; }

        [DataMember]
        public virtual string Type { get; set; }
    }

    [DataContract]
    public class BootApplication
    {
        [DataMember]
        public virtual List<DataToken> DataTokens { get; set; }

        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual BootDFSProcessor DFS { get; set; }

        [DataMember]
        public virtual Guid ID { get; set; }

        [DataMember]
        public virtual bool IsPrivate { get; set; }

        [DataMember]
        public virtual bool IsTriggerSignIn { get; set; }

        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual List<BootOAuthProcessor> OAuths { get; set; }

        [DataMember]
        public virtual string Path { get; set; }

        [DataMember]
        public virtual int Priority { get; set; }

        [DataMember]
        public virtual Dictionary<string, BootProxyProcessor> Proxies { get; set; }

        [DataMember]
        public virtual List<string> Rights { get; set; }
    }

    [DataContract]
    public class BootDFSProcessor
    {
        [DataMember]
        public virtual List<BootDevOpsAction> Actions { get; set; }

        [DataMember]
        public virtual GitHubLowCodeUnit GitHub { get; set; }

        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual List<BootDFSModifier> Modifiers { get; set; }

        [DataMember]
        public virtual NPMLowCodeUnit NPM { get; set; }

        [DataMember]
        public virtual ZipLowCodeUnit Zip { get; set; }
    }

    [DataContract]
    public class BootProxyProcessor
    {
        [DataMember]
        public virtual APILowCodeUnit API { get; set; }

        [DataMember]
        public virtual string InboundPath { get; set; }

        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual List<BootDFSModifier> Modifiers { get; set; }

        [DataMember]
        public virtual SPALowCodeUnit SPA { get; set; }
    }

    [DataContract]
    public class BootOAuthProcessor
    {
        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual GitHubOAuthLowCodeUnit GitHub { get; set; }

        [DataMember]
        public virtual string Scopes { get; set; }

        [DataMember]
        public virtual string TokenLookup { get; set; }
    }

    [DataContract]
    public class BootDFSModifier
    {
        [DataMember]
        public virtual string Key { get; set; }

        [DataMember]
        public virtual string Parent { get; set; }
    }

    [DataContract]
    public class BootDevOpsAction
    {
        [DataMember]
        public virtual string Details { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual bool Overwrite { get; set; }

        [DataMember]
        public virtual string Path { get; set; }

        [DataMember]
        public virtual List<Secret> Secrets { get; set; }

        [DataMember]
        public virtual string Template { get; set; }
    }
    
    [DataContract]
    public class Cloud : LCUVertex
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Name { get; set; }
    }

    [DataContract]
    public class DataToken : LCUVertex
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual string Value { get; set; }
    }
    [DataContract]
    public class DeleteEnterpriseByLookupRequest : BaseRequest
    {
        [DataMember]
        public virtual string Password { get; set; }
    }

    [DataContract]
    public class DFSModifier : LCUVertex
    {
        [DataMember]
        public virtual string Details { get; set; }

        [DataMember]
        public virtual bool Enabled { get; set; }

        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual string PathFilterRegex { get; set; }

        [DataMember]
        public virtual int Priority { get; set; }

        [DataMember]
        public virtual string Type { get; set; }
    }

    [DataContract]
    public class EnsureAPISubscriptionRequest
    {
        [DataMember]
        public virtual string SubscriptionType { get; set; }

        [DataMember]
        public virtual Guid ProjectID { get; set; }
    }

    [DataContract]
    public enum DeviceAttestationTypes
    {
        [DataMember]
        SymmetricKey = 0,

        [DataMember]
        TrustedPlatformModule = 1,

        [DataMember]
        X509Certificate = 2
    }

    [DataContract]
    public enum AllAnyTypes
    {
        [DataMember]
        All = 0,

        [DataMember]
        Any = 1
    }

    [DataContract]
    public enum DeviceEnrollmentTypes
    {
        [DataMember]
        Group = 0,

        [DataMember]
        Individual = 1
    }

    [DataContract]
    public class DeviceInfo : MetadataModel
    {
        [DataMember]
        public virtual string DeviceID { get; set; }

        [DataMember]
        public virtual string ConnectionString { get; set; }
    }

    [DataContract]
    public class EnrollDeviceRequest : BaseRequest
    {
        [DataMember]
        public virtual MetadataModel AttestationOptions { get; set; }

        [DataMember]
        public virtual string DeviceID { get; set; }

        [DataMember]
        public virtual MetadataModel EnrollmentOptions { get; set; }
    }

    [DataContract]
    public class EnrollDeviceResponse : BaseResponse
    {
        [DataMember]
        public virtual DeviceInfo Device { get; set; }
    }

	[DataContract]
    public class Enterprise
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual Guid ID { get; set; }

        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual string PrimaryEnvironment { get; set; }

        [DataMember]
        public virtual string PrimaryHost { get; set; }
    }

    [DataContract]
    public class EnterpriseContext
    {
        #region Constants
        public const string LOOKUP = "<DAF:Enterprise>";

        public const string AD_B2C_APPLICATION_ID_LOOKUP = "AD-B2C-APPLICATION-ID";
        #endregion

        [DataMember]
        public virtual string ADB2CApplicationID { get; set; }

        [DataMember]
        public virtual int CacheSeconds { get; set; }

        [DataMember]
        public virtual string Host { get; set; }

        [DataMember]
        public virtual Guid ID { get; set; }

        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual Guid? ProjectID { get; set; }
    }

    public class License : LCUVertex
    {
        public DateTimeOffset AccessStartDate { get; set; }
        public Fathym.MetadataModel Details { get; set; }
        public DateTimeOffset ExpirationDate { get; set; }
        public bool EnterpriseOverride { get; set; }
        public bool IsLocked { get; set; }
        public bool IsReset { get; set; }
        public string Lookup { get; set; }
        public int TrialPeriodDays { get; set; }
        public string Username { get; set; }
    }

    public class RevokeAccessCardRequest : BaseRequest
    {
        //public RevokeAccessCardRequest();
        public virtual string AccessConfiguration { get; set; }

        public virtual string Username { get; set; }
    }

    [DataContract]
    public class GitHubLowCodeUnit : LowCodeUnit
    {
        [DataMember]
        public virtual string Branch { get; set; }

        [DataMember]
        public virtual string Organization { get; set; }
        
        [DataMember]
        public virtual string Repository { get; set; }
    }

    [DataContract]
    public class GitHubOAuthLowCodeUnit : LowCodeUnit
    {
        [DataMember]
        public virtual string ClientID { get; set; }
        
        [DataMember]
        public virtual string ClientSecret { get; set; }
    }

    [DataContract]
    public class Host : LCUVertex
    {
        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual bool Verified { get; set; }
    }

    [DataContract]
    public class LCUEnvironment : LCUVertex
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual string Name { get; set; }
    }

    [DataContract]
    public class LCUStartupHTTPClientOptions
    {
        [DataMember]
        public virtual int CircuitBreakDurationSeconds { get; set; }

        [DataMember]
        public virtual int CircuitFailuresAllowed { get; set; }

        [DataMember]
        public virtual int LongTimeoutSeconds { get; set; }

        [DataMember]
        public virtual Dictionary<string, LCUClientOptions> Options { get; set; }

        [DataMember]
        public virtual int RetryCycles { get; set; }

        [DataMember]
        public virtual int RetrySleepDurationMilliseconds { get; set; }

        [DataMember]
        public virtual int TimeoutSeconds { get; set; }
    }

    [DataContract]
    public class GenerateAPIKeysRequest
    {
        [DataMember]
        public virtual string KeyType { get; set; }
    }

    [DataContract]
    public class LCUClientOptions
    {
        [DataMember]
        public virtual string BaseAddress { get; set; }
    }

    [DataContract]
    public class LCUVertex
    {
        [DataMember]
        public virtual Guid ID { get; set; }

        [DataMember]
        public virtual string Label { get; set; }

        [DataMember]
        public virtual string Registry { get; set; }

        [DataMember]
        public virtual string TenantLookup { get; set; }
    }

    [DataContract]
    public class LowCodeUnit : LCUVertex
    {
        [DataMember]
        public virtual string Lookup { get; set; }
    }

    [DataContract]
    public class NPMLowCodeUnit : LowCodeUnit
    {
        [DataMember]
        public virtual string Package { get; set; }

        [DataMember]
        public virtual string Version { get; set; }
    }

    [DataContract]
    public class Secret : LCUVertex
    {
        [DataMember]
        public virtual string DataTokenLookup { get; set; }

        [DataMember]
        public virtual string KnownAs { get; set; }

        [DataMember]
        public virtual string Name { get; set; }
    }

    [DataContract]
    public class SPALowCodeUnit : LowCodeUnit
    {
        [DataMember]
        public virtual string SPARoot { get; set; }

        //  TODO:  Fucntion mapping, AzureFunctionLowCodeUnit?
    }

    [DataContract]
    public class ZipLowCodeUnit : LowCodeUnit
    {
        [DataMember]
        public virtual string ZipFile { get; set; }
    }

    [DataContract]
    public class CommitEnterpriseAsCodeRequest : BaseRequest
    {
        [DataMember]
        public virtual EnterpriseAsCode EaC { get; set; }

        [DataMember]
        public virtual string Username { get; set; }
    }

    [DataContract]
    public class EnterpriseAsCode
    {
        [DataMember]
        public virtual Dictionary<string, EaCAccessRight> AccessRights { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCApplicationAsCode> Applications { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCDataToken> DataTokens { get; set; }

        [DataMember]
        public virtual EaCEnterpriseDetails Enterprise { get; set; }

        [DataMember]
        public virtual string EnterpriseLookup { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCEnvironmentAsCode> Environments { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCHost> Hosts { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCLicenseConfiguration> LicenseConfigurations { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCDFSModifier> Modifiers { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCProjectAsCode> Projects { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCProvider> Providers { get; set; }
    }

    [DataContract]
    public class EaCHost
    {
        [DataMember]
        public virtual string HostDNSInstance { get; set; }

        [DataMember]
        public virtual bool Verified { get; set; }
    }


    [DataContract]
    public class EaCDFSModifier
    {
        [DataMember]
        public virtual string Details { get; set; }

        [DataMember]
        public virtual bool Enabled { get; set; }

        [DataMember]
        public virtual string Lookup { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual string PathFilterRegex { get; set; }

        [DataMember]
        public virtual int Priority { get; set; }

        [DataMember]
        public virtual string Type { get; set; }
    }

    [DataContract]
    public class EaCEnterpriseDetails
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual string ParentEnterpriseLookup { get; set; }

        [DataMember]
        public virtual string PrimaryEnvironment { get; set; }

        [DataMember]
        public virtual string PrimaryHost { get; set; }
    }

    [DataContract]
    public class EaCLicenseConfiguration
    {
        [DataMember]
        public virtual Dictionary<string, EaCPlan> Plans { get; set; }
    }

    [DataContract]
    public class EaCAccessRight
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Name { get; set; }
    }

    [DataContract]
    public class EaCDataToken
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual Guid ID { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual string Value { get; set; }
    }

    [DataContract]
    public class EaCPlan
    {
        [DataMember]
        public virtual string Details { get; set; }

        [DataMember]
        public virtual string[] Features { get; set; }

        [DataMember]
        public virtual bool Featured { get; set; }

        [DataMember]
        public virtual string HeaderName { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCPrice> Prices { get; set; }

        [DataMember]
        public virtual string Popular { get; set; }

        [DataMember]
        public virtual int Priority { get; set; }

        [DataMember]
        public virtual string SuccessRedirect { get; set; }
    }

    [DataContract]
    public class EaCPrice
    {
        [DataMember]
        public virtual string Currency { get; set; }

        [DataMember]
        public virtual float Discount { get; set; }

        [DataMember]
        public virtual string Interval { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual float Value { get; set; }
    }

    [DataContract]
    public class EaCEnvironmentAsCode
    {
        [DataMember]
        public virtual Dictionary<string, EaCArtifact> Artifacts { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCCloud> Clouds { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCDevOpsAction> DevOpsActions { get; set; }

        [DataMember]
        public virtual EaCEnvironmentDetails Environment { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCSecret> Secrets { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCSnapshotConfiguration> SnapshotConfigurations { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCSourceControl> Sources { get; set; }
    }

    [DataContract]
    public class EaCEnvironmentDetails
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Name { get; set; }
    }

    [DataContract]
    public class EaCCloud : MetadataModel
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCCloudResourceGroup> ResourceGroups { get; set; }

        [DataMember]
        public virtual string Type { get; set; }
    }

    [DataContract]
    public class EaCCloudResourceGroup
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Name { get; set; }
    }

    [DataContract]
    public class EaCDevOpsAction
    {
        [DataMember]
        public virtual List<string> ArtifactLookups { get; set; }

        [DataMember]
        public virtual List<string> DevOpsActionTriggerLookups { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual string Path { get; set; }

        [DataMember]
        public virtual List<string> SecretLookups { get; set; }

        [DataMember]
        public virtual List<string> Templates { get; set; }
    }

    [DataContract]
    public class EaCArtifact : MetadataModel
    {
        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual string Output { get; set; }

        [DataMember]
        public virtual string Type { get; set; }
    }

    [DataContract]
    public class EaCSecret : MetadataModel
    {
        [DataMember]
        public virtual string DataTokenLookup { get; set; }

        [DataMember]
        public virtual string KnownAs { get; set; }

        [DataMember]
        public virtual string Name { get; set; }
    }

    [DataContract]
    public class EaCSnapshotConfiguration
    {
        [DataMember]
        public virtual bool AllowRun { get; set; }

        [DataMember]
        public virtual string BlobConnection { get; set; }

        [DataMember]
        public virtual string BlobContainer { get; set; }

        [DataMember]
        public virtual string CosmosConnection { get; set; }

        [DataMember]
        public virtual string CosmosDatabase { get; set; }

        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Name { get; set; }
    }

    [DataContract]
    public class EaCSourceControl : MetadataModel
    {
        [DataMember]
        public virtual List<string> DevOpsActionTriggerLookups { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual string Type { get; set; }
    }

    [DataContract]
    public class EaCProjectAsCode
    {
        [DataMember]
        public virtual List<string> ApplicationLookups { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCDataToken> DataTokens { get; set; }

        [DataMember]
        public virtual List<string> Hosts { get; set; }

        [DataMember]
        public virtual List<string> ModifierLookups { get; set; }

        [DataMember]
        public virtual EaCProjectDetails Project { get; set; }

        [DataMember]
        public virtual EaCRelyingParty RelyingParty { get; set; }
    }

    [DataContract]
    public class EaCProjectDetails
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual bool IsInheritable { get; set; }

        [DataMember]
        public virtual bool IsInheritableByChild { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual bool PreventInheritedApplications { get; set; }
    }

    [DataContract]
    public class EaCRelyingParty
    {
        [DataMember]
        public virtual List<string> AccessRightLookups { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCAccessConfiguration> AccessConfigurations { get; set; }

        [DataMember]
        public virtual string DefaultAccessConfigurationLookup { get; set; }

        [DataMember]
        public virtual List<string> TrustedProviderLookups { get; set; }
    }

    [DataContract]
    public class EaCAccessConfiguration
    {
        [DataMember]
        public virtual List<string> AccessRightLookups { get; set; }

        [DataMember]
        public virtual List<string> ProviderLookups { get; set; }

        [DataMember]
        public virtual List<string> Usernames { get; set; }
    }

    [DataContract]
    public class EaCProvider : MetadataModel
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual string Type { get; set; }
    }

    [DataContract]
    public class EaCApplicationAsCode
    {
        [DataMember]
        public virtual List<string> AccessRightLookups { get; set; }

        [DataMember]
        public virtual EaCApplicationDetails Application { get; set; }

        [DataMember]
        public virtual Dictionary<string, EaCDataToken> DataTokens { get; set; }

        [DataMember]
        public virtual List<string> LicenseConfigurationLookups { get; set; }

        [DataMember]
        public virtual EaCApplicationLookupConfiguration LookupConfig { get; set; }

        [DataMember]
        public virtual EaCLowCodeUnit LowCodeUnit { get; set; }

        [DataMember]
        public virtual List<string> ModifierLookups { get; set; }

        [DataMember]
        public virtual EaCProcessor Processor { get; set; }

        [DataMember]
        public virtual string SourceControlLookup { get; set; }
    }

    [DataContract]
    public class EaCApplicationLookupConfiguration
    {
        [DataMember]
        [JsonConverter(typeof(StringEnumConverter))]
        public virtual AllAnyTypes AccessRightsAllAny { get; set; }

        [DataMember]
        public virtual List<string> AllowedMethods { get; set; }

        [DataMember]
        public virtual string HeaderRegex { get; set; }

        [DataMember]
        public virtual bool IsPrivate { get; set; }

        [DataMember]
        public virtual bool IsTriggerSignIn { get; set; }

        [DataMember]
        [JsonConverter(typeof(StringEnumConverter))]
        public virtual AllAnyTypes LicensesAllAny { get; set; }

        [DataMember]
        public virtual string PathRegex { get; set; }

        [DataMember]
        public virtual string QueryRegex { get; set; }

        [DataMember]
        public virtual string UserAgentRegex { get; set; }

        #region Constructors
        public EaCApplicationLookupConfiguration()
        {
            AccessRightsAllAny = AllAnyTypes.Any;

            LicensesAllAny = AllAnyTypes.All;
        }
        #endregion
    }

    [DataContract]
    public class EaCApplicationDetails
    {
        [DataMember]
        public virtual string Description { get; set; }

        [DataMember]
        public virtual string Name { get; set; }

        [DataMember]
        public virtual int Priority { get; set; }
    }

    [DataContract]
    public class EaCProcessor : MetadataModel
    {
        [DataMember]
        public virtual string CacheControl { get; set; }

        [DataMember]
        public virtual List<string> ModifierLookups { get; set; }

        [DataMember]
        public virtual int Priority { get; set; }

        [DataMember]
        public virtual string Type { get; set; }
    }

    [DataContract]
    public class EaCLowCodeUnit : MetadataModel
    {
        [DataMember]
        public virtual string Type { get; set; }
    }
}