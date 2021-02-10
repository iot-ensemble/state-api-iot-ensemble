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

namespace LCU.State.API.IoTEnsemble.Shared
{
    [Serializable]
    [DataContract]
    public class ListAllDeviceNamesRequest : BaseRequest
    {
        [DataMember]
        public virtual string ChildEntLookup { get; set; }

        [DataMember]
        public virtual string Filter { get; set; }
    }

    [Serializable]
    [DataContract]
    public class ListAllDeviceNamesResponse : BaseResponse
    {
        [DataMember]
        public virtual List<string> DeviceNames { get; set; }
    }

    public class ListAllDeviceNames
    {
        protected ApplicationArchitectClient appArch;

        public ListAllDeviceNames(ApplicationArchitectClient appArch)
        {
            this.appArch = appArch;
        }

        [FunctionName("ListAllDeviceNames")]
        public virtual async Task<HttpResponseMessage> Run([HttpTrigger] HttpRequest req, ILogger log,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob,
            DocumentClient telemClient)
        {
            var queried = new ListAllDeviceNamesResponse()
            {
                Status = Status.GeneralError
            };

            var status = await stateBlob.WithStateHarness<IoTEnsembleSharedState, ListAllDeviceNamesRequest, IoTEnsembleSharedStateHarness>(req, signalRMessages, log,
                async (harness, dataReq, actReq) =>
                {
                    log.LogInformation($"Running a ListAllDeviceNames Query: {dataReq}");

                    var stateDetails = StateUtils.LoadStateDetails(req);

                    if (dataReq == null)
                        dataReq = new ListAllDeviceNamesRequest();

                    queried.DeviceNames = await harness.ListAllDeviceNames(appArch, dataReq.ChildEntLookup, dataReq.Filter);

                    queried.Status = Status.Success;

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
