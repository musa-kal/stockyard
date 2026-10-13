namespace Stockyard.Infrastructure.Sql;

/// <summary>
/// The hand-written T-SQL objects embedded in this assembly. Every script is re-runnable, and
/// <see cref="InstallOrder"/> puts the table type and views before the procedures that use them.
/// </summary>
public static class SqlScripts
{
    public static IReadOnlyList<string> InstallOrder { get; } =
    [
        "ReceiptLineType",
        "vw_StockOnHand",
        "vw_OpenPurchaseOrders",
        "usp_PostPurchaseOrder",
        "usp_ReceiveDelivery",
        "usp_IssueStock",
        "usp_ReconcileStock",
        "IX_StockMovement_ItemId_OccurredAt",
    ];

    public static string Read(string name)
    {
        var resourceName = ResourcePrefix + name + ".sql";
        using var stream = typeof(SqlScripts).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"SQL script '{name}' is not embedded in {typeof(SqlScripts).Assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IEnumerable<string> EmbeddedNames() =>
        typeof(SqlScripts).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..^".sql".Length]);

    private const string ResourcePrefix = "Stockyard.Infrastructure.Sql.";
}
