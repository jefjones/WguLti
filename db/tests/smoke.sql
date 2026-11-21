-- Smoke test. Run as wgulti_app:  psql -U wgulti_app -d wgulti -v ON_ERROR_STOP=1 -f tests/smoke.sql
-- Everything runs in one transaction and is rolled back.
BEGIN;
SET search_path = lti, public;
DO $$
DECLARE
  t uuid; acct uuid; idp uuid; stu uuid; tch uuid; lg uuid; ctx uuid; sec uuid; grp uuid; gcat uuid;
  ag uuid; asg uuid; mod uuid; reg uuid; dep uuid; rl uuid; li uuid; cons uuid;
  r_student uuid; r_teacher uuid; n int; s record;
BEGIN
  SELECT id INTO r_student FROM roles WHERE name = 'Student' AND is_built_in;
  SELECT id INTO r_teacher FROM roles WHERE name = 'Teacher' AND is_built_in;
  ASSERT r_student IS NOT NULL AND r_teacher IS NOT NULL, 'built-in roles seeded';

  INSERT INTO tenants(slug,name,issuer,platform_guid) VALUES ('wgu','WGU','https://lti.wgu.local','wgu-guid') RETURNING id INTO t;
  INSERT INTO accounts(tenant_id,name) VALUES (t,'Root') RETURNING id INTO acct;
  INSERT INTO identity_providers(tenant_id,kind,name,is_default) VALUES (t,'local','Local',true) RETURNING id INTO idp;
  INSERT INTO users(tenant_id,email,given_name,family_name) VALUES (t,'sam@wgu.edu','Sam','Student') RETURNING id INTO stu;
  INSERT INTO users(tenant_id,email,given_name,family_name) VALUES (t,'tia@wgu.edu','Tia','Teacher') RETURNING id INTO tch;
  INSERT INTO user_logins(user_id,identity_provider_id,unique_id,password_hash) VALUES (stu,idp,'sam','AQAAAAIAAYagAAAAE...') RETURNING id INTO lg;
  INSERT INTO user_sessions(user_id,user_login_id,session_token_hash,expires_at) VALUES (stu,lg,sha256('cookie'),now()+interval '8 hours');
  INSERT INTO api_tokens(tenant_id,user_id,name,token_prefix,token_hash) VALUES (t,tch,'ci','wgl_ab',sha256('tok'));

  INSERT INTO contexts(tenant_id,account_id,course_code,title,workflow_state) VALUES (t,acct,'C950','Data Structures II','available') RETURNING id INTO ctx;
  INSERT INTO course_sections(context_id,name,is_default) VALUES (ctx,'Section 1',true) RETURNING id INTO sec;
  INSERT INTO enrollments(context_id,section_id,user_id,role_id) VALUES (ctx,sec,stu,r_student),(ctx,sec,tch,r_teacher);
  SELECT count(*) INTO n FROM enrollment_changes WHERE context_id = ctx; ASSERT n = 2, 'enrollment_changes logged adds';
  UPDATE enrollments SET status = 'inactive' WHERE user_id = stu;
  SELECT count(*) INTO n FROM enrollment_changes WHERE context_id = ctx AND change = 'updated'; ASSERT n = 1, 'status change logged';
  UPDATE enrollments SET status = 'active' WHERE user_id = stu;

  INSERT INTO group_categories(context_id,name) VALUES (ctx,'Project teams') RETURNING id INTO gcat;
  INSERT INTO groups(context_id,category_id,name) VALUES (ctx,gcat,'Team A') RETURNING id INTO grp;
  INSERT INTO group_memberships(group_id,user_id,is_leader) VALUES (grp,stu,true);

  INSERT INTO tool_registrations(tenant_id,owner_account_id,client_id,name,tool_domain,oidc_initiation_url,target_link_uri,redirect_uris,jwks_uri,scopes,state)
  VALUES (t,acct,'10000000000001','Demo Tool','tool.local','https://tool.local/login','https://tool.local/launch',
          ARRAY['https://tool.local/launch'],'https://tool.local/jwks',ARRAY['https://purl.imsglobal.org/spec/lti-ags/scope/score'],'on') RETURNING id INTO reg;
  INSERT INTO placements(registration_id,placement,label) VALUES (reg,'assignment_selection','Demo Tool');
  INSERT INTO deployments(registration_id,deployment_id,context_id) VALUES (reg,'1:c950',ctx) RETURNING id INTO dep;
  INSERT INTO lti_user_ids(registration_id,user_id,sub) VALUES (reg,stu,'opaque-123');

  INSERT INTO lti1p1_consumers(tenant_id,registration_id,oauth_consumer_key,secret_ref)
  VALUES (t,reg,'legacy-key','arn:aws:secretsmanager:us-west-2:000000000000:secret:wgulti/lti11/demo') RETURNING id INTO cons;
  INSERT INTO lti1p1_id_map(consumer_id,entity_type,entity_id,legacy_id) VALUES (cons,'user',stu,'535fa2d9...'),(cons,'context',ctx,'c950-legacy');

  INSERT INTO assignment_groups(context_id,name,weight) VALUES (ctx,'Practice',20) RETURNING id INTO ag;
  INSERT INTO assignments(context_id,assignment_group_id,title,points_possible,submission_types,due_at,published)
  VALUES (ctx,ag,'Practice set 3',100,ARRAY['external_tool'],now()+interval '7 days',true) RETURNING id INTO asg;
  INSERT INTO resource_links(deployment_id,context_id,assignment_id,link_id,title) VALUES (dep,ctx,asg,'rl_1','Practice set 3') RETURNING id INTO rl;
  INSERT INTO modules(context_id,name,published) VALUES (ctx,'Week 1',true) RETURNING id INTO mod;
  INSERT INTO module_items(module_id,item_type,title,assignment_id,resource_link_id,published) VALUES (mod,'external_tool','Practice set 3',asg,rl,true);
  INSERT INTO line_items(context_id,deployment_id,assignment_id,resource_link_id,label,score_maximum) VALUES (ctx,dep,asg,rl,'Practice set 3',10) RETURNING id INTO li;

  -- AGS: 8/10 fully graded -> result 8/10 -> submission 80/100
  INSERT INTO scores(line_item_id,user_id,score_given,score_maximum,activity_progress,grading_progress,tool_timestamp,posted_by_registration_id)
  VALUES (li,stu,8,10,'Completed','FullyGraded',now()-interval '10 min',reg);
  SELECT * INTO s FROM submissions WHERE assignment_id = asg AND user_id = stu;
  ASSERT s.score = 80 AND s.grade = '80' AND s.workflow_state = 'graded' AND s.grade_source = 'lti_ags', 'AGS -> submission sync';
  ASSERT s.grader_registration_id = reg, 'grader is the tool';

  -- stale score (older timestamp) ignored
  INSERT INTO scores(line_item_id,user_id,score_given,score_maximum,activity_progress,grading_progress,tool_timestamp)
  VALUES (li,stu,1,10,'Completed','FullyGraded',now()-interval '1 hour');
  SELECT score INTO s FROM submissions WHERE assignment_id = asg AND user_id = stu; ASSERT s.score = 80, 'stale score ignored';

  -- score on a different scale is normalised: 45/50 -> 9/10 -> 90/100
  INSERT INTO scores(line_item_id,user_id,score_given,score_maximum,activity_progress,grading_progress,tool_timestamp)
  VALUES (li,stu,45,50,'Completed','FullyGraded',now()-interval '5 min');
  SELECT score INTO s FROM submissions WHERE assignment_id = asg AND user_id = stu; ASSERT s.score = 90, 'normalised score';

  -- PendingManual keeps score, flips state
  INSERT INTO scores(line_item_id,user_id,activity_progress,grading_progress,tool_timestamp)
  VALUES (li,stu,'Submitted','PendingManual',now()-interval '4 min');
  SELECT * INTO s FROM submissions WHERE assignment_id = asg AND user_id = stu;
  ASSERT s.workflow_state = 'pending_review' AND s.score = 90, 'PendingManual handling';

  -- instructor override wins over later AGS scores
  UPDATE submissions SET score = 95, grade = '95', instructor_override = true, grader_user_id = tch,
         grader_registration_id = NULL, grade_source = 'manual', workflow_state = 'graded', graded_at = now()
   WHERE assignment_id = asg AND user_id = stu;
  INSERT INTO scores(line_item_id,user_id,score_given,score_maximum,activity_progress,grading_progress,tool_timestamp)
  VALUES (li,stu,2,10,'Completed','FullyGraded',now()-interval '1 min');
  SELECT score INTO s FROM submissions WHERE assignment_id = asg AND user_id = stu; ASSERT s.score = 95, 'override respected';
  SELECT result_score INTO s FROM results WHERE line_item_id = li AND user_id = stu;
  ASSERT s.result_score = 2, 'AGS result still reflects latest tool score';

  SELECT count(*) INTO n FROM grade_changes WHERE assignment_id = asg; ASSERT n = 3, 'grade history: 80, 90, 95 (got ' || n || ')';

  -- audit partitions via SECURITY DEFINER helper (app has no DDL)
  PERFORM ensure_audit_partitions(1);
  INSERT INTO audit_log(tenant_id,event,details) VALUES (t,'score.posted','{"ok":true}');
  SELECT count(*) INTO n FROM ONLY audit_log_default; ASSERT n = 0, 'audit row routed to monthly partition';
  PERFORM purge_expired();

  -- constraints
  BEGIN
    INSERT INTO deployments(registration_id,deployment_id,account_id,context_id) VALUES (reg,'bad',acct,ctx);
    RAISE EXCEPTION 'deployment scope CHECK did not fire';
  EXCEPTION WHEN check_violation THEN NULL; END;
  BEGIN
    INSERT INTO module_items(module_id,item_type,title) VALUES (mod,'external_tool','no link');
    RAISE EXCEPTION 'module_items CHECK did not fire';
  EXCEPTION WHEN check_violation THEN NULL; END;
  BEGIN
    INSERT INTO assignments(context_id,title,submission_types) VALUES (ctx,'bad',ARRAY['carrier_pigeon']);
    RAISE EXCEPTION 'submission_types CHECK did not fire';
  EXCEPTION WHEN check_violation THEN NULL; END;

  RAISE NOTICE 'SMOKE OK: % tables in lti', (SELECT count(*) FROM information_schema.tables WHERE table_schema='lti' AND table_type='BASE TABLE');
END $$;
ROLLBACK;
