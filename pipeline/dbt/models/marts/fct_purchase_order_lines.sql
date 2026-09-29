-- Grain: one purchase order line, as of its latest export.
select
    order_line_key,
    order_id,
    order_number,
    line_number,
    supplier_code,
    job_site_code,
    item_sku,
    cast(posted_at as date) as posted_on,
    posted_at,
    quantity_ordered,
    quantity_received,
    quantity_ordered - quantity_received as quantity_outstanding,
    unit_cost,
    line_total,
    export_date as as_of_export_date
from {{ ref('stg_order_lines') }}
