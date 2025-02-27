using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Extensions.Redis;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Net;
using Microsoft.Azure.Functions.Worker.Extensions.Abstractions;
using Azure;

namespace Orchestrator
{
    public class Orchestrator
    {
        private readonly ILogger<Orchestrator> _logger;
        private static readonly HttpClient _httpClient = new();

        public Orchestrator(ILogger<Orchestrator> logger)
        {
            _logger = logger;
        }

        [Function("SimulationLoopEventGrid")]
        [EventHubOutput("digital-twin", Connection = "EventHubConnectionString")]
        public async Task<string> SimulationLoopEventGrid(
            [EventGridTrigger] CloudEvent cloudEvent)
        {
            try
            {
                return await ProcessSimulationEvent(cloudEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Event Grid request");
                return string.Empty;
            }
        }

        [Function("SimulationLoopWebhook")]
        public async Task<HttpResponseData> SimulationLoopWebhook(
            [HttpTrigger(AuthorizationLevel.Function, "post", "options")] HttpRequestData req)
        {
            try
            {
                // Handle OPTIONS request for Event Grid subscription validation
                if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    var response = req.CreateResponse(HttpStatusCode.OK);
                    response.Headers.Add("WebHook-Allowed-Origin", "*");
                    response.Headers.Add("WebHook-Allowed-Rate", "*");
                    return response;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrEmpty(requestBody))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Request body is empty");
                }

                // Parse as CloudEvent for normal event processing
                var cloudEvent = CloudEvent.Parse(BinaryData.FromString(requestBody));
                if (cloudEvent == null)
                {
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Could not parse cloud event");
                }
                
                var results = await ProcessSimulationEvent(cloudEvent);
                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(results);
                return successResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook request");
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "Error processing request");
            }
        }

        private async Task<string> ProcessSimulationEvent(CloudEvent cloudEvent)
        {
            if (cloudEvent == null)
            {
                throw new ArgumentNullException(nameof(cloudEvent));
            }

            // Log the event type and subject
            _logger.LogInformation("Event type: {type}, Event subject: {subject}", cloudEvent.Type, cloudEvent.Subject);

            // Get the simulation ID and schema from the environment variable
            var simulationId = Environment.GetEnvironmentVariable("SimulationId") ?? 
                throw new InvalidOperationException("SimulationId cannot be null.");
            var simulationSchema = Environment.GetEnvironmentVariable("SimulationSchema") ?? 
                throw new InvalidOperationException("SimulationSchema cannot be null.");

            // Process the event
            var cloudEventData = cloudEvent.Data?.ToString() ?? 
                throw new InvalidOperationException("Event data cannot be null.");

            // Transform the event data to match the schema of the simulator
            var inputData = TransformEventData(cloudEventData, simulationSchema);
            
            // Send the simulation data to the simulator
            await this.InputSimulationData(simulationId, inputData);

            // Resume the simulation
            var resumeSimulationResponse = await this.ResumeSimulation(simulationId);

            // Keep checking the simulation status until is reports "IDLE"
            var simulationStatus = await this.GetSimulationStatus(simulationId);
            while (simulationStatus != "IDLE")
            {
                _logger.LogInformation("Simulation status: {simulationStatus}", simulationStatus);
                await Task.Delay(1000);
                simulationStatus = await this.GetSimulationStatus(simulationId);
            }

            // Get the results of the simulation
            return await this.GetSimulationResults(simulationId);
        }

        private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string message)
        {
            var response = req.CreateResponse(statusCode);
            await response.WriteAsJsonAsync(new { message = message });
            return response;
        }

        [Function("StartSimulation")]
        public async Task<HttpResponseData> StartSimulation(
            [HttpTrigger(AuthorizationLevel.Function, "put")] HttpRequestData req)
        {
            _logger.LogInformation("Processing Start Simulation request");

            // Get the simulation ID
            var simulationId = Environment.GetEnvironmentVariable("SimulationId") ?? throw new InvalidOperationException("Simulation ID cannot be null.");

            // Initiate the simulation
            var simulationInitiated = await this.InitiateSimulation(simulationId);
            if (!simulationInitiated)
            {
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // Return 200 OK with the simulation ID (Redis binding will handle storing the returned value)
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(simulationInitiated.ToString());
            return response;
        }

        [Function("StopSimulation")]
        public async Task<HttpResponseData> StopSimulation(
            [HttpTrigger(AuthorizationLevel.Function, "put")] HttpRequestData req)
        {
            _logger.LogInformation("Processing Stop Simulation request");

            // Get the simulation ID
            var simulationId = Environment.GetEnvironmentVariable("SimulationId") ?? throw new InvalidOperationException("Simulation ID cannot be null.");

            // Initiate the simulation
            var simulationStopped = await this.StopSimulation(simulationId);
            if (!simulationStopped)
            {
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // Return 200 OK with the simulation ID (Redis binding will handle storing the returned value)
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(simulationStopped.ToString());
            return response;
        }

///////// Helper functions

        private async Task<bool> InitiateSimulation(string simulationID)
        {
            // Call the HTTP endpoint
            var response = await _httpClient.PutAsync(Environment.GetEnvironmentVariable("SimulatorUrlPrefix") + "/simulations/" + simulationID + "/start", null);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();

            // Log the HTTP response
            _logger.LogInformation("/start response: {responseBody}", responseBody);

            // Return the result of the value of "result" in the response body
            var result = JsonDocument.Parse(responseBody).RootElement.GetProperty("result").GetBoolean();
            return result;
        }

        private async Task InputSimulationData(string simulationID, string inputData)
        {
            // Create the HTTP content
            var content = new StringContent(inputData, Encoding.UTF8, "application/json");

            // Call the HTTP endpoint
            var response = await _httpClient.PutAsync(Environment.GetEnvironmentVariable("SimulatorUrlPrefix") + "/simulations/" + simulationID + "/input/data", content);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();

            // Log the HTTP response
            _logger.LogInformation("/input/data response: {responseBody}", responseBody);
        }

        private static string TransformEventData(string eventData, string simulationSchema)
        {
            // This is a placeholder for the actual transformation logic
            return eventData;
        }

        private async Task<string> ResumeSimulation(string simulationID)
        {
            // Call the HTTP endpoint
            var response = await _httpClient.PutAsync(Environment.GetEnvironmentVariable("SimulatorUrlPrefix") + "/simulations/" + simulationID + "/resume", null);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();

            // Log the HTTP response
            _logger.LogInformation("/resume response: {responseBody}", responseBody);

            return responseBody;
        }

        private async Task<string> GetSimulationStatus(string simulationID)
        {
            // Call the HTTP endpoint
            var response = await _httpClient.GetAsync(Environment.GetEnvironmentVariable("SimulatorUrlPrefix") + "/simulations/" + simulationID + "/status");
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();

            // Log the HTTP response
            _logger.LogInformation("/status response: {responseBody}", responseBody);

            // Get the status from the response
            var simulationStatus = JsonDocument.Parse(responseBody).RootElement.GetProperty("status").GetString() ?? throw new InvalidOperationException("Simulation status cannot be null.");
            return simulationStatus;
        }

        private async Task<string> GetSimulationResults(string simulationID)
        {
            // Call the HTTP endpoint
            var response = await _httpClient.GetAsync(Environment.GetEnvironmentVariable("SimulatorUrlPrefix") + "/simulations/" + simulationID + "/results");
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();

            // Log the HTTP response
            _logger.LogInformation("/results response: {responseBody}", responseBody);

            return responseBody;
        }

        private async Task<bool> StopSimulation(string simulationID)
        {
            // Call the HTTP endpoint
            var response = await _httpClient.PutAsync(Environment.GetEnvironmentVariable("SimulatorUrlPrefix") + "/simulations/" + simulationID + "/stop", null);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();

            // Log the HTTP response
            _logger.LogInformation("/stop response: {responseBody}", responseBody);

            // Return the result of the value of "result" in the response body
            var result = JsonDocument.Parse(responseBody).RootElement.GetProperty("result").GetBoolean();
            return result;
        }
    }
}
