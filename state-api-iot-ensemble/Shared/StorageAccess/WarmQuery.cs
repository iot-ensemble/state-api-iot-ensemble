using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using Fathym;
using Microsoft.Azure.Storage.Blob;
using System.Runtime.Serialization;
using Fathym.API;
using System.Collections.Generic;
using System.Linq;
using LCU.Personas.Client.Applications;
using LCU.StateAPI.Utilities;
using System.Security.Claims;
using LCU.Personas.Client.Enterprises;
using LCU.State.API.IoTEnsemble.State;
using LCU.Personas.Client.Security;
using System.Net.Http;
using System.Net;
using System.Text;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.Documents.Client;

namespace LCU.State.API.IoTEnsemble.Shared.StorageAccess
{
    [Serializable]
    [DataContract]
    public class WarmQueryRequest : BaseRequest
    {
        [DataMember]
        public virtual DateTime? EndDate { get; set; }

        [DataMember]
        public virtual bool IncludeEmulated { get; set; }

        [DataMember]
        public virtual int? Page { get; set; }

        [DataMember]
        public virtual int? PageSize { get; set; }

        [DataMember]
        public virtual List<string> SelectedDeviceIDs { get; set; }

        [DataMember]
        public virtual DateTime? StartDate { get; set; }
    }

    public class WarmQuery
    {
        protected SecurityManagerClient secMgr;

        public WarmQuery(SecurityManagerClient secMgr)
        {
            this.secMgr = secMgr;
        }

        [FunctionName("WarmQuery")]
        public virtual async Task<HttpResponseMessage> Run([HttpTrigger] HttpRequest req, ILogger log,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob,
            [CosmosDB(
                databaseName: "%LCU-WARM-STORAGE-DATABASE%",
                collectionName: "%LCU-WARM-STORAGE-TELEMETRY-CONTAINER%",
                ConnectionStringSetting = "LCU-WARM-STORAGE-CONNECTION-STRING")]DocumentClient telemClient)
        {
            var queried = new IoTEnsembleTelemetryResponse()
            {
                Status = Status.GeneralError
            };

            var status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, WarmQueryRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
                {
                    log.LogInformation($"Running a WarmQuery: {dataReq}");

                    var stateDetails = StateUtils.LoadStateDetails(req);

                    if (dataReq == null)
                        dataReq = new WarmQueryRequest();

                    if (req.Query.ContainsKey("endDate"))
                        dataReq.EndDate = req.Query["endDate"].ToString().As<DateTime>();

                    if (req.Query.ContainsKey("includeEmulated"))
                        dataReq.IncludeEmulated = req.Query["includeEmulated"].ToString().As<bool>();

                    if (req.Query.ContainsKey("page"))
                        dataReq.Page = req.Query["page"].ToString().As<int>();

                    if (req.Query.ContainsKey("pageSize"))
                        dataReq.PageSize = req.Query["pageSize"].ToString().As<int>();

                    if (req.Query.ContainsKey("startDate"))
                        dataReq.StartDate = req.Query["startDate"].ToString().As<DateTime>();

                    if (req.Query.ContainsKey("selectedDevices"))
                        dataReq.SelectedDeviceIDs = req.Query["selectedDevices"].ToString().Split(',').ToList();

                    queried = await harness.WarmQuery(telemClient, dataReq.SelectedDeviceIDs, dataReq.PageSize, dataReq.Page,
                        dataReq.IncludeEmulated, dataReq.StartDate, dataReq.EndDate);

                    return queried.Status;
                }, preventStatusException: true, withLock: false);

            var statusCode = status ? HttpStatusCode.OK : HttpStatusCode.InternalServerError;

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(queried.ToJSON(), Encoding.UTF8, "application/json")
            };
        }
    }
}
