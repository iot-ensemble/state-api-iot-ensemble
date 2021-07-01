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
             DeviceEnrollmentTypes enrollmentType, [Query] string username, [Query] string envLookup = null);

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

        //Hosting
        [Get("/hosting/resolve/{host}")]
        Task<BaseResponse<EnterpriseContext>> ResolveHost(string host, bool isDevEnv);
	}

    [DataContract]
    public class DeleteEnterpriseByLookupRequest : BaseRequest
    {
        [DataMember]
        public virtual string Password { get; set; }
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
        // [DataMember]
        // public const string Lookup = "<DAF:Enterprise>";

        // [DataMember]
        // public const string ADB2CApplicationIDLookup = "AD-B2C-APPLICATION-ID";

        [DataMember]
        public virtual string ADB2CApplicationID { get; set; }

        [DataMember]
        public virtual int CacheSeconds { get; set; }

        [DataMember]
        public virtual string Host { get; set; }

        [DataMember]
        public virtual Guid ID { get; set; }

        [DataMember]
        public virtual bool IsDevEnv { get; set; }

        [DataMember]
        public virtual string EnterpriseLookup { get; set; }

        [DataMember]
        public virtual bool PreventDefaultApplications { get; set; }
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
    public class LCUClientOptions
    {
        [DataMember]
        public virtual string BaseAddress { get; set; }
    }
}