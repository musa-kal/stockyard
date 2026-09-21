using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stockyard.IntegrationTests.Api;

/// <summary>Starts one api per test class and offers small helpers for the common calls.</summary>
public abstract class ApiTestBase(SqlServerFixture sql) : IAsyncLifetime
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private ApiFactory? factory;

    protected HttpClient Client { get; private set; } = null!;

    protected SqlServerFixture Sql => sql;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(sql);
        Client = factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    protected async Task<JsonElement> PostJsonAsync(string url, object body, HttpStatusCode expected = HttpStatusCode.Created)
    {
        using var response = await Client.PostAsJsonAsync(url, body, Json, Ct);
        var content = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, content);
        return content.Length == 0 ? default : JsonDocument.Parse(content).RootElement.Clone();
    }

    protected async Task<JsonElement> GetJsonAsync(string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await Client.GetAsync(url, Ct);
        var content = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, content);
        return JsonDocument.Parse(content).RootElement.Clone();
    }

    /// <summary>Creates a supplier, a job site and two items; returns their codes.</summary>
    protected async Task<(string Supplier, string Site, string ItemA, string ItemB)> MasterDataAsync(string? webhookUrl = null)
    {
        var supplier = TestData.UniqueCode("S");
        var site = TestData.UniqueCode("J");
        var itemA = TestData.UniqueCode("ELBOW-");
        var itemB = TestData.UniqueCode("VALVE-");

        await PostJsonAsync("/api/v1/suppliers", new { code = supplier.ToLowerInvariant(), name = "Keel and Co. Fittings", webhookUrl });
        await PostJsonAsync("/api/v1/job-sites", new { code = site, name = "Harbour Street Tower" });
        await PostJsonAsync("/api/v1/items", new { sku = itemA, description = "Copper elbow 22 mm", unitOfMeasure = "EA", unitCost = 3.45m, reorderPoint = 50 });
        await PostJsonAsync("/api/v1/items", new { sku = itemB, description = "Ball valve 25 mm", unitOfMeasure = "EA", unitCost = 18.75m, reorderPoint = 5 });
        return (supplier, site, itemA, itemB);
    }

    /// <summary>A draft order with one line per item: 10 of the first, 4 of the second.</summary>
    protected async Task<Guid> DraftOrderAsync(string supplier, string site, string itemA, string itemB)
    {
        var order = await PostJsonAsync("/api/v1/purchase-orders", new { supplierCode = supplier, jobSiteCode = site });
        var id = order.GetProperty("id").GetGuid();
        await PostJsonAsync($"/api/v1/purchase-orders/{id}/lines", new { itemSku = itemA, quantityOrdered = 10 });
        await PostJsonAsync($"/api/v1/purchase-orders/{id}/lines", new { itemSku = itemB, quantityOrdered = 4, unitCost = 17.5m });
        return id;
    }

    protected async Task<HttpResponseMessage> PostOrderAsync(Guid id, string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/purchase-orders/{id}/post");
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await Client.SendAsync(request, Ct);
    }
}
