-- One row per day from the first posting or movement to the last, so daily
-- facts can be joined without gaps.
with bounds as (

    select
        least(
            (select min(cast(posted_at as date)) from {{ ref('stg_order_lines') }}),
            (select min(occurred_on) from {{ ref('stg_stock_movements') }})
        ) as first_day,
        greatest(
            (select max(cast(posted_at as date)) from {{ ref('stg_order_lines') }}),
            (select max(occurred_on) from {{ ref('stg_stock_movements') }})
        ) as last_day

),

days as (

    select cast(unnest(generate_series(first_day, last_day, interval 1 day)) as date) as date_day
    from bounds

)

select
    date_day,
    extract(year from date_day) as year,
    extract(month from date_day) as month,
    extract(day from date_day) as day_of_month,
    isodow(date_day) as iso_day_of_week,
    weekofyear(date_day) as iso_week,
    isodow(date_day) in (6, 7) as is_weekend
from days
