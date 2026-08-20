namespace Stockyard.Domain;

public sealed class JobSite
{
    /// <summary>Code of the row that represents stock held off site.</summary>
    public const string WarehouseCode = "WAREHOUSE";

    /// <summary>Id of the warehouse row. Stock movements store null instead of this value.</summary>
    public static readonly Guid WarehouseId = Guid.Empty;

    public const int NameMaxLength = 120;

    public Guid Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool Active { get; set; } = true;

    /// <summary>Creates a job site. The warehouse code is reserved for the row the migration inserts.</summary>
    public static JobSite Create(string? code, string? name)
    {
        var normalized = Codes.NormalizeCode(code, "code");
        if (normalized == WarehouseCode)
        {
            throw new DomainException($"{WarehouseCode} is reserved for stock held off site.");
        }

        return new JobSite { Code = normalized, Name = Text.Required(name, NameMaxLength, "name") };
    }
}
