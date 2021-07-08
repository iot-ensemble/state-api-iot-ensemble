using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Fathym;
using Fathym.API;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Refit;

[assembly: FunctionsStartup(typeof(LCU.State.API.IoTEnsemble.Host.Startup))]

namespace LCU.State.API.IoTEnsemble.Host.TempRefit
{
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

        [Post("{/iot/entLookup}/devices/from/{deviceName}/send")]
        Task<BaseResponse> SendDeviceMessage([Body] MetadataModel payload, string entLookup, string deviceName,
            [Query] string connStrType = "primary", [Query] string envLookup = null);

        [Post("/iot/{entLookup}/devices/to/{deviceName}/send")]
        Task<BaseResponse> SendCloudMessage([Body] MetadataModel request, string entLookup, string deviceName,
            [Query] string envLookup = null);
    }
    
    public interface IEnterprisesBootService
    {
        [Post("/boot/registry")]
        Task<BaseResponse<EnterpriseContext>> Boot([Body] BootEnterpriseRequest request, bool cleanBoot = false);
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

    public interface IEnterprisesHostingManagerService
    {
        [Get("/hosting/{entLookup}/hosts")]
        Task<BaseResponse<List<Host>>> ListHosts(string entLookup);

        [Get("/hosting/resolve/{host}")]
        Task<BaseResponse<EnterpriseContext>> ResolveHost(string host);
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
    public enum DeviceAttestationTypes
    {
        SymmetricKey = 0,
        TrustedPlatformModule = 1,
        X509Certificate = 2
    }

    [DataContract]
    public enum DeviceEnrollmentTypes
    {
        Group = 0,
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
}