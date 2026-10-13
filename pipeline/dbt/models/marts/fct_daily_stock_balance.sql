-- Grain: item, job site, and day. Closing balance is the running sum of the
-- ledger, the same arithmetic as dbo.vw_StockOnHand, so the last day should
-- match what the API reports for each item and site.
with daily as (

    select
        item_sku,
        job_site_code,
        occurred_on,
        sum(quantity) as net_quantity,
        count(*) as movements
    from {{ ref('fct_stock_movements') }}
    group by item_sku, job_site_code, occurred_on

),

pairs as (

    select
        item_sku,
        job_site_code,
        min(occurred_on) as first_day
    from daily
    group by item_sku, job_site_code

),

grid as (

    select
        pairs.item_sku,
        pairs.job_site_code,
        dates.date_day
    from pairs
    inner join {{ ref('dim_date') }} as dates
        on dates.date_day >= pairs.first_day

)

select
    grid.item_sku,
    grid.job_site_code,
    grid.date_day,
    coalesce(daily.net_quantity, 0) as net_quantity,
    coalesce(daily.movements, 0) as movements,
    sum(coalesce(daily.net_quantity, 0)) over (
        partition by grid.item_sku, grid.job_site_code
        order by grid.date_day
        rows between unbounded preceding and current row
    ) as closing_quantity
from grid
left join daily
    on grid.item_sku = daily.item_sku
    and grid.job_site_code = daily.job_site_code
    and grid.date_day = daily.occurred_on
