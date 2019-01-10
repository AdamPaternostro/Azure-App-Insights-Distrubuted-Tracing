using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.ApplicationInsights;

namespace MyRESTAPI
{
    public class GenerateBlob
    {
        // Create a CSV file and save to Blob storage with the Headers required for our Azure Function processing
        // A new request telemetry is created
        // The request is part of the parent request (since)
        public void CreateBlob(string fileName)
        {
            ///////////////////////////////////////////////////
            // Grab existing 
            ///////////////////////////////////////////////////
            Microsoft.ApplicationInsights.TelemetryClient telemetryClient = new Microsoft.ApplicationInsights.TelemetryClient();
            telemetryClient.Context.User.AuthenticatedUserId = "adam.paternostro@microsoft.com";

            string commonOperationId = null;
            string parentId = null;
            System.Console.WriteLine("telemetryClient.Context.Operation.Id: " + telemetryClient.Context.Operation.Id);
            System.Console.WriteLine("telemetryClient.Context.Session.Id: " + telemetryClient.Context.Session.Id);
            System.Console.WriteLine("telemetryClient.Context.Operation.ParentId: " + telemetryClient.Context.Operation.ParentId);


            using (var requestTelemetry = telemetryClient.StartOperation<Microsoft.ApplicationInsights.DataContracts.RequestTelemetry>
                 ("CreateBlob: " + fileName))
            {
                ///////////////////////////////////////////////////
                // Request Telemetry
                ///////////////////////////////////////////////////
                requestTelemetry.Telemetry.Context.User.AuthenticatedUserId = "adam.paternostro@microsoft.com";

                if (!string.IsNullOrWhiteSpace(commonOperationId))
                {
                    // Use the existing common operation id
                    requestTelemetry.Telemetry.Context.Operation.Id = commonOperationId;
                    System.Console.WriteLine("[Use existing] commonOperationId: " + commonOperationId);
                }
                else
                {
                    // Set the commonOperationId (we did not know it until now)
                    commonOperationId = requestTelemetry.Telemetry.Context.Operation.Id;
                    System.Console.WriteLine("[Set the] commonOperationId = requestTelemetry.Telemetry.Context.Operation.Id: " + commonOperationId);
                }

                if (!string.IsNullOrWhiteSpace(parentId))
                {
                    // Use the existing parentId
                    requestTelemetry.Telemetry.Context.Operation.ParentId = parentId;
                    System.Console.WriteLine("[Use existing] parentId: " + parentId);
                }
                else
                {
                    parentId = requestTelemetry.Telemetry.Id;
                    System.Console.WriteLine("[Set the] parentId = requestTelemetry.Telemetry.Id: " + parentId);
                }
                // Store future parent id
                parentId = requestTelemetry.Telemetry.Id;
                System.Console.WriteLine("parentId = requestTelemetry.Telemetry.Id: " + parentId);



                ///////////////////////////////////////////////////
                // Create Dependency for future Azure Function processing
                // NOTE: I trick it by giving a Start Time OFfset of Now.AddSeconds(1), so it sorts correctly in the Azure Portal UI
                ///////////////////////////////////////////////////
                string operationName = "Dependency: Blob Event";
                string target = "03-disttrace-func-blob";
                string dependencyName = "Dependency Name: Azure Function Blob Trigger";
                Microsoft.ApplicationInsights.DataContracts.DependencyTelemetry dependencyTelemetry =
                   new Microsoft.ApplicationInsights.DataContracts.DependencyTelemetry(
                    operationName, target, dependencyName,
                    "02-disttrace-web-app", System.DateTimeOffset.Now.AddSeconds(1), System.TimeSpan.FromSeconds(2), "200", true);
                dependencyTelemetry.Context.Operation.Id = commonOperationId;
                dependencyTelemetry.Context.Operation.ParentId = requestTelemetry.Telemetry.Id;
                // Store future parent id
                parentId = dependencyTelemetry.Id;
                System.Console.WriteLine("parentId = dependencyTelemetry.Id: " + parentId);
               telemetryClient.TrackDependency(dependencyTelemetry);



                ///////////////////////////////////////////////////
                // Blob code
                ///////////////////////////////////////////////////
                string containerName = "appinsightstest";
                string storageConnectionString = System.Environment.GetEnvironmentVariable("ai_storage_key");
                CloudStorageAccount storageAccount = null;
                System.Console.WriteLine("storageConnectionString: " + storageConnectionString);

                CloudStorageAccount.TryParse(storageConnectionString, out storageAccount);

                System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();

                list.Add("id,date");

                for (int i = 1; i <= 50000; i++)
                {
                    list.Add(i.ToString() + "," + string.Format("{0:MM/dd/yyyy}", System.DateTime.Now));
                }

                var text = string.Join("\n", list.ToArray());

                Microsoft.WindowsAzure.Storage.Blob.CloudBlobClient blobStorage = storageAccount.CreateCloudBlobClient();
                blobStorage.DefaultRequestOptions.RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.LinearRetry(System.TimeSpan.FromSeconds(1), 10);
                Microsoft.WindowsAzure.Storage.Blob.CloudBlobContainer container = blobStorage.GetContainerReference(containerName);
                container.CreateIfNotExistsAsync().Wait();

                Microsoft.WindowsAzure.Storage.Blob.CloudBlockBlob blob = container.GetBlockBlobReference(fileName);

                ///////////////////////////////////////////////////
                // Set the blob's meta data
                // We need the values from the dependency
                ///////////////////////////////////////////////////
                // Request-Context: appId=cid “The App Id of the current App Insights Account”
                System.Console.WriteLine("ai_02_disttrace_web_app_appkey: appId=cid-v1:" + System.Environment.GetEnvironmentVariable("ai_02_disttrace_web_app_appkey"));
                blob.Metadata.Add("RequestContext", "appId=cid-v1:" + System.Environment.GetEnvironmentVariable("ai_02_disttrace_web_app_appkey"));

                // Request-Id / traceparent: “The Id of the Track Dependency”
                System.Console.WriteLine("RequestId: " + parentId);
                blob.Metadata.Add("RequestId", parentId);
                System.Console.WriteLine("traceparent: " + parentId);
                blob.Metadata.Add("traceparent", parentId);

                // Traceoperation “The Operation Id of the Track Dependency”
                System.Console.WriteLine("commonOperationId: " + commonOperationId);
                blob.Metadata.Add("traceoperation", commonOperationId);


                using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)))
                {
                    blob.UploadFromStreamAsync(memoryStream).Wait();
                }

                telemetryClient.StopOperation(requestTelemetry);
            } // using

            ///////////////////////////////////////////////////
            // For Debugging
            ///////////////////////////////////////////////////
            telemetryClient.Flush();

        } // Create Blob
    } // class
} // namespace