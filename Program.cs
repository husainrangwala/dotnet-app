using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NewRelic.Api.Agent;

class Program
{
    private static readonly HttpClient client = new HttpClient();
    private static IConfiguration? configuration;
    private static string? ENDPOINT_URL;
    private static int INTERVAL_SECONDS;
    private static int TIMEOUT_SECONDS;

    static async Task Main(string[] args)
    {
        // Load configuration
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables();

        configuration = builder.Build();

        var LICENSE_KEY = configuration.GetValue<string>("NewRelic:LicenseKey");
        var APP_NAME = configuration.GetValue<string>("NewRelic:AppName");

        try
        {
            var agent = NewRelic.Api.Agent.NewRelic.GetAgent();
            if (agent != null)
            {
                Console.WriteLine($"   ✓✓✓ AGENT ATTACHED ✓✓✓");
                Console.WriteLine($"   Agent Type: {agent.GetType().FullName}");

                // Try to record a test metric
                try
                {
                    NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/Startup/DiagnosticTest", 1);
                    Console.WriteLine($"   ✓ Test metric recorded successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️  Could not record test metric: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"   ✗✗✗ AGENT NOT ATTACHED ✗✗✗");
                Console.WriteLine($"   GetAgent() returned null - Profiler may not be loaded");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ✗ ERROR accessing agent: {ex.Message}");
            Console.WriteLine($"   {ex.StackTrace}");
        }

        Console.WriteLine("LICENSE_KEY: " + LICENSE_KEY);
        Console.WriteLine("APP_NAME: " + APP_NAME);

        // Read settings from configuration, with environment variable overrides
        ENDPOINT_URL = Environment.GetEnvironmentVariable("ENDPOINT_URL")
            ?? configuration["ApplicationSettings:EndpointUrl"]
            ?? "https://httpbin.org/delay/1";

        INTERVAL_SECONDS = int.TryParse(
            Environment.GetEnvironmentVariable("INTERVAL_SECONDS")
            ?? configuration["ApplicationSettings:IntervalSeconds"],
            out var interval) ? interval : 30;

        TIMEOUT_SECONDS = int.TryParse(
            configuration["ApplicationSettings:TimeoutSeconds"],
            out var timeout) ? timeout : 10;

        Console.WriteLine($"Starting periodic endpoint caller");
        Console.WriteLine($"Endpoint: {ENDPOINT_URL}");
        Console.WriteLine($"Interval: {INTERVAL_SECONDS} seconds");
        Console.WriteLine($"Timeout: {TIMEOUT_SECONDS} seconds");
        Console.WriteLine("Press Ctrl+C to stop\n");

        // Configure HTTP client timeout
        client.Timeout = TimeSpan.FromSeconds(TIMEOUT_SECONDS);

        using (var cts = new CancellationTokenSource())
        {
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await CallEndpointPeriodically(cts.Token);
        }

        Console.WriteLine("Application stopped.");
    }

    static async Task CallEndpointPeriodically(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var timestamp = DateTime.UtcNow.ToString("o");
                Console.WriteLine($"[{timestamp}] Calling endpoint...");

                var response = await client.GetAsync(ENDPOINT_URL, cancellationToken);

                if (response.IsSuccessStatusCode)
                {

                    // Record custom metrics for New Relic
                    NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/EndpointCall/Success", 1);
                    NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/EndpointCall/StatusCode", (int)response.StatusCode);
                }
                else
                {

                    // Record failure metrics
                    NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/EndpointCall/Failure", 1);
                    NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/EndpointCall/StatusCode", (int)response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:o}] Request failed: {ex.Message}");

                // Record request exception metric
                NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/EndpointCall/Exception", 1);
                NewRelic.Api.Agent.NewRelic.NoticeError(ex);
            }
            catch (TaskCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.UtcNow:o}] Unexpected error: {ex.Message}");

                // Record unexpected error metric
                NewRelic.Api.Agent.NewRelic.RecordMetric("Custom/EndpointCall/UnexpectedError", 1);
                NewRelic.Api.Agent.NewRelic.NoticeError(ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(INTERVAL_SECONDS), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
