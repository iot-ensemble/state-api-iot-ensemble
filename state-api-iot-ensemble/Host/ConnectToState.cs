using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using LCU.StateAPI;
using Microsoft.Azure.Storage.Blob;
using LCU.StateAPI.Utilities;
using LCU.State.API.IoTEnsemble.State;

namespace LCU.State.API.IoTEnsemble.Host
{
    public class ConnectToState
    {
        protected ILogger log;
        public ConnectToState(ILogger<ConnectToState> log){
            this.log = log;
        }
        
        [FunctionName("ConnectToState")]
        public async Task<ConnectToStateResponse> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req,
            ClaimsPrincipal claimsPrincipal, //[LCUStateDetails]StateDetails stateDetails,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRMessage> signalRMessages,
            [SignalR(HubName = IoTEnsembleState.HUB_NAME)] IAsyncCollector<SignalRGroupAction> signalRGroupActions,
            [Blob("state-api/{headers.lcu-ent-lookup}/{headers.lcu-hub-name}/{headers.x-ms-client-principal-id}/{headers.lcu-state-key}", FileAccess.ReadWrite)] CloudBlockBlob stateBlob)
        {
            var stateDetails = StateUtils.LoadStateDetails(req);

            if (stateDetails.StateKey.StartsWith("admin"))
                return await signalRMessages.ConnectToState<IoTEnsembleAdminState>(req, log, claimsPrincipal, stateBlob, signalRGroupActions);
            else
                return await signalRMessages.ConnectToState<IoTEnsembleSharedState>(req, log, claimsPrincipal, stateBlob, signalRGroupActions);
        }
    }

    // 	public static class StateUtils2
	// {
	// 	public static string BuildGroupName(StateDetails stateDetails)
	// 	{
	// 		var username = !stateDetails.Username.IsNullOrEmpty() ? $"{stateDetails.Username}|" : null;

	// 		return $"{stateDetails.EnterpriseLookup}|{stateDetails.HubName}|{username}{stateDetails.StateKey}".ToMD5Hash();
	// 	}

	// 	public static string LoadEntLookup(string statePath)
	// 	{
	// 		var splits = statePath.Split('/');

	// 		return splits[0];
	// 	}

	// 	public static string LoadEntLookup(HttpRequest req)
	// 	{
	// 		var entLookup = req.Headers["lcu-ent-lookup"];

	// 		return entLookup;
	// 	}

	// 	public static string LoadAccessToken(HttpRequest req)
	// 	{
	// 		var entLookup = req.Headers["lcu-access-token"];

	// 		return entLookup;
	// 	}

	// 	public static string LoadAppID(HttpRequest req)
	// 	{
	// 		var entLookup = req.Headers["lcu-app-id"];

	// 		return entLookup;
	// 	}

	// 	public static string LoadHost(HttpRequest req)
	// 	{
	// 		var entLookup = req.Headers["lcu-host"];

	// 		return entLookup;
	// 	}

	// 	public static string LoadHubName(string statePath)
	// 	{
	// 		var splits = statePath.Split('/');

	// 		return splits[1];
	// 	}

	// 	public static string LoadHubName(HttpRequest req)
	// 	{
	// 		var entLookup = req.Headers["lcu-hub-name"];

	// 		return entLookup;
	// 	}

	// 	public static StateDetails LoadStateDetails(string statePath, string host)
	// 	{
	// 		var entLookup = StateUtils.LoadEntLookup(statePath);

	// 		var hubName = StateUtils.LoadHubName(statePath);

	// 		var stateKey = StateUtils.LoadStateKey(statePath);

	// 		var username = StateUtils.LoadUsername(statePath);

	// 		return new StateDetails()
	// 		{
	// 			EnterpriseLookup = entLookup,
	// 			Host = host,
	// 			HubName = hubName.Replace('-', '_'),
	// 			StateKey = stateKey,
	// 			Username = username
	// 		};
	// 	}

	// 	public static StateDetails LoadStateDetails(HttpRequest req)
	// 	{
	// 		return LoadStateDetails(req, req.HttpContext.User);
	// 	}

	// 	public static StateDetails LoadStateDetails(HttpRequest req, ClaimsPrincipal user)
	// 	{
	// 		var accessToken = "";

	// 		var entLookup = LoadEntLookup(req);

	// 		var hubName = LoadHubName(req);

	// 		var stateKey = LoadStateKey(req);

	// 		var host = LoadHost(req);

	// 		var appId = LoadAppID(req);

	// 		var userIdClaim = user?.FindFirst(ClaimTypes.NameIdentifier);

	// 		return new StateDetails()
	// 		{
	// 			AccessToken = accessToken,
	// 			ApplicationID = appId,
	// 			EnterpriseLookup = entLookup,
	// 			Host = host,
	// 			HubName = hubName.Replace('-', '_'),
	// 			StateKey = stateKey,
	// 			Username = userIdClaim?.Value ?? LoadUsername(req)
	// 		};
	// 	}

	// 	public static string LoadStateKey(string statePath)
	// 	{
	// 		var splits = statePath.Split('/');

	// 		return splits.Length == 3 ? splits[2] : splits[4];
	// 	}

	// 	public static string LoadStateKey(HttpRequest req)
	// 	{
	// 		var entLookup = req.Headers["lcu-state-key"];

	// 		return entLookup;
	// 	}

	// 	public static string LoadUsername(string statePath)
	// 	{
	// 		var splits = statePath.Split('/');

	// 		return splits.Length > 3 ? splits[2] : null;
	// 	}

	// 	public static string LoadUsername(HttpRequest req)
	// 	{
	// 		var unMock = req.Headers["x-ms-client-principal-id"];

	// 		return unMock;
	// 	}
	// }
}
