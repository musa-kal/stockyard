-- Grain: one ledger movement. Joined to the movement kinds seed for the code
-- the application stores and the direction the movement should have.
select
    movements.movement_id,
    movements.item_sku,
    movements.job_site_code,
    movements.kind,
    kinds.kind_code,
    kinds.direction,
    movements.quantity,
    movements.reference,
    movements.occurred_at,
    movements.occurred_on
from {{ ref('stg_stock_movements') }} as movements
left join {{ ref('movement_kinds') }} as kinds
    on movements.kind = kinds.kind
