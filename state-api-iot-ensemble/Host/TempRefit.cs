using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Fathym.API;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Refit;


[assembly: FunctionsStartup(typeof(LCU.State.API.IoTEnsemble.Host.Startup))]

namespace LCU.State.API.IoTEnsemble.Host.TempRefit
{
    public interface IEnterprisesManagementService
    {
        [Delete("/management/enterprises/by-host/{host}")]
        Task<BaseResponse> DeleteEnterpriseByHost(string host, [Body] DeleteEnterpriseByLookupRequest request);

		[Post("/management/enterprises/by-lookup/{entLookup}")]
		Task<BaseResponse> DeleteEnterpriseByLookup(string entLookup, [Body] DeleteEnterpriseByLookupRequest request);

		[Get("/management/enterprise/by-lookup/{entLookup}")]
		Task<BaseResponse<Enterprise>> GetEnterprise(string entLookup);

		[Get("/management/enterprises/{entLookup}/children")]
		Task<BaseResponse<List<Enterprise>>> ListChildEnterprises(string entLookup);

        [Post("/billing/{entLookup}/stripe/subscription/user/{username}/{licenseType}/cancel")]
        Task<BaseResponse> CancelSubscriptionByUser(string username, string entLookup, string licenseType);
      
        [Get("/hosting/resolve/{host}")]
        Task<BaseResponse<EnterpriseContext>> ResolveHost(string host, bool isDevEnv);
	}

    public interface IIdentityManagerClient
    {
        [Get("{entLookup}/license/{username}/{allAny}")]
        Task<BaseResponse<Fathym.MetadataModel>> HasLicenseAccess(string entLookup,string username, Personas.AllAnyTypes allAny, List<string> licenseTypes);

        [Get("{entLookup}/licenses/{username}")]
        Task<BaseResponse<List<License>>> ListLicenses(string entLookup, string username, List<string> licenseTypes = null);

        [Post("{entLookup}/revoke")]
        Task<BaseResponse> RevokeAccessCard(RevokeAccessCardRequest request, string entLookup);

        [Delete("{entLookup}/license/{username}/{licenseType}")]
        Task<BaseResponse> RevokeLicenseAccess(string entLookup, string username, string licenseType);

        [Delete("{entLookup}/passport/{username}")]
		Task<BaseResponse> RevokePassport(string entLookup, string username);
    }

    [DataContract]
    public class DeleteEnterpriseByLookupRequest : BaseRequest
    {
        [DataMember]
        public virtual string Password { get; set; }
    }
	
    public class Enterprise
    {
        public virtual string Description { get; set; }

        public virtual Guid ID { get; set; }

        public virtual string Lookup { get; set; }

        public virtual string Name { get; set; }

        public virtual string PrimaryEnvironment { get; set; }

        public virtual string PrimaryHost { get; set; }
    }

    public class EnterpriseContext
    {
        public const string Lookup = "<DAF:Enterprise>";

        public const string ADB2CApplicationIDLookup = "AD-B2C-APPLICATION-ID";

        public virtual string ADB2CApplicationID { get; set; }

        public virtual int CacheSeconds { get; set; }

        public virtual string Host { get; set; }

        public virtual Guid ID { get; set; }

        public virtual bool IsDevEnv { get; set; }

        public virtual string EnterpriseLookup { get; set; }
        
        public virtual bool PreventDefaultApplications { get; set; }
    }

    public class License //': LCUVertex
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
}