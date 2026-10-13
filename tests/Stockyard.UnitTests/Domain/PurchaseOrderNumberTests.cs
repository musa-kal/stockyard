using Stockyard.Domain;

namespace Stockyard.UnitTests.Domain;

public sealed class PurchaseOrderNumberTests
{
    [Theory]
    [InlineData(1000, "PO-001000")]
    [InlineData(1001, "PO-001001")]
    [InlineData(7, "PO-000007")]
    [InlineData(1234567, "PO-1234567")]
    public void FormatPadsToSixDigits(long value, string expected)
    {
        PurchaseOrderNumber.Format(value).ShouldBe(expected);
    }

    [Fact]
    public void FormatRejectsNegativeValues()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PurchaseOrderNumber.Format(-1));
    }
}
