using Stockyard.Domain;

namespace Stockyard.UnitTests.Domain;

public sealed class PurchaseOrderTests
{
    private static readonly Item Elbow = new()
    {
        Id = Guid.NewGuid(),
        Sku = "ELBOW-CU-22",
        Description = "Copper elbow 22 mm",
        UnitOfMeasure = "EA",
        UnitCost = 3.4500m,
    };

    [Fact]
    public void AddLineNumbersLinesFromOne()
    {
        var order = NewOrder();

        order.AddLine(Elbow, 10).LineNumber.ShouldBe(1);
        order.AddLine(Elbow, 5).LineNumber.ShouldBe(2);
    }

    [Fact]
    public void AddLineDefaultsUnitCostToTheItemCost()
    {
        var order = NewOrder();

        order.AddLine(Elbow, 10).UnitCost.ShouldBe(3.4500m);
        order.AddLine(Elbow, 10, 2.10m).UnitCost.ShouldBe(2.10m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddLineRejectsNonPositiveQuantities(decimal quantity)
    {
        var ex = Should.Throw<DomainException>(() => NewOrder().AddLine(Elbow, quantity));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.Validation);
    }

    [Fact]
    public void AddLineRejectsNegativeUnitCost()
    {
        Should.Throw<DomainException>(() => NewOrder().AddLine(Elbow, 1, -0.01m));
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Posted)]
    [InlineData(PurchaseOrderStatus.Received)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public void AddLineRequiresADraft(PurchaseOrderStatus status)
    {
        var order = NewOrder();
        order.Status = status;

        var ex = Should.Throw<DomainException>(() => order.AddLine(Elbow, 1));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.InvalidState);
    }

    private static PurchaseOrder NewOrder() => new() { Id = Guid.NewGuid(), Number = PurchaseOrderNumber.Format(1000) };
}
