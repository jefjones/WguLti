-- =====================================================================
-- V002__seed_reference.sql   Reference data only: no secrets, no users,
-- no tenants. Re-runnable (ON CONFLICT DO NOTHING / DO UPDATE).
-- =====================================================================
SET ROLE wgulti_owner;
SET search_path = lti, public;

-- ---------- LIS v2 role vocabulary (LTI 1.3 Core, Appendix A) ----------
INSERT INTO lis_roles(uri, short_name, kind, parent_uri, is_core) VALUES
 -- context (membership) roles
 ('http://purl.imsglobal.org/vocab/lis/v2/membership#Administrator',    'Administrator',    'context', NULL, true),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership#ContentDeveloper', 'ContentDeveloper', 'context', NULL, true),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership#Instructor',       'Instructor',       'context', NULL, true),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership#Learner',          'Learner',          'context', NULL, true),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership#Mentor',           'Mentor',           'context', NULL, true),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership#Manager',          'Manager',          'context', NULL, false),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership#Member',           'Member',           'context', NULL, false),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership#Officer',          'Officer',          'context', NULL, false)
ON CONFLICT (uri) DO NOTHING;

INSERT INTO lis_roles(uri, short_name, kind, parent_uri, is_core) VALUES
 ('http://purl.imsglobal.org/vocab/lis/v2/membership/Instructor#TeachingAssistant', 'Instructor#TeachingAssistant', 'context_sub', 'http://purl.imsglobal.org/vocab/lis/v2/membership#Instructor', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership/Instructor#PrimaryInstructor', 'Instructor#PrimaryInstructor', 'context_sub', 'http://purl.imsglobal.org/vocab/lis/v2/membership#Instructor', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership/Instructor#Grader',            'Instructor#Grader',            'context_sub', 'http://purl.imsglobal.org/vocab/lis/v2/membership#Instructor', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership/Instructor#Lecturer',          'Instructor#Lecturer',          'context_sub', 'http://purl.imsglobal.org/vocab/lis/v2/membership#Instructor', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership/Learner#Learner',              'Learner#Learner',              'context_sub', 'http://purl.imsglobal.org/vocab/lis/v2/membership#Learner', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership/Learner#NonCreditLearner',     'Learner#NonCreditLearner',     'context_sub', 'http://purl.imsglobal.org/vocab/lis/v2/membership#Learner', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership/Mentor#Advisor',               'Mentor#Advisor',               'context_sub', 'http://purl.imsglobal.org/vocab/lis/v2/membership#Mentor', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership/Mentor#Auditor',               'Mentor#Auditor',               'context_sub', 'http://purl.imsglobal.org/vocab/lis/v2/membership#Mentor', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/membership/ContentDeveloper#ContentDeveloper', 'ContentDeveloper#ContentDeveloper', 'context_sub', 'http://purl.imsglobal.org/vocab/lis/v2/membership#ContentDeveloper', false)
ON CONFLICT (uri) DO NOTHING;

INSERT INTO lis_roles(uri, short_name, kind, is_core) VALUES
 -- institution roles
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Administrator',      'Administrator',      'institution', true),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Faculty',            'Faculty',            'institution', true),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Guest',              'Guest',              'institution', true),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#None',               'None',               'institution', true),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Other',              'Other',              'institution', true),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Staff',              'Staff',              'institution', true),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Student',            'Student',            'institution', true),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Alumni',             'Alumni',             'institution', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Instructor',         'Instructor',         'institution', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Learner',            'Learner',            'institution', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Member',             'Member',             'institution', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Mentor',             'Mentor',             'institution', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#Observer',           'Observer',           'institution', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/institution/person#ProspectiveStudent', 'ProspectiveStudent', 'institution', false),
 -- system roles
 ('http://purl.imsglobal.org/vocab/lis/v2/system/person#Administrator', 'Administrator', 'system', true),
 ('http://purl.imsglobal.org/vocab/lis/v2/system/person#None',          'None',          'system', true),
 ('http://purl.imsglobal.org/vocab/lis/v2/system/person#AccountAdmin',  'AccountAdmin',  'system', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/system/person#Creator',       'Creator',       'system', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/system/person#SysAdmin',      'SysAdmin',      'system', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/system/person#SysSupport',    'SysSupport',    'system', false),
 ('http://purl.imsglobal.org/vocab/lis/v2/system/person#User',          'User',          'system', false),
 ('http://purl.imsglobal.org/vocab/lti/system/person#TestUser',         'TestUser',      'system', false)
ON CONFLICT (uri) DO NOTHING;

-- ---------- permission catalog ----------
INSERT INTO permissions(key, category, applies_to, description) VALUES
 ('manage_account_settings',   'account', 'account', 'Edit account settings and sub-accounts'),
 ('manage_users',              'people',  'both',    'Create, edit and suspend users'),
 ('manage_sso',                'auth',    'account', 'Configure identity providers (SAML/OIDC)'),
 ('manage_api_tokens',         'auth',    'account', 'Issue and revoke admin API tokens'),
 ('manage_roles',              'account', 'account', 'Create custom roles and set permissions'),
 ('become_user',               'people',  'account', 'Act as (masquerade) another user'),
 ('manage_lti_registrations',  'lti',     'account', 'Create/edit tool registrations (developer keys) and approve scopes'),
 ('manage_lti_deployments',    'lti',     'both',    'Install, enable and remove tool deployments'),
 ('manage_lti_placements',     'lti',     'both',    'Show/hide tool placements in navigation'),
 ('launch_lti_tools',          'lti',     'both',    'Launch LTI tools'),
 ('view_lti_audit',            'lti',     'account', 'View LTI launch/token/score audit log'),
 ('manage_courses',            'course',  'both',    'Create, edit, publish and conclude courses'),
 ('manage_sections',           'course',  'course',  'Create sections and move enrollments'),
 ('manage_enrollments',        'course',  'both',    'Add and remove enrollments'),
 ('manage_groups',             'course',  'course',  'Create groups and group categories'),
 ('manage_content',            'course',  'course',  'Edit modules, module items and pages'),
 ('manage_assignments',        'course',  'course',  'Create and edit assignments'),
 ('read_roster',               'course',  'course',  'See the course roster'),
 ('view_all_grades',           'grades',  'course',  'See every student''s grades'),
 ('manage_grades',             'grades',  'course',  'Enter, change and post grades'),
 ('view_grade_history',        'grades',  'course',  'See grade change history'),
 ('submit_assignments',        'grades',  'course',  'Submit work to assignments'),
 ('view_own_grades',           'grades',  'course',  'See own posted grades')
ON CONFLICT (key) DO UPDATE SET category = EXCLUDED.category, applies_to = EXCLUDED.applies_to,
                                description = EXCLUDED.description;

-- ---------- built-in roles (tenant_id NULL), Canvas base types ----------
INSERT INTO roles(name, base_role_type, lis_role_uri, is_built_in) VALUES
 ('AccountAdmin',  'AccountMembership',  'http://purl.imsglobal.org/vocab/lis/v2/institution/person#Administrator', true),
 ('Student',       'StudentEnrollment',  'http://purl.imsglobal.org/vocab/lis/v2/membership#Learner', true),
 ('Teacher',       'TeacherEnrollment',  'http://purl.imsglobal.org/vocab/lis/v2/membership#Instructor', true),
 ('TA',            'TaEnrollment',       'http://purl.imsglobal.org/vocab/lis/v2/membership/Instructor#TeachingAssistant', true),
 ('Designer',      'DesignerEnrollment', 'http://purl.imsglobal.org/vocab/lis/v2/membership#ContentDeveloper', true),
 ('Observer',      'ObserverEnrollment', 'http://purl.imsglobal.org/vocab/lis/v2/membership#Mentor', true)
ON CONFLICT DO NOTHING;

-- ---------- default permissions for built-in roles ----------
INSERT INTO role_permissions(role_id, permission_key, enabled, locked)
SELECT r.id, p.key, true, false
FROM roles r
JOIN (VALUES
  ('AccountAdmin', 'manage_account_settings'), ('AccountAdmin', 'manage_users'), ('AccountAdmin', 'manage_sso'),
  ('AccountAdmin', 'manage_api_tokens'), ('AccountAdmin', 'manage_roles'), ('AccountAdmin', 'become_user'),
  ('AccountAdmin', 'manage_lti_registrations'), ('AccountAdmin', 'manage_lti_deployments'),
  ('AccountAdmin', 'manage_lti_placements'), ('AccountAdmin', 'launch_lti_tools'), ('AccountAdmin', 'view_lti_audit'),
  ('AccountAdmin', 'manage_courses'), ('AccountAdmin', 'manage_sections'), ('AccountAdmin', 'manage_enrollments'),
  ('AccountAdmin', 'manage_groups'), ('AccountAdmin', 'manage_content'), ('AccountAdmin', 'manage_assignments'),
  ('AccountAdmin', 'read_roster'), ('AccountAdmin', 'view_all_grades'), ('AccountAdmin', 'manage_grades'),
  ('AccountAdmin', 'view_grade_history'),
  ('Teacher', 'manage_lti_deployments'), ('Teacher', 'manage_lti_placements'), ('Teacher', 'launch_lti_tools'),
  ('Teacher', 'manage_sections'), ('Teacher', 'manage_enrollments'), ('Teacher', 'manage_groups'),
  ('Teacher', 'manage_content'), ('Teacher', 'manage_assignments'), ('Teacher', 'read_roster'),
  ('Teacher', 'view_all_grades'), ('Teacher', 'manage_grades'), ('Teacher', 'view_grade_history'),
  ('TA', 'launch_lti_tools'), ('TA', 'manage_groups'), ('TA', 'read_roster'), ('TA', 'view_all_grades'),
  ('TA', 'manage_grades'),
  ('Designer', 'launch_lti_tools'), ('Designer', 'manage_lti_placements'), ('Designer', 'manage_content'),
  ('Designer', 'manage_assignments'), ('Designer', 'read_roster'),
  ('Student', 'launch_lti_tools'), ('Student', 'submit_assignments'), ('Student', 'view_own_grades'),
  ('Student', 'read_roster'),
  ('Observer', 'launch_lti_tools'), ('Observer', 'view_own_grades')
) AS m(role_name, perm) ON m.role_name = r.name AND r.is_built_in
JOIN permissions p ON p.key = m.perm
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations(version, description, script)
VALUES ('002', 'seed reference data', 'V002__seed_reference.sql') ON CONFLICT (version) DO NOTHING;

RESET ROLE;
