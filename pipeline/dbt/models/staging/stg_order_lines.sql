-- One row per purchase order line. An order is exported again on every day it
-- is received against, so the same line can appear in several export folders
-- with a growing quantity_received; the most recent export wins. The export
-- day comes from the folder the file was read from, not from the data.
with source as (

    select * from {{ source('exports', 'order_lines') }}

),

exported as (

    select
        order_id || '-' || cast(line_number as varchar) as order_line_key,
        order_id,
        order_number,
        upper(supplier_code) as supplier_code,
        upper(job_site_code) as job_site_code,
        cast(posted_at as timestamp) as posted_at,
        line_number,
        upper(item_sku) as item_sku,
        quantity_ordered,
        quantity_received,
        unit_cost,
        line_total,
        cast(
            strptime(regexp_extract(replace(filename, '\', '/'), '(\d{4}/\d{2}/\d{2})/[^/]+$', 1), '%Y/%m/%d')
            as date
        ) as export_date
    from source

)

select *
from exported
qualify row_number() over (partition by order_line_key order by export_date desc) = 1
