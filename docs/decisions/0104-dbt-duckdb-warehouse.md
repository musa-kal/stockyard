# 0104: A dbt-duckdb warehouse over the Parquet exports

Status: accepted

## Context

Reporting questions (spend by supplier, what is still outstanding on open orders, how stock at each site moved day by day) should not run against the transactional SQL Server instance, which is a small Express edition box sized for the ledger.

The Exporter already writes one folder per UTC day of Parquet files to S3: order lines and stock movements. Something has to turn those files into a model people can query, with tests that catch a bad export.

Options considered: Athena with Glue tables over the bucket, Redshift Serverless, or dbt running on DuckDB reading the Parquet directly.

## Decision

Use dbt with the `dbt-duckdb` adapter. Sources read the export layout with `read_parquet` over a glob; staging models clean and deduplicate; marts form a small star schema (item, supplier, job site, and date dimensions; order line, stock movement, and daily balance facts). The same project runs against a generated sample set locally (`local` target) and against the bucket through `httpfs` and the AWS credential chain (`s3` target).

Why:

* Nothing to provision. DuckDB is a library inside the dbt virtualenv, so there is no warehouse to pay for while idle, which suits a project that is torn down between uses.
* The full model and its tests run on a laptop in seconds against `scripts/make_sample_parquet.py` output, so a change to the models is tested before it ever sees real data.
* Moving to Athena or Redshift later is a profile and adapter change; the models are plain SQL with a few DuckDB functions (`strptime`, `regexp_extract`, `generate_series`) that would need equivalents.

## Consequences

* The warehouse is a DuckDB file built by whoever runs dbt, not a shared service. Sharing results means publishing that file or the mart tables (for example back to S3 as Parquet), which is not built yet.
* Every run rescans all export days. At this data volume that is fine; with years of exports the sources would need to read only new folders, or the facts would become incremental models.
* dbt does not run in the Azure pipeline yet; it is run by hand after the nightly export.
