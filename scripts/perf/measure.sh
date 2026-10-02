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
