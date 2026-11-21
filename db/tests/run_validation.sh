#!/usr/bin/env bash
# End-to-end validation on a throwaway PostgreSQL 18 cluster (Linux/macOS/WSL).
# Simulates RDS: migrations run as a NON-superuser admin with CREATEROLE/CREATEDB.
set -euo pipefail
BIN=${PGBIN:-/usr/lib/postgresql/18/bin}; PORT=${PORT:-55418}; DATA=$(mktemp -d)
cd "$(dirname "$0")/.."
"$BIN/initdb" -D "$DATA" -U postgres -E UTF8 >/dev/null
"$BIN/pg_ctl" -D "$DATA" -o "-p $PORT -k /tmp" -l "$DATA/log" start >/dev/null; sleep 2
trap '"$BIN/pg_ctl" -D "$DATA" stop >/dev/null; rm -rf "$DATA"' EXIT
PS="psql -h /tmp -p $PORT -X -q -v ON_ERROR_STOP=1"
$PS -U postgres -d postgres -c "CREATE ROLE migrator LOGIN CREATEROLE CREATEDB"
for i in 1 2; do   # run everything twice to prove re-runnability
  $PS -U migrator -d postgres -v app_password="app-$i" -v readonly_password="ro-$i" -f 000_create_database.sql
  $PS -U migrator -d wgulti --single-transaction -f V001__baseline.sql
  $PS -U migrator -d wgulti --single-transaction -f V002__seed_reference.sql
done
$PS -U wgulti_app      -d wgulti -f tests/smoke.sql
$PS -U wgulti_app      -d wgulti -f tests/permissions.sql
$PS -U wgulti_readonly -d wgulti -f tests/readonly.sql
echo "ALL CHECKS PASSED"
