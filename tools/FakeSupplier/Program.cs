using System.Collections.Concurrent;
using System.Text.Json;

namespace FakeSupplier;

/// <summary>
/// Stands in for a supplier's webhook endpoint in docker compose: accepts POST /webhooks,
/// checks the signature with the shared SigningSecret, and lists what arrived at GET /received.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://+:8090");

        var secret = builder.Configuration["SigningSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("SigningSecret is required.");
        }

        builder.Services.AddSingleton(new SignatureVerifier(secret, TimeProvider.System));
        builder.Services.AddSingleton<ConcurrentQueue<ReceivedWebhook>>();

        var app = builder.Build();

        app.MapPost("/webhooks/{**rest}", async (HttpRequest request, SignatureVerifier verifier, ConcurrentQueue<ReceivedWebhook> received) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
            var valid = verifier.IsValid(request.Headers["X-Stockyard-Signature"], body);

            JsonElement? payload = null;
            try
            {
                payload = JsonDocument.Parse(body).RootElement.Clone();
            }
            catch (JsonException)
            {
                // Recorded as received; the payload is just not JSON.
            }

            received.Enqueue(new ReceivedWebhook(
                request.Headers["X-Stockyard-Delivery"].ToString(),
                request.Headers["X-Stockyard-Event"].ToString(),
                valid,
                DateTimeOffset.UtcNow,
                payload));

            return valid ? Results.Ok() : Results.Unauthorized();
        });

        app.MapGet("/received", (ConcurrentQueue<ReceivedWebhook> received) => received.ToArray());
        app.MapGet("/healthz", () => Results.Ok());

        await app.RunAsync();
    }
}

public sealed record ReceivedWebhook(string DeliveryId, string EventType, bool SignatureValid, DateTimeOffset ReceivedAt, JsonElement? Payload);
