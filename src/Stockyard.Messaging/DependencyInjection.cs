using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Stockyard.Messaging.Consuming;
using Stockyard.Messaging.Outbox;

namespace Stockyard.Messaging;

public static class DependencyInjection
{
    /// <summary>Registers the message bus chosen by Messaging__Provider.</summary>
    public static IServiceCollection AddStockyardMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MessagingOptions>()
            .Bind(configuration.GetSection(MessagingOptions.SectionName))
            .Validate(
                o => o.UsesSqs || string.Equals(o.Provider, MessagingOptions.InMemoryProvider, StringComparison.OrdinalIgnoreCase),
                "Messaging__Provider must be Sqs or InMemory.")
            .Validate(o => !o.UsesSqs || !string.IsNullOrWhiteSpace(o.QueueUrl), "Messaging__QueueUrl is required for SQS.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        var provider = configuration[$"{MessagingOptions.SectionName}:{nameof(MessagingOptions.Provider)}"];
        if (string.Equals(provider, MessagingOptions.InMemoryProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.TryAddSingleton<IMessageBus, InMemoryMessageBus>();
            return services;
        }

        services.TryAddSingleton<IAmazonSQS>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MessagingOptions>>().Value;
            var config = new AmazonSQSConfig();
            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                config.ServiceURL = options.ServiceUrl;
            }

            // Credentials and region come from the SDK default chain (IRSA on EKS, env vars locally).
            return new AmazonSQSClient(config);
        });
        services.TryAddSingleton<IMessageBus, SqsMessageBus>();
        return services;
    }

    /// <summary>Runs the outbox relay as a hosted service.</summary>
    public static IServiceCollection AddOutboxRelay(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .Validate(o => o.BatchSize is > 0 and <= 500, "Outbox__BatchSize must be between 1 and 500.")
            .Validate(o => o.PollInterval > TimeSpan.Zero, "Outbox__PollInterval must be positive.")
            .ValidateOnStart();
        services.TryAddSingleton<OutboxRelay>();
        services.AddHostedService<OutboxRelayService>();
        return services;
    }

    /// <summary>Consumes the events queue with <typeparamref name="THandler"/> behind the guard row.</summary>
    public static IServiceCollection AddMessageConsumer<THandler>(this IServiceCollection services)
        where THandler : class, IMessageHandler
    {
        services.AddScoped<IMessageHandler, THandler>();
        services.TryAddSingleton<MessageConsumer>();
        services.AddHostedService<MessageConsumerService>();
        return services;
    }
}
