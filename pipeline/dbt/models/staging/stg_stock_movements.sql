-- One row per ledger movement. Quantity keeps the ledger's sign: receipts are
-- positive, issues and returns negative, adjustments either way.
with source as (

    select * from {{ source('exports', 'stock_movements') }}

)

select
    movement_id,
    upper(item_sku) as item_sku,
    upper(job_site_code) as job_site_code,
    kind,
    quantity,
    reference,
    cast(occurred_at as timestamp) as occurred_at,
    cast(occurred_at as date) as occurred_on,
    cast(
        strptime(regexp_extract(replace(filename, '\', '/'), '(\d{4}/\d{2}/\d{2})/[^/]+$', 1), '%Y/%m/%d')
        as date
    ) as export_date
from source
