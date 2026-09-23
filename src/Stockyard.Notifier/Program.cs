using Stockyard.Domain.Webhooks;
using Stockyard.Infrastructure;
using Stockyard.Notifier;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStockyardDatabase(builder.Configuration);
builder.Services.AddOptions<NotifierOptions>()
    .Bind(builder.Configuration.GetSection(NotifierOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningSecret), "Notifier__SigningSecret is required.")
    .ValidateOnStart();

builder.Services.AddSingleton(WebhookRetryPolicy.Default);
builder.Services.AddScoped<WebhookDeliveryStore>();

await builder.Build().RunAsync();
