# Azure-App-Insights-Distrubuted-Tracing
How to use Application Insights to do distributed tracing through a Web App, REST API, Function App, Service Bus, Databricks and Data Factory.

# NOTE
# Please note this is a work in progress!

## Goal
To trace a call through a Web App, REST API, Azure Function (blob trigger event), Azure Function (service bus), Azure Data Factory, Azure Databricks and Spark Notebook.

### Sample trace (base upon current work)
![alt tag](https://raw.githubusercontent.com/AdamPaternostro/Azure-App-Insights-Distrubuted-Tracing/master/AppInsightsCompleteTrace.png)


### Here is the pipeline I want build to track telemetry between tiers:
![alt tag](https://raw.githubusercontent.com/AdamPaternostro/Azure-App-Insights-Distrubuted-Tracing/master/Architecture.png)


1.  A Web App (single page application – aka Angular)
    - The Web App will have App Insights JavaScript on each web page
    - The Web App will have the App Insights SDK
    - The Web App will have App Insights enabled in the Azure Portal under the web app

2. A REST API (.NET Controller)
    - The REST API App will have the App Insights SDK
    - The REST API App will have App Insights enabled in the Azure Portal under the web app
    - An HTML page delivered by the Web App will use JavaScript to call this REST API to upload/save a CSV to blob storage
    - The REST API will create a Request Telemetry (I’m assuming this is done automatically by App Insights)
        - Set the Telemetry Operation Id
            - QUESTION: Do we get an Operation Id from the Web App?  Or is this the starting Operation Id?  Since the Web App did not create a dependency, this seems like the start of the process.
        - Set the Telemetry Source (the web app’s app id)
            - NOTE: This will not be set if I decide this is the start of the calls (if this is the start then we never see the web app’s request).
        - Set the Telemetry Parent Id
            - QUESTION: Do we have a parent at this point?
    - The REST API will need to create a Track Dependency
        - Set the Target of the Dependency object to the a string that is "name | cid-v1:{GUID of the future app insights app id}"
        - Set the Telemetry Operation Id (assuming at this point it is the Request Telemetry’s Id)
        - Set the Telemetry Operation Parent Id to the Request Telemetry Id
    - The blob will be saved with the following meta-data
        - Request-Context: appId=cid “The App Id of the current App Insights Account”
          - NOTE: A "-" is not allowed.  Using RequestContext.
        - Request-Id / traceparent:  “The Id of the Track Dependency”
          - NOTE: A "-" is not allowed.  Using RequestId.
        - Traceoperation “The Operation Id of the Track Dependency”

3. An Azure Function detects the blob trigger
    - The function will have App Insights enabled in Azure
    - The Azure Function will create a Request Telemetry
        - Set the Telemetry Operation Id (Blob metadata: Traceoperation)
        - Set the Telemetry Source (Blob metadata: Request-Context)
        - Set the Telemetry Parent (Blob metadata: Request-Id / traceparent)
    - The Azure Function will need to create a Track Dependency
        - Set the Target of the Dependency object to the a string that is "name | cid-v1:{GUID of the future app insights app id}"
        - Set the Telemetry Operation Id (Blob metadata: Traceoperation)
        - Set the Telemetry Operation Parent Id to the Request Telemetry Id
    - The function will queue an item in Service Bus
    - The Service Bus JSON will have the following attributes
        - Filename: “The CSV filename”
        - Request-Context: appId=cid “The App Id of the current App Insights Account”
        - Request-Id / traceparent:  “The Id of the Track Dependency”
        - Traceoperation: from the blob (Blob metadata: Traceoperation)

4. An Azure Function monitors Service Bus
    - The function will have App Insights enabled in Azure
    - The Azure Function will create a Request Telemetry
        - Set the Telemetry Operation Id (Service Bus JSON payload: Traceoperation)
        - Set the Telemetry Source (Service Bus JSON payload: Request-Context)
        - Set the Telemetry Parent (Service Bus JSON payload: Request-Id / traceparent)
    - The Azure Function will need to create a Track Dependency
        - Set the Target of the Dependency object to the a string that is "name | cid-v1:{GUID of the future app insights app id}"
        - Set the Telemetry Operation Id (Service Bus JSON payload: Traceoperation)
        - Set the Telemetry Operation Parent Id to the Request Telemetry Id
    - The function will trigger an Azure Data Factory job via REST API
    - The Azure Data Factory will have the following parameters
        - Filename to be processed
        - Request-Context: appId=cid “The App Id of the current App Insights Account”
        - Request-Id / traceparent:  “The Id of the Track Dependency”
        - Traceoperation: (Service Bus JSON payload: Traceoperation)

5. The Azure Data Factory job will
    - Since Azure Data Factory does not support Application Insights an Azure Function will be created that handles the Application Insights calls
        - Create an Azure Function called App-Insights-Track-Request that returns a Request Id 
            - Parameters: iKey, App Id, string data (want to reuse this function for many ADF processes so pass the ikey and appid)
            - NOTE: This function will NOT have App Insights installed, we do not want to monitor this Azure Function we want it to write the monitoring on our behalf. What I really need is the Start a request, then later on call Stop request with a different call.  Can this be done?  This is not like .NET or code where we have a “using” block.
        - Create an Azure Function called App-Insights-Track-Dependency that returns a Dependency Id
            - Parameters: iKey, App Id, string data (want to reuse this function for many ADF processes so pass the ikey and appid)
 
    - Use an Azure Data Factory Web activity call the Azure Function: App-Insights-Track-Request
        - Set the Telemetry Operation Id (ADF Parameter: Traceoperation)
        - Set the Telemetry Source (ADF Parameter: Request-Context)
        - Set the Telemetry Parent (ADF Parameter: Request-Id / traceparent)

    - Use an Azure Data Factory Web activity call the Azure Function: App-Insights-Track-Dependency
        - Set the Telemetry Operation Id (ADF Parameter: Traceoperation)
        - Set the Telemetry Operation Parent Id to the Request Telemetry Id @string(activity('App-Insights-Track-Request').output))

    - Run a Databricks notebook
        - Prerequisite: install Application Insights on Databricks and attach to the cluster.
        - The Databricks Notebook will create a Request Telemetry
    - Set the Telemetry Operation Id (ADF Parameter: Traceoperation)
    - Set the Telemetry Source (Application Insights Account for the ADF <- hard code this or pass as a parameter from Azure Function)
    - Set the Telemetry Parent (@string(activity('App-Insights-Track-Dependency).output))
        - The notebook will process the files
        - The notebook will write a series of events to App Insights
        - The notebook will Stop Request Telemetry

    - Ideally I call “Stop Request” by calling an Azure Data Factory Web activity



## Azure Resource List
All resources in East US
Created in the below order

### Azure Storage
|  Storage |   | 
|---|---|
|  Blob |  00disttraceblob |

### Web Frontend         
|  MyWebSite |   | 
|---|---|
|  App Insights |  01-disttrace-web-app |
|  App Service |  01-disttrace-app-service |
|  Web App |  01-disttrace-web-app |

### REST API
|  MyRESTAPI |   | 
|---|---|
|  App Insights |  02-disttrace-web-app |
|  App Service |  02-disttrace-app-service |
|  Web App |  02-disttrace-web-app |

### Azure Function
|  Azure Function - Comsumption Plan - (Blob Trigger) |   | 
|---|---|
|  App Insights |  03-disttrace-func-blob |
|  Function |  03-disttrace-func-blob      (use 00disttraceblob storage account) |

### Azure Function
|  Azure Function (Service Bus) |   | 
|---|---|
|  App Insights |  04-disttrace-func-bus |
|  Function |  04-disttrace-func-bus       (use 00disttraceblob storage account)    |

### Azure Data Factory
|  Azure Data Factory |   | 
|---|---|
|  App Insights |  05-disttrace-app-insights   (type general) |
|  Data Factory |  05-disttrace-adf |
|  Function |  05-disttrace-func-helper    (use 00disttraceblob storage account | No App Insights)   |

### Databricks
|  Databricks |   | 
|---|---|
|  App Insights |  06-disttrace-app-insights   (type general) |
|  Workspace |  06-disttrace-databricks |

### Service Bus
|  Service Bus |   | 
|---|---|
|  Bus |  z07-disttrace-service-bus |
       

- dotnet new mvc --name MyWebSite
- cd MyWebSite
- dotnet restore
- dotnet build
- dotnet add package Microsoft.ApplicationInsights.AspNetCore --version 2.5.1
- dotnet build
- updated appsettings (Development) with the App Insights Key
- updated program.cs with .UseApplicationInsights()
- updated ViewImports and _Layout to add Javascript App Insights
- dotnet build

- dotnet new webapi --name MyRESTAPI
- cd MyWebSite
- dotnet restore
- dotnet build
- dotnet add package Microsoft.ApplicationInsights.AspNetCore --version 2.5.1
- dotnet add package WindowsAzure.Storage --version 9.3.3
- updated appsettings (Development) with the App Insights Key
- updated program.cs with .UseApplicationInsights()
- dotnet build
- dotnet run
- https://localhost:5001/api/values

- create a service bus queue in the portal (name: appinsightstest)
  - create a sas policy named ai_bus_key that has send and listen access
- create Azure function in portal (blob trigger) - paste in code 03-disttrace-func-blob.cs
  - add environment variables
  - ai_bus_key
  - ai_storage_key

### References
- https://github.com/Microsoft/ApplicationInsights-aspnetcore/wiki/Getting-Started-with-Application-Insights-for-ASP.NET-Core


### To do
- Environment Variables for everything (DONE)
- Creatd a file named SetEnvironmentVariables.sh so I can run locally
- Document the SetEnvironmentVariables.sh
- Service Bus: https://docs.microsoft.com/en-us/azure/azure-monitor/app/custom-operations-tracking
