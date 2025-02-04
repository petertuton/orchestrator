// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Orchestrator
{
    public class Orchestrator
    {
        private readonly ILogger<Orchestrator> _logger;
        private static readonly HttpClient _httpClient = new HttpClient();

        public Orchestrator(ILogger<Orchestrator> logger)
        {
            _logger = logger;
        }

        [Function(nameof(Orchestrator))]
        [EventHubOutput("digital-twin", Connection = "EventHubConnectionAppSetting")]
        public async Task<string> Run([EventGridTrigger] CloudEvent cloudEvent)
        {
            // Log the event type and subject
            _logger.LogInformation("Event type: {type}, Event subject: {subject}", cloudEvent.Type, cloudEvent.Subject);

            // Serialize the CloudEvent to JSON
            var cloudEventJson = JsonSerializer.Serialize(cloudEvent);
            var cloudEventData = JsonDocument.Parse(cloudEventJson).RootElement.GetProperty("data").GetRawText();

            // Create the HTTP content
            var content = new StringContent(cloudEventData, Encoding.UTF8, "application/json");

            // Call an HTTP endpoint with POST request
            var response = await _httpClient.PostAsync(Environment.GetEnvironmentVariable("DigitalTwinUrl"), content);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();

            // Log the HTTP response
            _logger.LogInformation("HTTP response: {responseBody}", responseBody);

            // Return the message to be sent to Event Hub
            return $"{responseBody}";
        }
    }
}
