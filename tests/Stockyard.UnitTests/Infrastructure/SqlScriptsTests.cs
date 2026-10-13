using Stockyard.Infrastructure.Sql;

namespace Stockyard.UnitTests.Infrastructure;

public sealed class SqlScriptsTests
{
    public static TheoryData<string> Scripts => [.. SqlScripts.InstallOrder];

    [Fact]
    public void InstallOrderListsEveryEmbeddedScriptOnce()
    {
        SqlScripts.InstallOrder.ShouldBeUnique();
        SqlScripts.EmbeddedNames().OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(SqlScripts.InstallOrder.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void EveryScriptStartsWithAHeaderComment(string name)
    {
        var sql = SqlScripts.Read(name);

        sql.TrimStart().ShouldStartWith("/*");
        sql.ShouldContain("Purpose:");
        sql.ShouldContain("Errors:");
        sql.ShouldContain("Locking:");
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void EveryScriptIsReRunnable(string name)
    {
        var sql = SqlScripts.Read(name);

        var reRunnable = sql.Contains("CREATE OR ALTER", StringComparison.Ordinal)
            || sql.Contains("IF NOT EXISTS", StringComparison.Ordinal)
            || sql.Contains("IS NULL", StringComparison.Ordinal);
        reRunnable.ShouldBeTrue();
    }

    [Fact]
    public void ReadThrowsForAnUnknownScript()
    {
        Should.Throw<InvalidOperationException>(() => SqlScripts.Read("usp_DoesNotExist"));
    }
}
