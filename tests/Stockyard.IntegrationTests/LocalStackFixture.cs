using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Testcontainers.LocalStack;

namespace Stockyard.IntegrationTests;

/// <summary>
/// LocalStack pinned to 4.14: later tags ask for an auth token. Each call to
/// <see cref="CreateQueuesAsync"/> makes a fresh queue pair with the production redrive policy.
/// </summary>
public sealed class LocalStackFixture : IAsyncLifetime
{
    public const string Image = "localstack/localstack:4.14";

    private readonly LocalStackContainer container = new LocalStackBuilder(Image).Build();

    public string ServiceUrl => container.GetConnectionString();

    public static BasicAWSCredentials Credentials { get; } = new("test", "test");

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public ValueTask DisposeAsync() => container.DisposeAsync();

    public AmazonSQSClient CreateSqsClient() =>
        new(Credentials, new AmazonSQSConfig { ServiceURL = ServiceUrl, AuthenticationRegion = "us-east-1" });

    public async Task<(string QueueUrl, string DlqUrl)> CreateQueuesAsync(string name)
    {
        using var sqs = CreateSqsClient();
        var dlq = await sqs.CreateQueueAsync(name + "-dlq");
        var dlqArn = (await sqs.GetQueueAttributesAsync(dlq.QueueUrl, ["QueueArn"])).QueueARN;
        var queue = await sqs.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = name,
            Attributes = new Dictionary<string, string>
            {
                ["RedrivePolicy"] = $$"""{"deadLetterTargetArn":"{{dlqArn}}","maxReceiveCount":"5"}""",
                ["VisibilityTimeout"] = "30",
            },
        });
        return (queue.QueueUrl, dlq.QueueUrl);
    }
}
