-- usp_IssueStock refuses an issue that would take stock below zero (error
-- 50003), so no item should ever close a day negative at any site. A row here
-- means a movement bypassed the procedure or an export day is missing.
select
    item_sku,
    job_site_code,
    date_day,
    closing_quantity
from {{ ref('fct_daily_stock_balance') }}
where closing_quantity < 0
