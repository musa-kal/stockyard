namespace Stockyard.Domain;

/// <summary>Why a stock movement exists. Stored as tinyint, so the values are fixed.</summary>
public enum MovementKind : byte
{
    Receipt = 1,
    Issue = 2,
    Adjustment = 3,
    ReturnToSupplier = 4,
}
