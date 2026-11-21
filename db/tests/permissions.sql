-- Run as wgulti_app. Every statement here MUST fail with insufficient_privilege.
SET search_path = lti, public;
DO $$
DECLARE stmt text; ok int := 0;
BEGIN
  FOREACH stmt IN ARRAY ARRAY[
    'CREATE TABLE lti.hack(id int)',
    'CREATE TABLE public.hack(id int)',
    'DROP TABLE lti.users',
    'ALTER TABLE lti.users ADD COLUMN x int',
    'TRUNCATE lti.audit_log',
    'CREATE INDEX ix_hack ON lti.users(email)',
    'UPDATE lti.audit_log SET event = ''x''',
    'DELETE FROM lti.audit_log_default',
    'DELETE FROM lti.grade_changes',
    'UPDATE lti.scores SET score_given = 0',
    'INSERT INTO lti.lis_roles(uri,short_name,kind) VALUES (''x'',''x'',''system'')',
    'DELETE FROM lti.schema_migrations',
    'SELECT lti.drop_audit_partitions_older_than(0)',
    'CREATE SCHEMA hack',
    'SET ROLE wgulti_owner',
    'ALTER ROLE wgulti_app CREATEDB'
  ] LOOP
    BEGIN
      EXECUTE stmt;
      RAISE EXCEPTION 'NOT DENIED: %', stmt;
    EXCEPTION WHEN insufficient_privilege THEN ok := ok + 1;
    END;
  END LOOP;
  RAISE NOTICE 'PERMISSIONS OK: % DDL/protected statements denied for %', ok, current_user;
END $$;
