#r "Newtonsoft.Json"
#r "Microsoft.ApplicationInsights"
#r "Microsoft.WindowsAzure.Storage"
#r "System.Diagnostics.DiagnosticSource"
// #r "Microsoft.Azure.ServiceBus" // not needed for nuget

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.ApplicationInsights;  
using Microsoft.Azure.ServiceBus;


public static void Run(Stream myBlob, string name, ILogger log)
{
    log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");

    string containerName = "appinsightstest";
    string storageConnectionString = System.Environment.GetEnvironmentVariable("ai_storage_key");
    CloudStorageAccount storageAccount = null;

    CloudStorageAccount.TryParse(storageConnectionString, out storageAccount);

    Microsoft.WindowsAzure.Storage.Blob.CloudBlobClient blobStorage = storageAccount.CreateCloudBlobClient();
    blobStorage.DefaultRequestOptions.RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.LinearRetry(System.TimeSpan.FromSeconds(1), 10);
    Microsoft.WindowsAzure.Storage.Blob.CloudBlobContainer container = blobStorage.GetContainerReference(containerName);
    Microsoft.WindowsAzure.Storage.Blob.CloudBlockBlob blob = container.GetBlockBlobReference(name);

    blob.FetchAttributesAsync().Wait();

    string requestContext = blob.Metadata["RequestContext"];
    string requestId = blob.Metadata["RequestId"];
    string traceparent = blob.Metadata["traceparent"];
    string traceoperation = blob.Metadata["traceoperation"];

    log.LogInformation($"Metadata requestContext: {requestContext}");
    log.LogInformation($"Metadata requestId: {requestId}");
    log.LogInformation($"Metadata traceparent: {traceparent}");
    log.LogInformation($"Metadata traceoperation: {traceoperation}");

    Microsoft.ApplicationInsights.TelemetryClient telemetryClient = new Microsoft.ApplicationInsights.TelemetryClient();
    telemetryClient.Context.User.AuthenticatedUserId = "adam.paternostro@microsoft.com";

    Microsoft.ApplicationInsights.DataContracts.RequestTelemetry requestTelemetry = new Microsoft.ApplicationInsights.DataContracts.RequestTelemetry();
    requestTelemetry.Name = "Process Blob Event: " + name;
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
        string operationName = "Dependency: Service Bus Event";
        string target = "04-disttrace-func-bus | cid-v1:" + System.Environment.GetEnvironmentVariable("ai_04_disttrace_web_app_appkey");
        string dependencyName = "Dependency Name: Azure Function Service Bus";
        Microsoft.ApplicationInsights.DataContracts.DependencyTelemetry dependencyTelemetry =
            new Microsoft.ApplicationInsights.DataContracts.DependencyTelemetry(
            operationName, target, dependencyName,
            "03-disttrace-func-blob", System.DateTimeOffset.Now.AddSeconds(1), System.TimeSpan.FromSeconds(2), "200", true);
        dependencyTelemetry.Context.Operation.Id = traceoperation;
        dependencyTelemetry.Context.Operation.ParentId = requestBlock.Telemetry.Id;
        // Store future parent id
        traceparent = dependencyTelemetry.Id;
        log.LogInformation($"New traceparent dependencyTelemetry.Id: {traceparent}");
        telemetryClient.TrackDependency(dependencyTelemetry);

        ///////////////////////////////////////////////////
        // Service Bus Message code
        ///////////////////////////////////////////////////
        string serviceBusConnectionString = System.Environment.GetEnvironmentVariable("ai_bus_key");;
        string queueName = containerName;
        var queueClient = new Microsoft.Azure.ServiceBus.QueueClient(serviceBusConnectionString, queueName);
        byte[] payload = System.Text.Encoding.ASCII.GetBytes(name);        
        var message = new Microsoft.Azure.ServiceBus.Message(payload);
        
        ///////////////////////////////////////////////////
        // Set the servive bus message's meta data
        // We need the values from the dependency
        ///////////////////////////////////////////////////
        // Request-Context: appId=cid-v1:{The App Id of the current App Insights Account}
        requestContext = "appId=cid-v1:" + System.Environment.GetEnvironmentVariable("ai_03_disttrace_web_app_appkey");
        log.LogInformation("Service Bus Message Metadata -> RequestContext:" + requestContext);
        message.UserProperties.Add("RequestContext", requestContext);

        // Request-Id / traceparent: {parent request/operation id} (e.g. the Track Dependency)
        log.LogInformation("Service Bus Message Metadata -> requestId: " + traceparent);
        message.UserProperties.Add("RequestId", traceparent);
        log.LogInformation("Service Bus Message Metadata -> traceparent: " + traceparent);
        message.UserProperties.Add("traceparent", traceparent);
 
        // Traceoperation {common operation id} (e.g. same operation id for all requests in this telemetry pipeline)
        log.LogInformation("Service Bus Message Metadata -> traceoperation: " + traceoperation);
        message.UserProperties.Add("traceoperation", traceoperation);        

        queueClient.SendAsync(message);
   
        requestTelemetry.ResponseCode = "200";
        requestTelemetry.Success = true;
        telemetryClient.StopOperation(requestBlock);
    }
    telemetryClient.Flush();
}