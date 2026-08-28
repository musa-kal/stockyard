# 0102: RDS for SQL Server, Express edition

Status: accepted

## Context

The application is written against SQL Server 2022: hand-written T-SQL procedures, a table type, `rowversion`, filtered unique indexes, `sp_getapplock`, and `FOR JSON PATH` payloads. Moving to PostgreSQL or Aurora would mean rewriting the posting, receiving, issuing, and reconcile procedures, which are the part of the project worth showing.

The AWS options for SQL Server are RDS (Express, Web, Standard, Enterprise) or SQL Server on EC2. The workload is a single small ledger with low write volume.

## Decision

Run RDS for SQL Server 2022 Express edition, license included, single-AZ, on `db.t3.small` with 20 GiB of gp3 storage, encrypted, in the private subnets. RDS manages the master password in Secrets Manager (`manage_master_user_password`), so it never sits in Terraform state.

List prices looked up for this ADR (us-east-1, on-demand, single-AZ, license included; see the cost table in docs/platform.md for the date and source): Express on `db.t3.small` is 0.044 USD per hour, Web on the same class is 0.139 USD per hour, and Standard starts on `db.t3.xlarge` at 1.044 USD per hour.

## Consequences

Express edition limits that apply here:

* Each database is capped at 10 GB. Storage autoscaling grows the volume, but not past that per-database limit. The ledger (`StockMovement`) is the table that grows; the nightly Parquet export to S3 is the long-term history, so old movements could be archived if the cap ever gets close. The free storage alarm watches the volume, not the database, so database size is something to check by hand (`sp_spaceused`) until it matters.
* Compute is limited to the lesser of one socket or four cores, and the buffer pool to about 1.4 GB.
* No Multi-AZ. A host failure means RDS replaces the instance and the services wait on `/readyz`. Recovery point is the automated backup plus transaction logs (point in time restore, 7 days by default).
* No SQL Server Agent. Nothing in Stockyard needs it: the nightly reconcile runs in the Exporter, not as an Agent job.

Moving to Web or Standard later is a snapshot restore into a new instance with a different `engine`, which the RDS module would take as a variable.
