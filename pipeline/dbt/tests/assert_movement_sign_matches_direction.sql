-- The ledger stores signed quantities: receipts add stock, issues and returns
-- to the supplier take it away, adjustments may go either way. A receipt with
-- a negative quantity (or an issue with a positive one) means the sign was
-- flipped somewhere between the procedure and the export.
select
    movement_id,
    kind,
    direction,
    quantity
from {{ ref('fct_stock_movements') }}
where quantity = 0
    or (direction = 'in' and quantity < 0)
    or (direction = 'out' and quantity > 0)
