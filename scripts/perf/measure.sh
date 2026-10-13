#!/usr/bin/env bash
# Measures the stock on hand read path (docs/performance.md) on the compose SQL Server.
#   bash ./scripts/perf/measure.sh
# Uses its own database (StockyardPerf by default), migrated with the real migrations and
# seeded with 1,000,000 movements, then runs scripts/perf/measure.sql once per index state.
set -euo pipefail

DATABASE="${DATABASE:-StockyardPerf}"
SKU="${SKU:-PERF-01000}"
STAGES="${STAGES:-none fk covering}"

cd "$(dirname "$0")/../.."
export MSYS_NO_PATHCONV=1  # keep Git Bash on Windows from rewriting the container paths

password=$(sed -n 's/^MSSQL_SA_PASSWORD=//p' .env)
[ -n "$password" ] || { echo "MSSQL_SA_PASSWORD is not set in .env" >&2; exit 1; }

# sqlcmd reads -v before -i; the password comes from the container's own environment.
run_sql() {
  local file=$1; shift
  local variables=""
  [ $# -gt 0 ] && variables="-v $*"
  docker compose exec -T sqlserver bash -c \
    "SQLCMDPASSWORD=\"\$MSSQL_SA_PASSWORD\" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d $DATABASE -b -W -s '|' $variables -i /tmp/$file"
}

run_query() {
  docker compose exec -T sqlserver bash -c \
    "SQLCMDPASSWORD=\"\$MSSQL_SA_PASSWORD\" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d $DATABASE -b -W -s '|' -Q \"$1\""
}

reconcile_query="SET NOCOUNT ON;
CREATE TABLE #r (ItemSku nvarchar(32), JobSiteCode nvarchar(16), Projected decimal(18, 3), Actual decimal(18, 3));
DECLARE @t datetime2(7) = SYSDATETIME();
INSERT #r EXEC dbo.usp_ReconcileStock @Apply = 1;
SELECT 'apply' AS Run, COUNT(*) AS Differences, DATEDIFF(MILLISECOND, @t, SYSDATETIME()) AS ElapsedMs FROM #r;
TRUNCATE TABLE #r;
SET @t = SYSDATETIME();
INSERT #r EXEC dbo.usp_ReconcileStock @Apply = 0;
SELECT 'report' AS Run, COUNT(*) AS Differences, DATEDIFF(MILLISECOND, @t, SYSDATETIME()) AS ElapsedMs FROM #r;"

echo "==> SQL Server and the $DATABASE database"
docker compose up -d --wait sqlserver
docker compose run --rm --no-deps \
  -e "ConnectionStrings__Stockyard=Server=sqlserver,1433;Database=$DATABASE;User Id=sa;Password=$password;TrustServerCertificate=True" \
  migrate

echo "==> seeding 1,000,000 movements (skipped when already there)"
docker compose cp scripts/perf/seed-movements.sql sqlserver:/tmp/seed-movements.sql
docker compose cp scripts/perf/measure.sql sqlserver:/tmp/measure.sql
started=$(date +%s)
run_sql seed-movements.sql
echo "    seed step took $(( $(date +%s) - started )) s"

for stage in $STAGES; do
  echo "==> stage: $stage"
  run_sql measure.sql "Stage=$stage" "Sku=$SKU"
done

# The first run corrects every pair (the perf data has no projection rows); the second
# one, read only, finds nothing left. Both scan the whole ledger.
echo "==> reconcile over the whole ledger"
run_query "$reconcile_query"
