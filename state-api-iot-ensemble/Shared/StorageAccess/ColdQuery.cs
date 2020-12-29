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
using Newtonsoft.Json.Converters;
using System.Net.Http.Headers;
using LCU.Presentation.State.ReqRes;
using Microsoft.Extensions.Primitives;

namespace LCU.State.API.IoTEnsemble.Shared.StorageAccess
{
    [Serializable]
    [DataContract]
    public class ColdQueryRequest : BaseRequest
    {
        [DataMember]
        [JsonConverter(typeof(StringEnumConverter))]
        public virtual ColdQueryDataTypes DataType { get; set; }

        [DataMember]
        public virtual DateTime? EndDate { get; set; }

        [DataMember]
        public virtual bool Flatten { get; set; }

        [DataMember]
        public virtual bool IncludeEmulated { get; set; }

        [DataMember]
        public virtual int Page { get; set; }

        [DataMember]
        public virtual int PageSize { get; set; }

        [DataMember]
        [JsonConverter(typeof(StringEnumConverter))]
        public virtual ColdQueryResultTypes ResultType { get; set; }

        [DataMember]
        public virtual List<string> SelectedDeviceIDs { get; set; }

        [DataMember]
        public virtual DateTime? StartDate { get; set; }

        [DataMember]
        public virtual bool Zip { get; set; }
    }

    public class ColdQuery
    {
        protected SecurityManagerClient secMgr;

        public ColdQuery(SecurityManagerClient secMgr)
        {
            this.secMgr = secMgr;
        }

        [FunctionName("ColdQuery")]
        public virtual async Task<HttpResponseMessage> Run([HttpTrigger] HttpRequest req, ILogger log,
            [SignalR(HubName = IoTEnsembleSharedState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob,
            [Blob("cold-storage/data", FileAccess.Read, Connection = "LCU-COLD-STORAGE-CONNECTION-STRING")] CloudBlobDirectory coldBlob)
        {
            var queried = new HttpResponseMessage(); ;

            var status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, ColdQueryRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
                {
                    log.LogInformation($"Running a ColdQuery: {dataReq.ToJSON()}");

                    var stateDetails = StateUtils.LoadStateDetails(req);

                    if (req.Query.ContainsKey("dataType"))
                        dataReq.DataType = req.Query["dataType"].As<ColdQueryDataTypes>();

                    if (req.Query.ContainsKey("endDate"))
                        dataReq.EndDate = req.Query["endDate"].As<DateTime>();

                    if (req.Query.ContainsKey("flatten"))
                        dataReq.Flatten = req.Query["flatten"].As<bool>();

                    if (req.Query.ContainsKey("includeEmulated"))
                        dataReq.IncludeEmulated = req.Query["includeEmulated"].As<bool>();

                    if (req.Query.ContainsKey("page"))
                        dataReq.Page = req.Query["page"].As<int>();

                    if (req.Query.ContainsKey("pageSize"))
                        dataReq.PageSize = req.Query["pageSize"].As<int>();

                    if (req.Query.ContainsKey("resultType"))
                        dataReq.ResultType = req.Query["resultType"].As<ColdQueryResultTypes>();

                    if (req.Query.ContainsKey("startDate"))
                        dataReq.StartDate = req.Query["startDate"].As<DateTime>();

                    if (req.Query.ContainsKey("selectedDevices"))
                        dataReq.SelectedDeviceIDs = req.Query["selectedDevices"].ToString().Split(',').ToList();

                    if (req.Query.ContainsKey("zip"))
                        dataReq.Zip = req.Query["zip"].As<bool>();

                    var now = DateTime.UtcNow;

                    if (dataReq.StartDate == null)
                        dataReq.StartDate = now.AddDays(-30);

                    if (dataReq.EndDate == null)
                        dataReq.EndDate = now;

                    queried = await harness.ColdQuery(coldBlob, dataReq.SelectedDeviceIDs, dataReq.PageSize, dataReq.Page,
                        dataReq.IncludeEmulated, dataReq.StartDate, dataReq.EndDate, dataReq.ResultType, dataReq.Flatten, dataReq.DataType,
                        dataReq.Zip);

                    return Status.Success;
                }, preventStatusException: true);

            return queried;
        }
    }
}
