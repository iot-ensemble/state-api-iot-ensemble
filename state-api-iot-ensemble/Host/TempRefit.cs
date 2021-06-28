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
}