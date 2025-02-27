# Functions Orchestrator

## Overview

This project contains an Azure Function that processes Azure Event Grid events and sends data to an HTTP endpoint then onto Azure Event Hub.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- [Docker](https://www.docker.com/get-started)
- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)

## Setting Up Environment Variables

### Using Azure Portal

1. Navigate to your Azure Function App in the Azure Portal.
2. Go to the "Environment variables" section under "Settings".
3. Click on "+ Add".
4. Enter `SimulatorUrlPrefix` as the name and http://\<container-apps-name-for-httpserver\>/ as the value. Click "Apply"
5. Enter `SimulationId` along with the uuid of the simulation. Click "Apply".
6. Enter `SimulationSchema` along with the JSON for the schema. Click "Apply".
7. Click "Apply" to apply all the changes.

For more detailed instructions, you can refer to the official documentation: [Configure app settings for Azure Functions.](https://learn.microsoft.com/en-us/azure/azure-functions/functions-how-to-use-azure-function-app-settings?tabs=azure-portal%2Cto-premium)

### Local Development

For local development, you can set environment variables in the `local.settings.json` file. Here is an example of how to set the `SimulatorUrlPrefix` environment variable:

1. Open the `local.settings.json` file.
2. Add the `SimulatorUrlPrefix` environment variable under the `Values` section.
3. Enter the URL of the Azure Container Apps app. 
4. Save the file.

```json
{
    "IsEncrypted": false,
    "Values": {
        "AzureWebJobsStorage": "UseDevelopmentStorage=true",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
        "SimulatorUrlPrefix": "http://<container-apps-name-for-httpserver>"
    }
}
```

