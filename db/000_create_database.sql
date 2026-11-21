-- =====================================================================
-- 000_create_database.sql  (WguLti / database: wgulti)
-- Run ONCE per server as a superuser (local) or the RDS master user.
-- NOT a Flyway/DbUp migration: it creates the database and roles that
-- the versioned V###__*.sql scripts run inside.
--
--   psql -h <host> -U <admin> -d postgres -v ON_ERROR_STOP=1 \
--        -v app_password='<from secret store>' \
--        [-v readonly_password='<optional>'] \
--        -f 000_create_database.sql
--
-- Safe to rerun: roles/database are created only if missing; passwords
-- are (re)set from the variables on every run (use this to rotate).
-- Tip: set PGOPTIONS='-c log_statement=none' (or run with logging of DDL
-- off) so the password never lands in the server log.
-- =====================================================================
\set ON_ERROR_STOP on

\if :{?app_password}
\else
  DO $$ BEGIN RAISE EXCEPTION 'pass the application password: psql -v app_password=...'; END $$;
\endif

-- ---------- roles ----------
-- wgulti_owner: owns every object, cannot log in.
SELECT 'CREATE ROLE wgulti_owner NOLOGIN'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wgulti_owner') \gexec

-- wgulti_readonly: SELECT on non-sensitive tables (reporting / BI).
SELECT 'CREATE ROLE wgulti_readonly NOLOGIN'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wgulti_readonly') \gexec

-- wgulti_app: the API/worker login. DML only, no DDL.
SELECT 'CREATE ROLE wgulti_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION CONNECTION LIMIT 200'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wgulti_app') \gexec
SELECT format('ALTER ROLE wgulti_app WITH LOGIN PASSWORD %L', :'app_password') \gexec

\if :{?readonly_password}
  SELECT format('ALTER ROLE wgulti_readonly WITH LOGIN PASSWORD %L', :'readonly_password') \gexec
\endif

-- The admin running migrations must be able to SET ROLE wgulti_owner
-- (required on RDS, harmless for a local superuser).
SELECT 'GRANT wgulti_owner TO ' || quote_ident(current_user) || ' WITH SET TRUE, INHERIT FALSE'
WHERE NOT pg_has_role(current_user, 'wgulti_owner', 'SET')
  AND NOT (SELECT rolsuper FROM pg_roles WHERE rolname = current_user) \gexec

-- ---------- database ----------
SELECT 'CREATE DATABASE wgulti OWNER wgulti_owner ENCODING ''UTF8'' TEMPLATE template0'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'wgulti') \gexec

-- database-level grants are made as the owner (works for RDS master and superuser)
SET ROLE wgulti_owner;
REVOKE ALL ON DATABASE wgulti FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE wgulti TO wgulti_app;
GRANT CONNECT ON DATABASE wgulti TO wgulti_readonly;
SELECT 'GRANT CONNECT, TEMPORARY ON DATABASE wgulti TO ' || quote_ident(session_user) \gexec
ALTER DATABASE wgulti SET timezone = 'UTC';
RESET ROLE;

-- Per-role session defaults inside wgulti
ALTER ROLE wgulti_app      IN DATABASE wgulti SET search_path = lti, public;
ALTER ROLE wgulti_app      IN DATABASE wgulti SET statement_timeout = '30s';
ALTER ROLE wgulti_app      IN DATABASE wgulti SET lock_timeout = '5s';
ALTER ROLE wgulti_app      IN DATABASE wgulti SET idle_in_transaction_session_timeout = '60s';
ALTER ROLE wgulti_readonly IN DATABASE wgulti SET search_path = lti, public;
ALTER ROLE wgulti_readonly IN DATABASE wgulti SET default_transaction_read_only = on;
ALTER ROLE wgulti_readonly IN DATABASE wgulti SET statement_timeout = '120s';

-- Lock down the public schema inside the new database
\connect wgulti
SET ROLE wgulti_owner;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
RESET ROLE;

\echo 'wgulti database and roles ready. Next: V001__baseline.sql'
