using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Stockyard.Domain;
using Stockyard.Domain.Events;
using Stockyard.Domain.Webhooks;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Infrastructure.Procedures;
using Stockyard.Notifier;

namespace Stockyard.IntegrationTests.Notifier;

public sealed class WebhookDispatcherTests(SqlServerFixture sql)
{
    private const string Secret = "test-signing-secret";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider time = new(DateTimeOffset.UtcNow.AddSeconds(5));

    private readonly NotifierOptions settings = new() { SigningSecret = Secret, BatchSize = 50, Lease = TimeSpan.FromSeconds(60) };

    [Fact]
    public async Task ADueDeliveryIsSignedSentAndMarkedDelivered()
    {
        var supplier = await PostedOrderForNewSupplierAsync();
        var handler = new RecordingHandler(HttpStatusCode.OK);

        await DispatchUntilSettledAsync(handler, supplier);

        var request = handler.Requests.Single(r => r.Url.Host == supplier.Host);
        request.Event.ShouldBe(EventTypes.PurchaseOrderPosted);
        WebhookSignature.Verify(Secret, request.Signature, request.Body, time.GetUtcNow(), TimeSpan.FromMinutes(5)).ShouldBeTrue();

        var delivery = await DeliveryAsync(supplier.Id);
        request.DeliveryId.ShouldBe(delivery.Id.ToString());
        delivery.DeliveredAt.ShouldNotBeNull();
        delivery.Attempts.ShouldBe(1);
        delivery.LastStatusCode.ShouldBe(200);
        delivery.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public async Task AFailedDeliveryIsRetriedWithADoublingDelay()
    {
        var supplier = await PostedOrderForNewSupplierAsync();
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable);

        await DispatchUntilSettledAsync(handler, supplier);
        var first = await DeliveryAsync(supplier.Id);
        first.Attempts.ShouldBe(1);
        first.LastStatusCode.ShouldBe(503);
        first.DeliveredAt.ShouldBeNull();
        first.NextAttemptAt!.Value.ShouldBe(time.GetUtcNow().UtcDateTime.AddSeconds(1), TimeSpan.FromMilliseconds(5));

        time.Advance(TimeSpan.FromSeconds(1));
        await DispatchUntilSettledAsync(handler, supplier);

        var second = await DeliveryAsync(supplier.Id);
        second.Attempts.ShouldBe(2);
        second.NextAttemptAt!.Value.ShouldBe(time.GetUtcNow().UtcDateTime.AddSeconds(2), TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public async Task TheLastFailedAttemptAbandonsTheDelivery()
    {
        var supplier = await PostedOrderForNewSupplierAsync();
        await using (var db = sql.CreateDbContext())
        {
            await db.WebhookDeliveries.Where(w => w.SupplierId == supplier.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Attempts, WebhookRetryPolicy.Default.MaxAttempts - 1), Ct);
        }

        var handler = new RecordingHandler(HttpStatusCode.InternalServerError);
        await DispatchUntilSettledAsync(handler, supplier);

        var delivery = await DeliveryAsync(supplier.Id);
        delivery.Attempts.ShouldBe(WebhookRetryPolicy.Default.MaxAttempts);
        delivery.NextAttemptAt.ShouldBeNull();

        time.Advance(TimeSpan.FromHours(1));
        handler.Requests.Clear();
        await DispatchUntilSettledAsync(handler, supplier);
        handler.Requests.ShouldNotContain(r => r.Url.Host == supplier.Host);
    }

    [Fact]
    public async Task AClaimedDeliveryIsHiddenUntilItsLeaseRunsOut()
    {
        var supplier = await PostedOrderForNewSupplierAsync();
        var deliveryId = (await DeliveryAsync(supplier.Id)).Id;

        await using var db = sql.CreateDbContext();
        var store = new WebhookDeliveryStore(db, time, WebhookRetryPolicy.Default);
        (await ClaimAllAsync(store)).ShouldContain(c => c.Id == deliveryId);
        (await ClaimAllAsync(store)).ShouldNotContain(c => c.Id == deliveryId);

        time.Advance(settings.Lease + TimeSpan.FromSeconds(1));
        (await ClaimAllAsync(store)).ShouldContain(c => c.Id == deliveryId);
    }

    private async Task<List<ClaimedDelivery>> ClaimAllAsync(WebhookDeliveryStore store)
    {
        var all = new List<ClaimedDelivery>();
        IReadOnlyList<ClaimedDelivery> batch;
        do
        {
            batch = await store.ClaimDueAsync(settings.BatchSize, settings.Lease, Ct);
            all.AddRange(batch);
        }
        while (batch.Count == settings.BatchSize);

        return all;
    }

    /// <summary>Other tests leave due deliveries behind, so dispatch until nothing is due.</summary>
    private async Task DispatchUntilSettledAsync(RecordingHandler handler, (Guid Id, string Host) supplier)
    {
        await using var db = sql.CreateDbContext();
        using var client = new HttpClient(handler, disposeHandler: false);
        var options = Options.Create(settings);
        var dispatcher = new WebhookDispatcher(
            new WebhookDeliveryStore(db, time, WebhookRetryPolicy.Default),
            new WebhookSender(client, options, time),
            options,
            NullLogger<WebhookDispatcher>.Instance);

        while (await dispatcher.DispatchBatchAsync(Ct) > 0)
        {
        }
    }

    private async Task<(Guid Id, string Host)> PostedOrderForNewSupplierAsync()
    {
        await using var db = sql.CreateDbContext();
        var host = TestData.UniqueCode("supplier").ToLowerInvariant() + ".test";
        var supplier = await TestData.AddSupplierAsync(db, $"http://{host}/webhooks");
        var item = await TestData.AddItemAsync(db);
        var order = await TestData.AddDraftOrderAsync(db, supplier, JobSite.WarehouseId, (item, 2));
        await new StockyardProcedures(db).PostPurchaseOrderAsync(order.Id, "hook-" + order.Number, Ct);
        return (supplier.Id, host);
    }

    private async Task<WebhookDelivery> DeliveryAsync(Guid supplierId)
    {
        await using var db = sql.CreateDbContext();
        return await db.WebhookDeliveries.AsNoTracking().SingleAsync(w => w.SupplierId == supplierId, Ct);
    }

    private sealed record RecordedRequest(Uri Url, string Body, string? Signature, string? Event, string? DeliveryId);

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public ConcurrentBag<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!,
                await request.Content!.ReadAsStringAsync(cancellationToken),
                Header(request, WebhookSignature.HeaderName),
                Header(request, WebhookSender.EventHeader),
                Header(request, WebhookSender.DeliveryHeader)));
            return new HttpResponseMessage(status);
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }
}
