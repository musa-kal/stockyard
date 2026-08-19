using Stockyard.Domain;

namespace Stockyard.UnitTests.Domain;

public sealed class CodesTests
{
    [Theory]
    [InlineData("ngps", "NGPS")]
    [InlineData("  keel-01 ", "KEEL-01")]
    [InlineData("pipe_cu.15", "PIPE_CU.15")]
    public void NormalizeCodeTrimsAndUpperCases(string input, string expected)
    {
        Codes.NormalizeCode(input, "code").ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeCodeRejectsMissingValues(string? input)
    {
        var ex = Should.Throw<DomainException>(() => Codes.NormalizeCode(input, "supplierCode"));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.Validation);
        ex.Message.ShouldContain("supplierCode");
    }

    [Fact]
    public void NormalizeCodeRejectsValuesLongerThanSixteen()
    {
        Should.Throw<DomainException>(() => Codes.NormalizeCode(new string('A', 17), "code"));
        Codes.NormalizeCode(new string('a', 16), "code").ShouldBe(new string('A', 16));
    }

    [Fact]
    public void NormalizeSkuAllowsThirtyTwoCharacters()
    {
        Codes.NormalizeSku(new string('v', 32), "sku").Length.ShouldBe(32);
        Should.Throw<DomainException>(() => Codes.NormalizeSku(new string('v', 33), "sku"));
    }

    [Theory]
    [InlineData("A B")]
    [InlineData("A/B")]
    [InlineData("caf\u00e9")]
    public void NormalizeRejectsCharactersOutsideTheAllowedSet(string input)
    {
        Should.Throw<DomainException>(() => Codes.NormalizeCode(input, "code"));
    }
}
