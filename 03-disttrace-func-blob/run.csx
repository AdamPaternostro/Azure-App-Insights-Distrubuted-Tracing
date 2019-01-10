#r "Newtonsoft.Json"
#r "Microsoft.ApplicationInsights"
#r "Microsoft.WindowsAzure.Storage"
#r "System.Diagnostics.DiagnosticSource"
// #r "Microsoft.Azure.ServiceBus" // not needed for nuget

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.ApplicationInsights;  
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Caching.Redis;

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

    string RequestContext = blob.Metadata["RequestContext"];
    string RequestId = blob.Metadata["RequestId"];
    string traceparent = blob.Metadata["traceparent"];
    string traceoperation = blob.Metadata["traceoperation"];

    log.LogInformation($"Metadata RequestContext: {RequestContext}");
    log.LogInformation($"Metadata RequestId: {RequestId}");
    log.LogInformation($"Metadata traceparent: {traceparent}");
    log.LogInformation($"Metadata traceoperation: {traceoperation}");

    Microsoft.ApplicationInsights.TelemetryClient telemetryClient = new Microsoft.ApplicationInsights.TelemetryClient();
    telemetryClient.Context.User.AuthenticatedUserId = "adam.paternostro@microsoft.com";

    Microsoft.ApplicationInsights.DataContracts.RequestTelemetry requestTelemetry = new Microsoft.ApplicationInsights.DataContracts.RequestTelemetry();
    requestTelemetry.Source = RequestContext.Replace("appId=",string.Empty);
    requestTelemetry.Timestamp = System.DateTimeOffset.Now;
    requestTelemetry.Duration = System.TimeSpan.FromSeconds(1);
    requestTelemetry.ResponseCode = "200";
    requestTelemetry.Success = true;
    requestTelemetry.Name = "CreateServiceBus: " + name;
    requestTelemetry.Context.Operation.Id = traceoperation;
    requestTelemetry.Context.Operation.ParentId = traceparent;
    requestTelemetry.Context.User.AuthenticatedUserId = "adam.paternostro@microsoft.com";
    //string parentId = requestTelemetry.Telemetry.Id;
   // log.LogInformation($"New parentId: {parentId}");
    //telemetryClient.TrackRequest(requestTelemetry);
    //telemetryClient.Flush();


    using (var requestTelemetry02 = telemetryClient.StartOperation<Microsoft.ApplicationInsights.DataContracts.RequestTelemetry>(requestTelemetry))
    {        
        ///////////////////////////////////////////////////
        // Create Dependency for future Azure Function processing
        // NOTE: I trick it by giving a Start Time OFfset of Now.AddSeconds(1), so it sorts correctly in the Azure Portal UI
        ///////////////////////////////////////////////////
        string operationName = "Dependency: Service Bus Event";
        string target = "03-disttrace-func-blob | cid-v1:" + System.Environment.GetEnvironmentVariable("ai_04_disttrace_web_app_appkey");
        string dependencyName = "Dependency Name: Azure Function Service Bus";
        Microsoft.ApplicationInsights.DataContracts.DependencyTelemetry dependencyTelemetry =
            new Microsoft.ApplicationInsights.DataContracts.DependencyTelemetry(
            operationName, target, dependencyName,
            "03-disttrace-func-blob", System.DateTimeOffset.Now.AddSeconds(1), System.TimeSpan.FromSeconds(2), "200", true);
        dependencyTelemetry.Context.Operation.Id = traceoperation;
        dependencyTelemetry.Context.Operation.ParentId = requestTelemetry02.Telemetry.Id;
        // Store future parent id
        string parentId = dependencyTelemetry.Id;
        log.LogInformation($"New parentId dependencyTelemetry.Id: {parentId}");
        telemetryClient.TrackDependency(dependencyTelemetry);

        // do service bus work
        string ServiceBusConnectionString = System.Environment.GetEnvironmentVariable("ai_bus_key");;
        string QueueName = containerName;

        var queueClient = new Microsoft.Azure.ServiceBus.QueueClient(ServiceBusConnectionString, QueueName);
        byte[] m = System.Text.Encoding.ASCII.GetBytes(name);
        var message = new Microsoft.Azure.ServiceBus.Message(m);
        queueClient.SendAsync(message);
   
        telemetryClient.StopOperation(requestTelemetry02);
    }
    telemetryClient.Flush();
}