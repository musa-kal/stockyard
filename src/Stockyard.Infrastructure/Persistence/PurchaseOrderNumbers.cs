using Microsoft.EntityFrameworkCore;
using Stockyard.Domain;

namespace Stockyard.Infrastructure.Persistence;

public static class PurchaseOrderNumbers
{
    /// <summary>Draws the next value of dbo.PurchaseOrderNumber and formats it as PO-001000.</summary>
    public static async Task<string> NextPurchaseOrderNumberAsync(this StockyardDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        // NEXT VALUE FOR cannot sit in a derived table, so the query is not composed further.
        var values = await db.Database
            .SqlQuery<long>($"SELECT NEXT VALUE FOR dbo.PurchaseOrderNumber AS Value")
            .ToListAsync(cancellationToken);

        return PurchaseOrderNumber.Format(values.Single());
    }
}
