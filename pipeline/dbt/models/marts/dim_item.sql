-- Every SKU seen on an order line or in the ledger. The export carries only
-- the SKU, so description and unit of measure stay in the application.
with skus as (

    select item_sku, posted_at as seen_at from {{ ref('stg_order_lines') }}
    union all
    select item_sku, occurred_at as seen_at from {{ ref('stg_stock_movements') }}

)

select
    item_sku,
    min(seen_at) as first_seen_at,
    max(seen_at) as last_seen_at
from skus
group by item_sku
