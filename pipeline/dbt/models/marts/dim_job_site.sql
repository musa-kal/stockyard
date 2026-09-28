-- Job sites that ordered or moved stock, plus WAREHOUSE (the exporter's code
-- for stock held off site) so ledger rows always have a site to join to.
with codes as (

    select job_site_code from {{ ref('stg_order_lines') }}
    union
    select job_site_code from {{ ref('stg_stock_movements') }}
    union
    select 'WAREHOUSE'

)

select
    job_site_code,
    job_site_code = 'WAREHOUSE' as is_warehouse
from codes
