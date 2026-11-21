# WguLti database scripts (`wgulti`)

Production-ready PostgreSQL 18 scripts for the WguLti LTI 1.3 Platform.
All objects live in schema `lti` of a dedicated database `wgulti`
(separate from `wgu` and `eCademyPenspringDB`).

| File | Run as | Purpose |
|---|---|---|
| `000_create_database.sql` | superuser (local) / RDS master | Creates roles `wgulti_owner` (NOLOGIN, owns everything), `wgulti_app` (LOGIN, DML only), `wgulti_readonly` (reporting) and database `wgulti` (UTF8). Re-runnable; re-running rotates passwords. Not a migration. |
| `V001__baseline.sql` | admin (does `SET ROLE wgulti_owner`) | Full schema: 50 tables + monthly audit partitions, FKs, CHECKs, indexes, triggers, grade sync, grants, default privileges. |
| `V002__seed_reference.sql` | admin | Reference data only: LIS v2 role vocabulary (39), permission catalog (23), 6 built-in roles and their default permissions. No users, tenants or secrets. |
| `tests/smoke.sql` | `wgulti_app` | End-to-end DML chain incl. AGS score -> result -> submission -> grade history; rolled back. |
| `tests/permissions.sql` | `wgulti_app` | Proves DDL and protected writes are denied (16 checks). |
| `tests/readonly.sql` | `wgulti_readonly` | Proves read access, no access to credential tables, no writes. |
| `tests/run_validation.sh` | any (Linux/WSL) | Spins up a throwaway PG18 cluster and runs everything twice + all tests. |

Versioned files use Flyway naming (`V<version>__<desc>.sql`), are plain SQL (no psql
meta-commands) and every one also records itself in `lti.schema_migrations`.
New changes go in `V003__...sql`, `V004__...sql`; never edit an applied file.

## Requirements
* PostgreSQL **18+** server (V001 refuses to run on older versions: uses `uuidv7()`).
* `psql` 16+ client (`000` uses `\if`, `\gexec`).
* Extension `citext` (bundled locally; allowed on RDS).

## Local: Creator (Windows, Postgres 18 at localhost:5432)

PowerShell, from `C:\stash\WguLti\db`:

```powershell
$env:PGHOST = "localhost"; $env:PGPORT = "5432"
$env:PGPASSWORD = Read-Host "postgres superuser password"     # or use %APPDATA%\postgresql\pgpass.conf

# 1. database + roles (pick a dev password; store it in dotnet user-secrets, not in git)
psql -U postgres -d postgres -v ON_ERROR_STOP=1 -v app_password="CHANGE-ME-dev" -f 000_create_database.sql

# 2. schema + reference data
psql -U postgres -d wgulti -v ON_ERROR_STOP=1 --single-transaction -f V001__baseline.sql
psql -U postgres -d wgulti -v ON_ERROR_STOP=1 --single-transaction -f V002__seed_reference.sql

# 3. smoke test as the app login
$env:PGPASSWORD = "CHANGE-ME-dev"
psql -U wgulti_app -d wgulti -v ON_ERROR_STOP=1 -f tests\smoke.sql
psql -U wgulti_app -d wgulti -v ON_ERROR_STOP=1 -f tests\permissions.sql
Remove-Item Env:PGPASSWORD
```

App connection string (`dotnet user-secrets set ConnectionStrings:Lti "..."` in WguLti.Api):

```
Host=localhost;Port=5432;Database=wgulti;Username=wgulti_app;Password=<dev pw>;Search Path=lti
```

Reset local DB (destructive): `psql -U postgres -d postgres -c "DROP DATABASE wgulti WITH (FORCE)"`, then rerun steps 1-2.

## AWS RDS for PostgreSQL 18

Use the RDS master user (member of `rds_superuser`, not a true superuser; the scripts
handle that by granting the master `SET ROLE wgulti_owner`). Keep passwords in
Secrets Manager; connect with TLS.

```bash
export PGHOST=<instance>.xxxx.us-west-2.rds.amazonaws.com PGPORT=5432 PGUSER=<master>
export PGSSLMODE=verify-full PGSSLROOTCERT=./global-bundle.pem   # https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem
export PGPASSWORD=$(aws secretsmanager get-secret-value --secret-id wgulti/master --query SecretString --output text | jq -r .password)
APP_PW=$(aws secretsmanager get-secret-value --secret-id wgulti/app --query SecretString --output text | jq -r .password)
export PGOPTIONS='-c log_statement=none'      # keep the password out of the server log

psql -d postgres -v ON_ERROR_STOP=1 -v app_password="$APP_PW" -f 000_create_database.sql
psql -d wgulti   -v ON_ERROR_STOP=1 --single-transaction -f V001__baseline.sql
psql -d wgulti   -v ON_ERROR_STOP=1 --single-transaction -f V002__seed_reference.sql
unset PGPASSWORD APP_PW
```

Rotate the app password: update the secret, rerun only `000_create_database.sql` with the new value.

Recommended RDS parameter group: `rds.force_ssl=1`, `log_min_duration_statement=500`,
`shared_preload_libraries=pg_stat_statements`. Run this from a bastion/SSM session or a
GitHub Actions job inside the VPC, never from a public endpoint.

## Using Flyway or DbUp instead of psql
* `000_create_database.sql` stays a one-time psql step (it creates the DB the tool connects to).
* Flyway: `flyway -url=jdbc:postgresql://host:5432/wgulti -user=<admin> -locations=filesystem:db -initSql="SET ROLE wgulti_owner" -defaultSchema=lti -createSchemas=false migrate`
  (000 is ignored because it lacks the `V` prefix; `initSql` makes the history table owner-owned too).
* DbUp: `WithScriptsFromFileSystem("db", f => Path.GetFileName(f).StartsWith("V"))`,
  `JournalToPostgresqlTable("lti", "schemaversions")`, connection string option `Options=-c role=wgulti_owner`.

## Operations
* Audit log is range-partitioned by month. The worker (as `wgulti_app`) calls
  `SELECT lti.ensure_audit_partitions(3);` daily; it is `SECURITY DEFINER`, so no DDL rights are needed,
  and it moves any rows that fell into `audit_log_default`.
* Retention (owner/ops only): `SELECT lti.drop_audit_partitions_older_than(84);` (months).
* Housekeeping every few minutes: `SELECT lti.purge_expired();`
* `wgulti_app` cannot UPDATE/DELETE `audit_log`, `grade_changes`, `scores`, `enrollment_changes` (append-only).
* `wgulti_readonly` cannot read credential/session tables (`user_logins`, `user_sessions`, `api_tokens`,
  `access_tokens`, `oidc_login_sessions`, `platform_keys`, `dynamic_registrations`, `lti1p1_consumers`,
  `identity_providers`). Future tables are readable by default: revoke in the migration that adds a sensitive one.

## Grade flow (how AGS reaches the gradebook)
`scores` (append-only, what tools POST) -> trigger `apply_score` upserts `results` only when the tool
`timestamp` is newer, normalising to the line item maximum -> trigger `sync_result_to_submission`
updates the assignment's `submissions` row for the primary line item: FullyGraded sets score/grade
(scaled to `points_possible`), PendingManual sets `pending_review`, Submitted/Completed sets
`submitted_at`. Rows with `instructor_override` or `excused` are never changed by AGS.
Every score/grade/excused change on `submissions` is appended to `grade_changes`.
