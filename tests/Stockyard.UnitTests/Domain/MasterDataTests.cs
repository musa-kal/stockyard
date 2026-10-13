using Stockyard.Domain;

namespace Stockyard.UnitTests.Domain;

public sealed class MasterDataTests
{
    [Fact]
    public void SupplierCreateNormalisesCodeAndTrimsName()
    {
        var supplier = Supplier.Create(" ngps ", "  Northgate Pipe Supply ", null);

        supplier.Code.ShouldBe("NGPS");
        supplier.Name.ShouldBe("Northgate Pipe Supply");
        supplier.WebhookUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData("ftp://harbourline.example/hooks")]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    public void SupplierCreateRejectsWebhookUrlsThatAreNotAbsoluteHttp(string url)
    {
        Should.Throw<DomainException>(() => Supplier.Create("HLV", "Harbourline Valves", url));
    }

    [Fact]
    public void SupplierCreateTreatsBlankWebhookUrlAsNone()
    {
        Supplier.Create("HLV", "Harbourline Valves", "   ").WebhookUrl.ShouldBeNull();
    }

    [Fact]
    public void SupplierCreateKeepsAValidWebhookUrl()
    {
        Supplier.Create("KCF", "Keel and Co. Fittings", "http://fake-supplier:8090/webhooks")
            .WebhookUrl.ShouldBe("http://fake-supplier:8090/webhooks");
    }

    [Fact]
    public void ItemCreateNormalisesSkuAndUnitOfMeasure()
    {
        var item = Item.Create("valve-bv-25", "Ball valve 25 mm", "ea", 18.75m, 4);

        item.Sku.ShouldBe("VALVE-BV-25");
        item.UnitOfMeasure.ShouldBe("EA");
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void ItemCreateRejectsNegativeCostOrReorderPoint(decimal unitCost, decimal reorderPoint)
    {
        Should.Throw<DomainException>(() => Item.Create("PIPE", "Pipe", "M", unitCost, reorderPoint));
    }

    [Fact]
    public void ItemCreateRejectsALongDescription()
    {
        Should.Throw<DomainException>(() => Item.Create("PIPE", new string('x', 201), "M", 1, 0));
    }

    [Theory]
    [InlineData("warehouse")]
    [InlineData("WAREHOUSE")]
    public void JobSiteCreateRefusesTheWarehouseCode(string code)
    {
        Should.Throw<DomainException>(() => JobSite.Create(code, "Another warehouse"));
    }

    [Fact]
    public void JobSiteCreateIsActive()
    {
        var site = JobSite.Create("riverside", "Riverside Clinic");

        site.Code.ShouldBe("RIVERSIDE");
        site.Active.ShouldBeTrue();
    }
}
