using Microsoft.Extensions.Options;
using Stockyard.Domain.Webhooks;
using Stockyard.Infrastructure;
using Stockyard.Notifier;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStockyardDatabase(builder.Configuration);
builder.Services.AddOptions<NotifierOptions>()
    .Bind(builder.Configuration.GetSection(NotifierOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningSecret), "Notifier__SigningSecret is required.")
    .Validate(o => o.BatchSize > 0 && o.PollInterval > TimeSpan.Zero && o.Lease > o.RequestTimeout, "Notifier timings are not consistent.")
    .ValidateOnStart();

builder.Services.AddSingleton(WebhookRetryPolicy.Default);
builder.Services.AddScoped<WebhookDeliveryStore>();
builder.Services.AddScoped<WebhookDispatcher>();
builder.Services.AddHttpClient<WebhookSender>((sp, client) =>
    client.Timeout = sp.GetRequiredService<IOptions<NotifierOptions>>().Value.RequestTimeout);
builder.Services.AddHostedService<NotifierService>();

await builder.Build().RunAsync();
