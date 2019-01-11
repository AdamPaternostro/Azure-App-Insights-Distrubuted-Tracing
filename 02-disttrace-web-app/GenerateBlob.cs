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

            string traceoperation = null;
            string traceparent = null;
            System.Console.WriteLine("telemetryClient.Context.Operation.Id: " + telemetryClient.Context.Operation.Id);
            System.Console.WriteLine("telemetryClient.Context.Session.Id: " + telemetryClient.Context.Session.Id);
            System.Console.WriteLine("telemetryClient.Context.Operation.ParentId: " + telemetryClient.Context.Operation.ParentId);

            Microsoft.ApplicationInsights.DataContracts.RequestTelemetry requestTelemetry = new Microsoft.ApplicationInsights.DataContracts.RequestTelemetry();
            requestTelemetry.Name = "Create Blob: " + fileName;
            //requestTelemetry.Source = requestContext.Replace("appId=",string.Empty);
            requestTelemetry.Timestamp = System.DateTimeOffset.Now;
            requestTelemetry.Context.Operation.Id = traceoperation;
            requestTelemetry.Context.Operation.ParentId = traceparent;
            requestTelemetry.Context.User.AuthenticatedUserId = "adam.paternostro@microsoft.com";

            using (var requestBlock = telemetryClient.StartOperation<Microsoft.ApplicationInsights.DataContracts.RequestTelemetry>(requestTelemetry))
            {
                ///////////////////////////////////////////////////
                // Request Telemetry
                ///////////////////////////////////////////////////
                requestBlock.Telemetry.Context.User.AuthenticatedUserId = "adam.paternostro@microsoft.com";

                if (!string.IsNullOrWhiteSpace(traceoperation))
                {
                    // Use the existing common operation id
                    requestBlock.Telemetry.Context.Operation.Id = traceoperation;
                    System.Console.WriteLine("[Use existing] traceoperation: " + traceoperation);
                }
                else
                {
                    // Set the traceoperation (we did not know it until now)
                    traceoperation = requestBlock.Telemetry.Context.Operation.Id;
                    System.Console.WriteLine("[Set the] traceoperation = requestBlock.Telemetry.Context.Operation.Id: " + traceoperation);
                }

                if (!string.IsNullOrWhiteSpace(traceparent))
                {
                    // Use the existing traceparent
                    requestBlock.Telemetry.Context.Operation.ParentId = traceparent;
                    System.Console.WriteLine("[Use existing] traceparent: " + traceparent);
                }
                else
                {
                    traceparent = requestBlock.Telemetry.Id;
                    System.Console.WriteLine("[Set the] traceparent = requestBlock.Telemetry.Id: " + traceparent);
                }
                // Store future parent id
                traceparent = requestBlock.Telemetry.Id;
                System.Console.WriteLine("traceparent = requestBlock.Telemetry.Id: " + traceparent);



                ///////////////////////////////////////////////////
                // Create Dependency for future Azure Function processing
                // NOTE: I trick it by giving a Start Time Offset of Now.AddSeconds(1), so it sorts correctly in the Azure Portal UI
                ///////////////////////////////////////////////////
                string operationName = "Dependency: Blob Event";
                // Set the target so it points to the "dependent" app insights account app id
                // string target = "03-disttrace-func-blob | cid-v1:676560d0-81fb-4e5b-bfdd-7da1ad11c866"
                string target = "03-disttrace-func-blob | cid-v1:" + System.Environment.GetEnvironmentVariable("ai_03_disttrace_web_app_appkey");
                string dependencyName = "Dependency Name: Azure Function Blob Trigger";
                Microsoft.ApplicationInsights.DataContracts.DependencyTelemetry dependencyTelemetry =
                   new Microsoft.ApplicationInsights.DataContracts.DependencyTelemetry(
                    operationName, target, dependencyName,
                    "02-disttrace-web-app", System.DateTimeOffset.Now.AddSeconds(1), System.TimeSpan.FromSeconds(2), "200", true);
                dependencyTelemetry.Context.Operation.Id = traceoperation;
                dependencyTelemetry.Context.Operation.ParentId = requestBlock.Telemetry.Id;
                // Store future parent id
                traceparent = dependencyTelemetry.Id;
                System.Console.WriteLine("traceparent = dependencyTelemetry.Id: " + traceparent);
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
                // Request-Context: appId=cid-v1:{The App Id of the current App Insights Account}
                string requestContext = "appId=cid-v1:" + System.Environment.GetEnvironmentVariable("ai_02_disttrace_web_app_appkey");
                System.Console.WriteLine("Blob Metadata -> requestContext: " + requestContext);
                blob.Metadata.Add("RequestContext", requestContext);

                // Request-Id / traceparent: {parent request/operation id} (e.g. the Track Dependency)
                System.Console.WriteLine("Blob Metadata -> RequestId: " + traceparent);
                blob.Metadata.Add("RequestId", traceparent);
                System.Console.WriteLine("Blob Metadata -> traceparent: " + traceparent);
                blob.Metadata.Add("traceparent", traceparent);

                // Traceoperation {common operation id} (e.g. same operation id for all requests in this telemetry pipeline)
                System.Console.WriteLine("Blob Metadata -> traceoperation: " + traceoperation);
                blob.Metadata.Add("traceoperation", traceoperation);


                using (System.IO.MemoryStream memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)))
                {
                    blob.UploadFromStreamAsync(memoryStream).Wait();
                }

                requestTelemetry.ResponseCode = "200";
                requestTelemetry.Success = true;
                telemetryClient.StopOperation(requestBlock);
            } // using

            ///////////////////////////////////////////////////
            // For Debugging
            ///////////////////////////////////////////////////
            telemetryClient.Flush();

        } // Create Blob
    } // class
} // namespace