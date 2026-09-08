using Microsoft.EntityFrameworkCore.Migrations;
using Stockyard.Infrastructure.Sql;

#nullable disable

namespace Stockyard.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Installs the hand-written procedures, table type, views and covering index from the
    /// embedded Sql/*.sql files. The scripts are re-runnable, so a later migration that changes
    /// one of them can run that script again.
    /// </summary>
    public partial class SqlObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var name in SqlScripts.InstallOrder)
            {
                migrationBuilder.Sql(SqlScripts.Read(name));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_ReconcileStock;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_IssueStock;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_ReceiveDelivery;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_PostPurchaseOrder;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_OpenPurchaseOrders;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_StockOnHand;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS dbo.ReceiptLineType;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_StockMovement_ItemId_OccurredAt ON dbo.StockMovement;");
            migrationBuilder.Sql(
                "IF INDEXPROPERTY(OBJECT_ID(N'dbo.StockMovement'), N'IX_StockMovement_ItemId', 'IndexID') IS NULL "
                + "CREATE INDEX IX_StockMovement_ItemId ON dbo.StockMovement (ItemId);");
        }
    }
}
