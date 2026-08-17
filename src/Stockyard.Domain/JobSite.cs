namespace Stockyard.Domain;

public sealed class JobSite
{
    /// <summary>Code of the row that represents stock held off site.</summary>
    public const string WarehouseCode = "WAREHOUSE";

    /// <summary>Id of the warehouse row. Stock movements store null instead of this value.</summary>
    public static readonly Guid WarehouseId = Guid.Empty;

    public Guid Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool Active { get; set; } = true;
}
