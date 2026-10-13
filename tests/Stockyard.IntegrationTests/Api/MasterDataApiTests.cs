using System.Net;

namespace Stockyard.IntegrationTests.Api;

public sealed class MasterDataApiTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    [Fact]
    public async Task CreatingASupplierReturnsCreatedWithLocationAndUpperCasedCode()
    {
        var code = TestData.UniqueCode("ngps").ToLowerInvariant();

        using var response = await Client.PostAsync(
            "/api/v1/suppliers",
            System.Net.Http.Json.JsonContent.Create(new { code, name = "Northgate Pipe Supply", webhookUrl = "http://fake-supplier:8090/webhooks" }),
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await GetJsonAsync(response.Headers.Location!.ToString());
        body.GetProperty("code").GetString().ShouldBe(code.ToUpperInvariant());
        body.GetProperty("webhookUrl").GetString().ShouldBe("http://fake-supplier:8090/webhooks");
    }

    [Fact]
    public async Task ADuplicateCodeIsAConflictProblem()
    {
        var code = TestData.UniqueCode("HLV");
        await PostJsonAsync("/api/v1/suppliers", new { code, name = "Harbourline Valves" });

        var problem = await PostJsonAsync("/api/v1/suppliers", new { code, name = "Harbourline Valves" }, HttpStatusCode.Conflict);

        problem.GetProperty("status").GetInt32().ShouldBe(409);
        problem.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TheWarehouseCodeIsRefusedForNewJobSites()
    {
        var problem = await PostJsonAsync("/api/v1/job-sites", new { code = "warehouse", name = "Second warehouse" }, HttpStatusCode.BadRequest);

        problem.GetProperty("errorNumber").GetInt32().ShouldBe(50000);
        problem.GetProperty("detail").GetString()!.ShouldContain("WAREHOUSE");
    }

    [Fact]
    public async Task InvalidItemsAreBadRequests()
    {
        await PostJsonAsync("/api/v1/items", new { sku = "", description = "No sku", unitOfMeasure = "EA", unitCost = 1, reorderPoint = 0 }, HttpStatusCode.BadRequest);
        await PostJsonAsync("/api/v1/items", new { sku = TestData.UniqueCode("NEG"), description = "Negative", unitOfMeasure = "EA", unitCost = -1, reorderPoint = 0 }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AMalformedBodyIsAProblemDetailsBadRequest()
    {
        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync("/api/v1/items", content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task UnknownIdsAreNotFound()
    {
        using var response = await Client.GetAsync($"/api/v1/items/{Guid.NewGuid()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HealthEndpointsAndTheOpenApiDocumentRespond()
    {
        (await Client.GetAsync("/healthz", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.GetAsync("/readyz", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var document = await GetJsonAsync("/openapi/v1.json");
        document.GetProperty("paths").TryGetProperty("/api/v1/purchase-orders/{id}/post", out _).ShouldBeTrue();
    }
}
