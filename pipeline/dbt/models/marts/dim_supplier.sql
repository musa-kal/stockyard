select
    supplier_code,
    count(distinct order_id) as orders_posted,
    min(posted_at) as first_order_posted_at,
    max(posted_at) as last_order_posted_at
from {{ ref('stg_order_lines') }}
group by supplier_code
