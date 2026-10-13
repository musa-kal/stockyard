/*
    dbo.vw_OpenPurchaseOrders

    Purpose:     purchase order lines that are posted and still waiting for stock, with the
                 quantity outstanding. Drives GET /api/v1/purchase-orders/open.
    Parameters:  none.
    Result set:  PurchaseOrderId, Number, Status, SupplierCode, JobSiteCode, PostedAt,
                 LineNumber, ItemSku, QuantityOrdered, QuantityReceived, QuantityOutstanding, UnitCost
    Errors:      none.
    Locking:     reader only.
*/
CREATE OR ALTER VIEW dbo.vw_OpenPurchaseOrders
AS
SELECT
    po.Id AS PurchaseOrderId,
    po.Number,
    po.Status,
    s.Code AS SupplierCode,
    js.Code AS JobSiteCode,
    po.PostedAt,
    l.LineNumber,
    i.Sku AS ItemSku,
    l.QuantityOrdered,
    l.QuantityReceived,
    l.QuantityOrdered - l.QuantityReceived AS QuantityOutstanding,
    l.UnitCost
FROM dbo.PurchaseOrder AS po
INNER JOIN dbo.PurchaseOrderLine AS l
    ON l.PurchaseOrderId = po.Id
INNER JOIN dbo.Supplier AS s
    ON s.Id = po.SupplierId
INNER JOIN dbo.JobSite AS js
    ON js.Id = po.JobSiteId
INNER JOIN dbo.Item AS i
    ON i.Id = l.ItemId
WHERE po.Status IN (1, 2) -- Posted, PartiallyReceived
    AND l.QuantityReceived < l.QuantityOrdered;
