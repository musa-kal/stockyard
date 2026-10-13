using Stockyard.Domain;

namespace Stockyard.Api.Contracts;

public sealed record CreatePurchaseOrderRequest(string? SupplierCode, string? JobSiteCode);

public sealed record AddPurchaseOrderLineRequest(string? ItemSku, decimal QuantityOrdered, decimal? UnitCost);

public sealed record PurchaseOrderResponse(
    Guid Id,
    string Number,
    PurchaseOrderStatus Status,
    string SupplierCode,
    string JobSiteCode,
    DateTime? PostedAt,
    IReadOnlyList<PurchaseOrderLineResponse> Lines);

public sealed record PurchaseOrderLineResponse(
    int LineNumber,
    string ItemSku,
    decimal QuantityOrdered,
    decimal QuantityReceived,
    decimal UnitCost,
    decimal LineTotal);

/// <summary>The line created by POST .../lines, with its own id and the order it belongs to.</summary>
public sealed record CreatedPurchaseOrderLineResponse(
    Guid Id,
    Guid PurchaseOrderId,
    int LineNumber,
    string ItemSku,
    decimal QuantityOrdered,
    decimal QuantityReceived,
    decimal UnitCost,
    decimal LineTotal);

public sealed record ReceiveDeliveryRequest(IReadOnlyList<ReceiptLineRequest>? Lines);

public sealed record ReceiptLineRequest(int LineNumber, decimal Quantity);

/// <summary>A line of dbo.vw_OpenPurchaseOrders.</summary>
public sealed record OpenPurchaseOrderLineResponse(
    Guid PurchaseOrderId,
    string Number,
    PurchaseOrderStatus Status,
    string SupplierCode,
    string JobSiteCode,
    DateTime? PostedAt,
    int LineNumber,
    string ItemSku,
    decimal QuantityOrdered,
    decimal QuantityReceived,
    decimal QuantityOutstanding,
    decimal UnitCost);
