using System.Globalization;

namespace Stockyard.Domain;

/// <summary>Formats values of the dbo.PurchaseOrderNumber sequence as PO-001000, PO-001001, ...</summary>
public static class PurchaseOrderNumber
{
    public const string Prefix = "PO-";

    public static string Format(long sequenceValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequenceValue);
        return Prefix + sequenceValue.ToString("D6", CultureInfo.InvariantCulture);
    }
}
