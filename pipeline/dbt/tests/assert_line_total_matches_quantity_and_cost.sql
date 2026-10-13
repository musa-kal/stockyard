-- LineTotal is a persisted computed column (QuantityOrdered * UnitCost) in SQL
-- Server. If the export ever disagrees, the export or the column changed.
select
    order_line_key,
    quantity_ordered,
    unit_cost,
    line_total
from {{ ref('fct_purchase_order_lines') }}
where abs(line_total - quantity_ordered * unit_cost) > 0.0001
