-- Run as wgulti_readonly.
SET search_path = lti, public;
DO $$
BEGIN
  PERFORM count(*) FROM lti.contexts;
  PERFORM count(*) FROM lti.submissions;
  BEGIN PERFORM count(*) FROM lti.user_logins; RAISE EXCEPTION 'readonly can read user_logins';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN PERFORM count(*) FROM lti.platform_keys; RAISE EXCEPTION 'readonly can read platform_keys';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN INSERT INTO lti.tenants(slug,name,issuer,platform_guid) VALUES ('x','x','x','x'); RAISE EXCEPTION 'readonly can write';
  EXCEPTION WHEN insufficient_privilege OR read_only_sql_transaction THEN NULL; END;
  RAISE NOTICE 'READONLY OK';
END $$;
