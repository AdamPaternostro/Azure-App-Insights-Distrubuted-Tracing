#r "Newtonsoft.Json"
#r "Microsoft.ApplicationInsights"
#r "System.Diagnostics.DiagnosticSource"
#r "../bin/Microsoft.Azure.WebJobs.ServiceBus.dll"

using Microsoft.ApplicationInsights;

public static void Run(
    [ServiceBusTrigger("appinsightstest", Connection = "ServiceBusConnection")] 
    string myQueueItem,
    System.Collections.Generic.IDictionary<String,Object> userProperties,
    ILogger log)
{
    log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");

    string requestContext = userProperties["RequestContext"].ToString();
    string requestId = userProperties["RequestId"].ToString();
    string traceparent = userProperties["traceparent"].ToString();
    string traceoperation = userProperties["traceoperation"].ToString();

    log.LogInformation($"Metadata requestContext: {requestContext}");
    log.LogInformation($"Metadata requestId: {requestId}");
    log.LogInformation($"Metadata traceparent: {traceparent}");
    log.LogInformation($"Metadata traceoperation: {traceoperation}");

    Microsoft.ApplicationInsights.TelemetryClient telemetryClient = new Microsoft.ApplicationInsights.TelemetryClient();
    telemetryClient.Context.User.AuthenticatedUserId = "adam.paternostro@microsoft.com";

    Microsoft.ApplicationInsights.DataContracts.RequestTelemetry requestTelemetry = new Microsoft.ApplicationInsights.DataContracts.RequestTelemetry();
    requestTelemetry.Name = "Process Service Bus Event: " + myQueueItem;
    requestTelemetry.Source = requestContext.Replace("appId=",string.Empty);
    requestTelemetry.Timestamp = System.DateTimeOffset.Now;
    requestTelemetry.Context.Operation.Id = traceoperation;
    requestTelemetry.Context.Operation.ParentId = traceparent;
    requestTelemetry.Context.User.AuthenticatedUserId = "adam.paternostro@microsoft.com";

    using (var requestBlock = telemetryClient.StartOperation<Microsoft.ApplicationInsights.DataContracts.RequestTelemetry>(requestTelemetry))
    {        
        ///////////////////////////////////////////////////
        // Create Dependency for future Azure Function processing
        // NOTE: I trick it by giving a Start Time Offset of Now.AddSeconds(1), so it sorts correctly in the Azure Portal UI
        ///////////////////////////////////////////////////
        string operationName = "Dependency: Azure Data Factory";
        string target = "05-disttrace-adf | cid-v1:" + System.Environment.GetEnvironmentVariable("ai_05_disttrace_web_app_appkey");
        string dependencyName = "Dependency Name: Azure Data Factory";
        Microsoft.ApplicationInsights.DataContracts.DependencyTelemetry dependencyTelemetry =
            new Microsoft.ApplicationInsights.DataContracts.DependencyTelemetry(
            operationName, target, dependencyName,
            "04-disttrace-func-bus", System.DateTimeOffset.Now.AddSeconds(1), System.TimeSpan.FromSeconds(2), "200", true);
        dependencyTelemetry.Context.Operation.Id = traceoperation;
        dependencyTelemetry.Context.Operation.ParentId = requestBlock.Telemetry.Id;
        // Store future parent id
        traceparent = dependencyTelemetry.Id;
        log.LogInformation($"New traceparent dependencyTelemetry.Id: {traceparent}");
        telemetryClient.TrackDependency(dependencyTelemetry);


        ///////////////////////////////////////////////////
        // Call Data Factory (via REST)
        ///////////////////////////////////////////////////
 
        
        ///////////////////////////////////////////////////
        // Set the REST calls headers
        // We need the values from the dependency
        ///////////////////////////////////////////////////
        // Request-Context: appId=cid-v1:{The App Id of the current App Insights Account}
        requestContext = "appId=cid-v1:" + System.Environment.GetEnvironmentVariable("ai_03_disttrace_web_app_appkey");
        log.LogInformation("Service Bus Message Metadata -> RequestContext:" + requestContext);
        //message.UserProperties.Add("RequestContext", requestContext);

        // Request-Id / traceparent: {parent request/operation id} (e.g. the Track Dependency)
        log.LogInformation("Service Bus Message Metadata -> requestId: " + traceparent);
        //message.UserProperties.Add("RequestId", traceparent);
        log.LogInformation("Service Bus Message Metadata -> traceparent: " + traceparent);
        //message.UserProperties.Add("traceparent", traceparent);
 
        // Traceoperation {common operation id} (e.g. same operation id for all requests in this telemetry pipeline)
        log.LogInformation("Service Bus Message Metadata -> traceoperation: " + traceoperation);
        //message.UserProperties.Add("traceoperation", traceoperation);        

       // queueClient.SendAsync(message);
   
        requestTelemetry.ResponseCode = "200";
        requestTelemetry.Success = true;
        telemetryClient.StopOperation(requestBlock);
    }
    telemetryClient.Flush();
}
