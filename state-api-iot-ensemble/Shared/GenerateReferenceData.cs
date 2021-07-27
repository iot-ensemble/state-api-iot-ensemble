using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fathym;
using LCU.Graphs.Registry.Enterprises;
using LCU.Graphs.Registry.Enterprises.Identity;
using LCU.Personas.Client.Applications;
using LCU.Personas.Client.Enterprises;
using LCU.Personas.Client.Identity;
using LCU.Personas.Enterprises;
using LCU.State.API.IoTEnsemble.Host.TempRefit;
using LCU.State.API.IoTEnsemble.State;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using LCU.State.API.IoTEnsemble.Host.TempRefit;

namespace LCU.State.API.IoTEnsemble.Shared
{
    public class GenerateReferenceData
    {
        #region Fields
        protected readonly bool bypassGenerateRefData;

        protected readonly IEnterprisesManagementService entMgr;

        protected readonly IEnterprisesHostingManagerService entHostMgr;

        protected readonly IIdentityAccessService idMgr;

        protected ILogger log;

        protected readonly string parentEntLookup;
        #endregion

        public GenerateReferenceData(IEnterprisesManagementService entMgr, IIdentityAccessService idMgr, ILogger<GenerateReferenceData> log, IEnterprisesHostingManagerService entHostMgr)
        {
            this.entMgr = entMgr;

            this.entHostMgr = entHostMgr;

            this.idMgr = idMgr;

            this.log = log;

            parentEntLookup = Environment.GetEnvironmentVariable("LCU-ENTERPRISE-LOOKUP");

            bypassGenerateRefData = Environment.GetEnvironmentVariable("LCU-BYPASS-GENERATE-REFERENCE-DATA").As<bool>();
        }

        [FunctionName("GenerateReferenceData")]
        public virtual async Task Run([TimerTrigger("0 */1 * * * *")] TimerInfo myTimer,
            [Blob("iot-ensemble-cold-query/reference-data", FileAccess.Read, Connection = "LCU-COLD-STORAGE-CONNECTION-STRING")] CloudBlobDirectory refDataBlobDir)
        {
            var shouldGenerate = bypassGenerateRefData ? "bypass" : "generate";

            log.LogInformation($"Should generate reference data: {shouldGenerate}");
            log.LogInformation(Environment.GetEnvironmentVariable("LCU-BYPASS-GENERATE-REFERENCE-DATA"));

            if (!bypassGenerateRefData)
            {
                log.LogInformation($"Generating reference data");

                var refData = await loadReferenceData();

                log.LogInformation($"Loaded {refData?.Count ?? 0} reference data records");

                await uploadReferenceData(refDataBlobDir, refData);
            }
        }

        #region Helpers
        protected virtual async Task<List<IoTEnsembleEnterpriseReferenceData>> loadReferenceData()
        {
            var childEnts = await entMgr.ListChildEnterprises(parentEntLookup);

            var refData = new List<IoTEnsembleEnterpriseReferenceData>();

            var licenses = await idMgr.ListLicenses(parentEntLookup, new List<string>() { "iot" });

            if (childEnts.Status && licenses.Status)
                await childEnts.Model.Each(async childEnt =>
                {
                    var hosts = await entHostMgr.ListHosts(childEnt.Lookup);

                    var metadata = await processChildEnt(childEnt, hosts.Model, licenses.Model);

                    lock (refData)
                        refData.AddRange(metadata);
                }, parallel: true);

            return refData;
        }

        protected virtual async Task<List<IoTEnsembleEnterpriseReferenceData>> processChildEnt(Host.TempRefit.Enterprise childEnt, List<Host.TempRefit.Host> hosts,
            List<License> licenses)
        {
            var refData = new List<IoTEnsembleEnterpriseReferenceData>();

            await hosts.Each(async host =>
            {
                var hostLookupParts = host.Lookup.Split('|');

                if (hostLookupParts.Length >= 2)
                {
                    var parentLookup = hostLookupParts[0];

                    var username = hostLookupParts[1];

                    var license = licenses.FirstOrDefault(lic => lic.Username == username);

                    await idMgr.HasLicenseAccess(parentLookup, username, Host.TempRefit.AllAnyTypes.All, new List<string>() { "iot" });

                    IoTEnsembleEnterpriseReferenceData refd;

                    if (license != null)
                        refd = license.Details.JSONConvert<IoTEnsembleEnterpriseReferenceData>();
                    else
                        refd = null;
                    // refd = new IoTEnsembleEnterpriseReferenceData()
                    // {
                    //     Devices = 1,
                    //     DataInterval = 300,
                    //     DataRetention = 43200
                    // };

                    if (refd != null)
                    {
                        refd.EnterpriseLookup = childEnt.Lookup;

                        refData.Add(refd);
                    }
                }
            });

            return refData;
        }

        protected virtual async Task uploadReferenceData(CloudBlobDirectory refDataBlobDir, List<IoTEnsembleEnterpriseReferenceData> refData)
        {
            var now = DateTime.UtcNow;

            var dateBlobDir = refDataBlobDir.GetDirectoryReference($"{now.ToString("yyyy-MM-dd")}");

            var timeBlobDir = dateBlobDir.GetDirectoryReference($"{now.AddMinutes(1).ToString("HH-mm")}");

            var refDataBlob = timeBlobDir.GetBlockBlobReference("enterprise.ref-data.json");

            await refDataBlob.UploadTextAsync(refData.ToJSON());
        }
        #endregion
    }
}
