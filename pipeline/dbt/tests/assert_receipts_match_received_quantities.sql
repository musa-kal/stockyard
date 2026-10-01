-- Every receipt in the ledger references its purchase order number. The sum
-- of receipts per order and item should equal what the order line says was
-- received, and never exceed what was ordered.
with receipts as (

    select
        reference as order_number,
        item_sku,
        sum(quantity) as received_in_ledger
    from {{ ref('fct_stock_movements') }}
    where kind = 'Receipt'
    group by reference, item_sku

),

lines as (

    select
        order_number,
        item_sku,
        sum(quantity_ordered) as quantity_ordered,
        sum(quantity_received) as quantity_received
    from {{ ref('fct_purchase_order_lines') }}
    group by order_number, item_sku

)

select
    coalesce(lines.order_number, receipts.order_number) as order_number,
    coalesce(lines.item_sku, receipts.item_sku) as item_sku,
    lines.quantity_ordered,
    lines.quantity_received,
    receipts.received_in_ledger
from lines
full outer join receipts
    on lines.order_number = receipts.order_number
    and lines.item_sku = receipts.item_sku
where coalesce(receipts.received_in_ledger, 0) <> coalesce(lines.quantity_received, 0)
    or coalesce(lines.quantity_received, 0) > coalesce(lines.quantity_ordered, 0)
