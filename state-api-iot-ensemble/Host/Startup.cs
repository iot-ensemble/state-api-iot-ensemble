using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using LCU.StateAPI.Hosting;
using System.Linq;

[assembly: FunctionsStartup(typeof(LCU.State.API.IoTEnsemble.Host.Startup))]

namespace LCU.State.API.IoTEnsemble.Host
{
    public class Startup : StateAPIStartup
    {
        #region Fields
        #endregion

        #region Constructors
        public Startup()
        { }
        #endregion

        #region API Methods
        public override void Configure(IFunctionsHostBuilder builder)
        {
            //  TODO: Refit client registration

            // builder.Services.AddLCUPersonas(null, null, null);

            var httpOpts = new LCUStartupHTTPClientOptions()
            {
                CircuitBreakDurationSeconds = 5,
                CircuitFailuresAllowed = 5,
                LongTimeoutSeconds = 60,
                RetryCycles = 3,
                RetrySleepDurationMilliseconds = 500,
                TimeoutSeconds = 30,
                Options = new System.Collections.Generic.Dictionary<string, LCU.Hosting.Options.LCUClientOptions>()
                {
                    {
                        nameof(IEnterprisesManagementService),
                        new LCU.Hosting.Options.LCUClientOptions()
                        {
                            BaseAddress = Environment.GetEnvironmentVariable($"{typeof(IEnterprisesManagementService).FullName}.BaseAddress")
                        }
                    }
                }
            };

            var registry = services.AddLCUPollyRegistry(httpOpts);

            services.AddLCUHTTPClient<IEnterprisesManagementService>(registry, httpOpts);
        }
        #endregion
    }
}