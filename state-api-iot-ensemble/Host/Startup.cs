using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using LCU.StateAPI.Hosting;
using System.Linq;
using LCU.State.API.IoTEnsemble.Host.TempRefit;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
            base.Configure(builder);

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
                Options = new System.Collections.Generic.Dictionary<string, LCUClientOptions>()
                {
                    {
                        nameof(IApplicationsIoTService),
                        new LCUClientOptions()
                        {
                            BaseAddress = Environment.GetEnvironmentVariable($"{typeof(IApplicationsIoTService).FullName}.BaseAddress")
                        }
                    },

                    {
                        nameof(IEnterprisesAPIManagementService),
                        new LCUClientOptions()
                        {
                            BaseAddress = Environment.GetEnvironmentVariable($"{typeof(IEnterprisesAPIManagementService).FullName}.BaseAddress")
                        }
                    },

                    {
                        nameof(IEnterprisesBootService),
                        new LCUClientOptions()
                        {
                            BaseAddress = Environment.GetEnvironmentVariable($"{typeof(IEnterprisesBootService).FullName}.BaseAddress")
                        }
                    },

                    {
                        nameof(IEnterprisesHostingManagerService),
                        new LCUClientOptions()
                        {
                            BaseAddress = Environment.GetEnvironmentVariable($"{typeof(IEnterprisesHostingManagerService).FullName}.BaseAddress")
                        }
                    },

                    {
                        nameof(IEnterprisesManagementService),
                        new LCUClientOptions()
                        {
                            BaseAddress = Environment.GetEnvironmentVariable($"{typeof(IEnterprisesManagementService).FullName}.BaseAddress")
                        }                 
                    },

                    {
                        nameof(IIdentityAccessService),
                        new LCUClientOptions()
                        {
                            BaseAddress = Environment.GetEnvironmentVariable($"{typeof(IIdentityAccessService).FullName}.BaseAddress")
                        }
                    },

                    {
                        nameof(ISecurityDataTokenService),
                        new LCUClientOptions()
                        {
                            BaseAddress = Environment.GetEnvironmentVariable($"{typeof(ISecurityDataTokenService).FullName}.BaseAddress")
                        }
                    }
                }
            };

            var registry = builder.Services.AddLCUPollyRegistry(httpOpts);

            // var loggerFactory = new LoggerFactory();

            // loggerFactory.CreateLogger<IApplicationsIoTService>();

            // loggerFactory.CreateLogger<IEnterprisesAPIManagementService>();

            // loggerFactory.CreateLogger<IEnterprisesBootService>();

            // loggerFactory.CreateLogger<IEnterprisesHostingManagerService>();

            // loggerFactory.CreateLogger<IEnterprisesManagementService>();

            // loggerFactory.CreateLogger<IIdentityAccessService>();

            // loggerFactory.CreateLogger<ISecurityDataTokenService>();

            // builder.Services.AddSingleton<ILoggerProvider, >(registry, httpOpts);

            builder.Services.AddLCUHTTPClient<IApplicationsIoTService>(registry, httpOpts);

            builder.Services.AddLCUHTTPClient<IEnterprisesAPIManagementService>(registry, httpOpts);

            builder.Services.AddLCUHTTPClient<IEnterprisesBootService>(registry, httpOpts);

            builder.Services.AddLCUHTTPClient<IEnterprisesHostingManagerService>(registry, httpOpts);

            builder.Services.AddLCUHTTPClient<IEnterprisesManagementService>(registry, httpOpts);

            builder.Services.AddLCUHTTPClient<IIdentityAccessService>(registry, httpOpts);

            builder.Services.AddLCUHTTPClient<ISecurityDataTokenService>(registry, httpOpts);
        }
        #endregion
    }
}