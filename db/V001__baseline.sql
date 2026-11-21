-- =====================================================================
-- V001__baseline.sql   WguLti LTI 1.3 / LTI Advantage Platform
-- Database: wgulti   Schema: lti   Requires PostgreSQL 18+ (uuidv7()).
--
-- Pure SQL (no psql meta-commands) so Flyway, DbUp or psql can run it.
-- psql:   psql -d wgulti -v ON_ERROR_STOP=1 --single-transaction -f V001__baseline.sql
-- Flyway/DbUp wrap it in their own transaction.
-- Re-runnable: IF NOT EXISTS / CREATE OR REPLACE everywhere.
-- Connect as the admin that 000_create_database.sql granted wgulti_owner.
-- =====================================================================

DO $$ BEGIN
  IF current_setting('server_version_num')::int < 180000 THEN
    RAISE EXCEPTION 'WguLti requires PostgreSQL 18+ (found %)', current_setting('server_version');
  END IF;
END $$;

SET ROLE wgulti_owner;                 -- every object below is owned by wgulti_owner
SET client_min_messages = warning;

CREATE EXTENSION IF NOT EXISTS citext;  -- trusted extension: db owner may create it
CREATE SCHEMA IF NOT EXISTS lti AUTHORIZATION wgulti_owner;
SET search_path = lti, public;

-- ---------------------------------------------------------------------
-- 0. migration bookkeeping (works alongside flyway_schema_history / schemaversions)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS schema_migrations (
  version     text PRIMARY KEY,
  description text NOT NULL,
  script      text NOT NULL,
  applied_at  timestamptz NOT NULL DEFAULT now(),
  applied_by  text NOT NULL DEFAULT session_user
);

CREATE OR REPLACE FUNCTION lti.touch_updated_at() RETURNS trigger
LANGUAGE plpgsql AS $$ BEGIN NEW.updated_at := now(); RETURN NEW; END $$;

-- =====================================================================
-- 1. REFERENCE VOCABULARIES (seeded in V002)
-- =====================================================================
CREATE TABLE IF NOT EXISTS lis_roles (
  uri         text PRIMARY KEY,                        -- full LIS v2 role URI
  short_name  text NOT NULL,
  kind        text NOT NULL CHECK (kind IN ('context','context_sub','institution','system')),
  parent_uri  text REFERENCES lis_roles(uri),
  is_core     boolean NOT NULL DEFAULT false
);

CREATE TABLE IF NOT EXISTS permissions (
  key         text PRIMARY KEY,
  category    text NOT NULL,
  applies_to  text NOT NULL CHECK (applies_to IN ('account','course','both')),
  description text NOT NULL
);

-- =====================================================================
-- 2. TENANCY
-- =====================================================================
CREATE TABLE IF NOT EXISTS tenants (
  id            uuid PRIMARY KEY DEFAULT uuidv7(),
  slug          citext NOT NULL UNIQUE,
  name          text   NOT NULL,
  issuer        text   NOT NULL UNIQUE,                 -- iss claim
  platform_guid text   NOT NULL UNIQUE,                 -- tool_platform.guid
  default_time_zone text NOT NULL DEFAULT 'America/Denver',
  status        text   NOT NULL DEFAULT 'active' CHECK (status IN ('active','suspended')),
  created_at    timestamptz NOT NULL DEFAULT now(),
  updated_at    timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS accounts (
  id                uuid PRIMARY KEY DEFAULT uuidv7(),
  tenant_id         uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  parent_account_id uuid REFERENCES accounts(id) ON DELETE RESTRICT,
  name              text NOT NULL,
  sis_id            text,
  path              uuid[] NOT NULL DEFAULT '{}',       -- ancestors, root first
  created_at        timestamptz NOT NULL DEFAULT now(),
  updated_at        timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, sis_id),
  CHECK (parent_account_id IS DISTINCT FROM id)
);
CREATE INDEX IF NOT EXISTS ix_accounts_tenant ON accounts(tenant_id);
CREATE INDEX IF NOT EXISTS ix_accounts_parent ON accounts(parent_account_id);
CREATE INDEX IF NOT EXISTS ix_accounts_path   ON accounts USING gin(path);

-- =====================================================================
-- 3. PLATFORM AUTHENTICATION
-- =====================================================================
CREATE TABLE IF NOT EXISTS identity_providers (
  id                uuid PRIMARY KEY DEFAULT uuidv7(),
  tenant_id         uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  kind              text NOT NULL CHECK (kind IN ('local','saml','oidc')),
  name              text NOT NULL,
  -- non-secret settings: SAML idp metadata url/entity id/cert fingerprints,
  -- OIDC authority, client_id, scopes, claim names ...
  config            jsonb NOT NULL DEFAULT '{}'::jsonb,
  secret_ref        text,                               -- AWS Secrets Manager ARN for client secret / SP key
  attribute_map     jsonb NOT NULL DEFAULT '{}'::jsonb, -- idp attribute -> users column
  jit_provisioning  boolean NOT NULL DEFAULT false,
  is_default        boolean NOT NULL DEFAULT false,
  position          int NOT NULL DEFAULT 0,
  enabled           boolean NOT NULL DEFAULT true,
  created_at        timestamptz NOT NULL DEFAULT now(),
  updated_at        timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, name),
  CHECK (kind <> 'oidc' OR secret_ref IS NOT NULL)   -- confidential OIDC client needs a secret
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_idp_one_default ON identity_providers(tenant_id) WHERE is_default;

CREATE TABLE IF NOT EXISTS users (
  id           uuid PRIMARY KEY DEFAULT uuidv7(),
  tenant_id    uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  sis_id       text,
  email        citext,
  given_name   text,
  family_name  text,
  display_name text,
  sortable_name text,
  locale       text NOT NULL DEFAULT 'en-US',
  time_zone    text,
  picture_url  text,
  workflow_state text NOT NULL DEFAULT 'active' CHECK (workflow_state IN ('pre_registered','active','suspended','deleted')),
  created_at   timestamptz NOT NULL DEFAULT now(),
  updated_at   timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, sis_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON users(tenant_id, email) WHERE workflow_state <> 'deleted';

CREATE TABLE IF NOT EXISTS user_logins (
  id                   uuid PRIMARY KEY DEFAULT uuidv7(),
  user_id              uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  identity_provider_id uuid NOT NULL REFERENCES identity_providers(id) ON DELETE RESTRICT,
  unique_id            text NOT NULL,                  -- username / SAML NameID / OIDC sub
  password_hash        text,                           -- local only: ASP.NET Identity v3 / Argon2id string
  password_changed_at  timestamptz,
  mfa_secret_ref       text,                           -- Secrets Manager ref, never the TOTP seed itself
  mfa_required         boolean NOT NULL DEFAULT false,
  failed_attempts      int NOT NULL DEFAULT 0 CHECK (failed_attempts >= 0),
  locked_until         timestamptz,
  last_login_at        timestamptz,
  last_login_ip        inet,
  workflow_state       text NOT NULL DEFAULT 'active' CHECK (workflow_state IN ('active','suspended','deleted')),
  created_at           timestamptz NOT NULL DEFAULT now(),
  updated_at           timestamptz NOT NULL DEFAULT now(),
  UNIQUE (identity_provider_id, unique_id)
);
CREATE INDEX IF NOT EXISTS ix_user_logins_user ON user_logins(user_id);

CREATE TABLE IF NOT EXISTS user_sessions (
  id                 uuid PRIMARY KEY DEFAULT uuidv7(),
  user_id            uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  user_login_id      uuid REFERENCES user_logins(id) ON DELETE CASCADE,
  session_token_hash bytea NOT NULL UNIQUE,            -- SHA-256 of cookie value
  masquerading_user_id uuid REFERENCES users(id) ON DELETE CASCADE,  -- "act as" (admin)
  mfa_verified       boolean NOT NULL DEFAULT false,
  ip                 inet,
  user_agent         text,
  created_at         timestamptz NOT NULL DEFAULT now(),
  last_seen_at       timestamptz NOT NULL DEFAULT now(),
  expires_at         timestamptz NOT NULL,
  revoked_at         timestamptz,
  CHECK (expires_at > created_at)
);
CREATE INDEX IF NOT EXISTS ix_user_sessions_user ON user_sessions(user_id) WHERE revoked_at IS NULL;
CREATE INDEX IF NOT EXISTS ix_user_sessions_expiry ON user_sessions(expires_at);

CREATE TABLE IF NOT EXISTS api_tokens (                 -- admin / integration personal access tokens
  id            uuid PRIMARY KEY DEFAULT uuidv7(),
  tenant_id     uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  user_id       uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  name          text NOT NULL,
  token_prefix  text NOT NULL,                         -- first chars shown in UI
  token_hash    bytea NOT NULL UNIQUE,
  scopes        text[] NOT NULL DEFAULT '{}',
  expires_at    timestamptz,
  last_used_at  timestamptz,
  last_used_ip  inet,
  revoked_at    timestamptz,
  created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_api_tokens_user ON api_tokens(user_id) WHERE revoked_at IS NULL;

-- =====================================================================
-- 4. ROLES & PERMISSIONS (Canvas-style built-in + custom roles)
-- =====================================================================
CREATE TABLE IF NOT EXISTS roles (
  id              uuid PRIMARY KEY DEFAULT uuidv7(),
  tenant_id       uuid REFERENCES tenants(id) ON DELETE CASCADE,  -- NULL = built-in, all tenants
  account_id      uuid REFERENCES accounts(id) ON DELETE CASCADE, -- where a custom role is defined
  name            text NOT NULL,
  base_role_type  text NOT NULL CHECK (base_role_type IN
                  ('AccountMembership','StudentEnrollment','TeacherEnrollment','TaEnrollment',
                   'DesignerEnrollment','ObserverEnrollment')),
  lis_role_uri    text REFERENCES lis_roles(uri),                 -- what roles claim emits
  is_built_in     boolean NOT NULL DEFAULT false,
  workflow_state  text NOT NULL DEFAULT 'active' CHECK (workflow_state IN ('active','inactive','deleted')),
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now(),
  CHECK (is_built_in = (tenant_id IS NULL))
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_name ON roles(COALESCE(tenant_id,'00000000-0000-0000-0000-000000000000'::uuid), name);

CREATE TABLE IF NOT EXISTS role_permissions (
  role_id        uuid NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
  permission_key text NOT NULL REFERENCES permissions(key) ON DELETE CASCADE,
  account_id     uuid REFERENCES accounts(id) ON DELETE CASCADE,  -- NULL = default for role
  enabled        boolean NOT NULL,
  locked         boolean NOT NULL DEFAULT false,                  -- sub-accounts cannot override
  updated_at     timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_role_permissions ON role_permissions(role_id, permission_key,
  COALESCE(account_id,'00000000-0000-0000-0000-000000000000'::uuid));

CREATE TABLE IF NOT EXISTS account_users (               -- admins of an account (sub-account)
  id          uuid PRIMARY KEY DEFAULT uuidv7(),
  account_id  uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  user_id     uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  role_id     uuid NOT NULL REFERENCES roles(id) ON DELETE RESTRICT,
  created_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (account_id, user_id, role_id)
);
CREATE INDEX IF NOT EXISTS ix_account_users_user ON account_users(user_id);

-- =====================================================================
-- 5. COURSES, SECTIONS, ENROLLMENTS, GROUPS
-- =====================================================================
CREATE TABLE IF NOT EXISTS contexts (                    -- courses (LTI "context")
  id           uuid PRIMARY KEY DEFAULT uuidv7(),
  tenant_id    uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  account_id   uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
  context_type text NOT NULL DEFAULT 'CourseOffering'
               CHECK (context_type IN ('CourseTemplate','CourseOffering')),
  course_code  text NOT NULL,                            -- context.label, e.g. C950
  title        text NOT NULL,
  sis_id       text,
  time_zone    text,
  starts_at    timestamptz,
  ends_at      timestamptz,
  workflow_state text NOT NULL DEFAULT 'unpublished'
               CHECK (workflow_state IN ('unpublished','available','completed','deleted')),
  created_at   timestamptz NOT NULL DEFAULT now(),
  updated_at   timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, sis_id),
  CHECK (ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at)
);
CREATE INDEX IF NOT EXISTS ix_contexts_account ON contexts(account_id);

CREATE TABLE IF NOT EXISTS course_sections (
  id          uuid PRIMARY KEY DEFAULT uuidv7(),
  context_id  uuid NOT NULL REFERENCES contexts(id) ON DELETE CASCADE,
  name        text NOT NULL,
  sis_id      text,
  starts_at   timestamptz,
  ends_at     timestamptz,
  is_default  boolean NOT NULL DEFAULT false,
  created_at  timestamptz NOT NULL DEFAULT now(),
  updated_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (context_id, sis_id)
);
CREATE INDEX IF NOT EXISTS ix_sections_context ON course_sections(context_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_sections_default ON course_sections(context_id) WHERE is_default;

CREATE TABLE IF NOT EXISTS enrollments (
  id          uuid PRIMARY KEY DEFAULT uuidv7(),
  context_id  uuid NOT NULL REFERENCES contexts(id) ON DELETE CASCADE,
  section_id  uuid REFERENCES course_sections(id) ON DELETE CASCADE,
  user_id     uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  role_id     uuid NOT NULL REFERENCES roles(id) ON DELETE RESTRICT,
  associated_user_id uuid REFERENCES users(id) ON DELETE CASCADE,   -- observer -> student (role_scope_mentor)
  status      text NOT NULL DEFAULT 'active' CHECK (status IN ('invited','active','inactive','completed','deleted')),
  sis_id      text,
  start_at    timestamptz,
  end_at      timestamptz,
  created_at  timestamptz NOT NULL DEFAULT now(),
  updated_at  timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_enrollments ON enrollments(context_id, user_id, role_id,
  COALESCE(section_id,'00000000-0000-0000-0000-000000000000'::uuid),
  COALESCE(associated_user_id,'00000000-0000-0000-0000-000000000000'::uuid));
CREATE INDEX IF NOT EXISTS ix_enrollments_user ON enrollments(user_id);
CREATE INDEX IF NOT EXISTS ix_enrollments_context_status ON enrollments(context_id, status);
CREATE INDEX IF NOT EXISTS ix_enrollments_section ON enrollments(section_id) WHERE section_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS enrollment_changes (          -- NRPS rel="differences"
  id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  context_id  uuid NOT NULL REFERENCES contexts(id) ON DELETE CASCADE,
  user_id     uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  change      text NOT NULL CHECK (change IN ('added','updated','removed')),
  changed_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_enrollment_changes_ctx_time ON enrollment_changes(context_id, changed_at);

CREATE OR REPLACE FUNCTION lti.log_enrollment_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'DELETE' THEN
    INSERT INTO lti.enrollment_changes(context_id, user_id, change) VALUES (OLD.context_id, OLD.user_id, 'removed');
    RETURN OLD;
  ELSIF TG_OP = 'INSERT' THEN
    INSERT INTO lti.enrollment_changes(context_id, user_id, change) VALUES (NEW.context_id, NEW.user_id, 'added');
  ELSIF NEW.status IS DISTINCT FROM OLD.status OR NEW.role_id IS DISTINCT FROM OLD.role_id
        OR NEW.section_id IS DISTINCT FROM OLD.section_id THEN
    INSERT INTO lti.enrollment_changes(context_id, user_id, change)
    VALUES (NEW.context_id, NEW.user_id, CASE WHEN NEW.status = 'deleted' THEN 'removed' ELSE 'updated' END);
  END IF;
  RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER trg_enrollments_changes AFTER INSERT OR UPDATE OR DELETE ON enrollments
  FOR EACH ROW EXECUTE FUNCTION lti.log_enrollment_change();

CREATE TABLE IF NOT EXISTS group_categories (
  id          uuid PRIMARY KEY DEFAULT uuidv7(),
  context_id  uuid NOT NULL REFERENCES contexts(id) ON DELETE CASCADE,
  name        text NOT NULL,
  self_signup text CHECK (self_signup IN ('enabled','restricted')),
  group_limit int CHECK (group_limit IS NULL OR group_limit > 0),
  created_at  timestamptz NOT NULL DEFAULT now(),
  UNIQUE (context_id, name)
);

CREATE TABLE IF NOT EXISTS groups (
  id           uuid PRIMARY KEY DEFAULT uuidv7(),
  context_id   uuid NOT NULL REFERENCES contexts(id) ON DELETE CASCADE,
  category_id  uuid REFERENCES group_categories(id) ON DELETE CASCADE,
  name         text NOT NULL,
  sis_id       text,
  workflow_state text NOT NULL DEFAULT 'available' CHECK (workflow_state IN ('available','deleted')),
  created_at   timestamptz NOT NULL DEFAULT now(),
  updated_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_groups_context ON groups(context_id);
CREATE INDEX IF NOT EXISTS ix_groups_category ON groups(category_id);

CREATE TABLE IF NOT EXISTS group_memberships (
  group_id    uuid NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
  user_id     uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  is_leader   boolean NOT NULL DEFAULT false,
  status      text NOT NULL DEFAULT 'accepted' CHECK (status IN ('invited','requested','accepted','deleted')),
  created_at  timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (group_id, user_id)
);
CREATE INDEX IF NOT EXISTS ix_group_memberships_user ON group_memberships(user_id);

-- =====================================================================
-- 6. PLATFORM SIGNING KEYS (private material in AWS KMS in prod)
-- =====================================================================
CREATE TABLE IF NOT EXISTS platform_keys (
  id            uuid PRIMARY KEY DEFAULT uuidv7(),
  tenant_id     uuid REFERENCES tenants(id) ON DELETE CASCADE,   -- NULL = shared
  kid           text NOT NULL UNIQUE,
  alg           text NOT NULL DEFAULT 'RS256' CHECK (alg IN ('RS256','RS384','RS512','ES256')),
  public_jwk    jsonb NOT NULL,
  kms_key_arn   text,
  private_pem_encrypted bytea,                                    -- dev only (DataProtection)
  status        text NOT NULL CHECK (status IN ('next','active','retiring','revoked')),
  activated_at  timestamptz,
  retire_after  timestamptz,
  remove_after  timestamptz,
  created_at    timestamptz NOT NULL DEFAULT now(),
  CHECK (kms_key_arn IS NOT NULL OR private_pem_encrypted IS NOT NULL)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_platform_keys_one_active
  ON platform_keys (COALESCE(tenant_id,'00000000-0000-0000-0000-000000000000'::uuid)) WHERE status = 'active';

-- =====================================================================
-- 7. TOOL REGISTRATIONS (developer keys) + config
-- =====================================================================
CREATE TABLE IF NOT EXISTS tool_registrations (
  id                    uuid PRIMARY KEY DEFAULT uuidv7(),
  tenant_id             uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  owner_account_id      uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
  client_id             text NOT NULL UNIQUE,
  name                  text NOT NULL,
  description           text,
  vendor                text,
  logo_uri              text,
  tool_domain           text NOT NULL,
  oidc_initiation_url   text NOT NULL CHECK (oidc_initiation_url ~ '^https://'),
  target_link_uri       text NOT NULL,
  redirect_uris         text[] NOT NULL CHECK (cardinality(redirect_uris) > 0),
  jwks_uri              text CHECK (jwks_uri IS NULL OR jwks_uri ~ '^https://'),
  public_jwk            jsonb,
  token_endpoint_auth_method text NOT NULL DEFAULT 'private_key_jwt' CHECK (token_endpoint_auth_method = 'private_key_jwt'),
  scopes                text[] NOT NULL DEFAULT '{}',
  privacy_level         text NOT NULL DEFAULT 'anonymous' CHECK (privacy_level IN ('anonymous','name_only','email_only','public')),
  claims_allowed        text[] NOT NULL DEFAULT '{}',
  registration_source   text NOT NULL DEFAULT 'manual' CHECK (registration_source IN ('manual','json_url','dynamic')),
  config_url            text,
  state                 text NOT NULL DEFAULT 'off' CHECK (state IN ('on','off','allow','deleted')),
  created_by            uuid REFERENCES users(id) ON DELETE SET NULL,
  created_at            timestamptz NOT NULL DEFAULT now(),
  updated_at            timestamptz NOT NULL DEFAULT now(),
  CHECK (jwks_uri IS NOT NULL OR public_jwk IS NOT NULL)
);
CREATE INDEX IF NOT EXISTS ix_tool_reg_tenant ON tool_registrations(tenant_id, state);

CREATE TABLE IF NOT EXISTS tool_jwks_cache (
  registration_id uuid PRIMARY KEY REFERENCES tool_registrations(id) ON DELETE CASCADE,
  jwks            jsonb NOT NULL,
  etag            text,
  fetched_at      timestamptz NOT NULL DEFAULT now(),
  expires_at      timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS registration_messages (
  id              uuid PRIMARY KEY DEFAULT uuidv7(),
  registration_id uuid NOT NULL REFERENCES tool_registrations(id) ON DELETE CASCADE,
  message_type    text NOT NULL CHECK (message_type IN
                  ('LtiResourceLinkRequest','LtiDeepLinkingRequest','LtiSubmissionReviewRequest',
                   'LtiStartProctoring','LtiEndAssessment','LtiAssetProcessorSettingsRequest','LtiReportReviewRequest')),
  target_link_uri text,
  UNIQUE (registration_id, message_type)
);

CREATE TABLE IF NOT EXISTS placements (
  id              uuid PRIMARY KEY DEFAULT uuidv7(),
  registration_id uuid NOT NULL REFERENCES tool_registrations(id) ON DELETE CASCADE,
  placement       text NOT NULL CHECK (placement IN
                  ('course_navigation','account_navigation','global_navigation','user_navigation',
                   'link_selection','assignment_selection','editor_button','homework_submission',
                   'module_menu','file_menu','submission_review','ai_assistant')),
  message_type    text NOT NULL DEFAULT 'LtiResourceLinkRequest',
  target_link_uri text,
  label           text NOT NULL,
  icon_url        text,
  window_target   text NOT NULL DEFAULT 'iframe' CHECK (window_target IN ('iframe','window','tray')),
  width           int CHECK (width IS NULL OR width BETWEEN 100 AND 4000),
  height          int CHECK (height IS NULL OR height BETWEEN 100 AND 4000),
  roles_allowed   text[] NOT NULL DEFAULT '{}',
  default_enabled boolean NOT NULL DEFAULT true,
  UNIQUE (registration_id, placement)
);

CREATE TABLE IF NOT EXISTS custom_params (
  id              uuid PRIMARY KEY DEFAULT uuidv7(),
  registration_id uuid NOT NULL REFERENCES tool_registrations(id) ON DELETE CASCADE,
  scope           text NOT NULL CHECK (scope IN ('registration','placement','deployment','resource_link')),
  scope_ref_id    uuid,
  name            text NOT NULL CHECK (name ~ '^[a-z0-9_]+$'),
  value           text NOT NULL,
  CHECK ((scope = 'registration') = (scope_ref_id IS NULL))
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_custom_params ON custom_params(registration_id, scope,
  COALESCE(scope_ref_id,'00000000-0000-0000-0000-000000000000'::uuid), name);

-- LTI 1.1 -> 1.3 migration: legacy consumer credentials per registration
CREATE TABLE IF NOT EXISTS lti1p1_consumers (
  id                 uuid PRIMARY KEY DEFAULT uuidv7(),
  tenant_id          uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  registration_id    uuid REFERENCES tool_registrations(id) ON DELETE SET NULL,
  oauth_consumer_key text NOT NULL,
  secret_ref         text NOT NULL,          -- Secrets Manager ARN; needed to compute oauth_consumer_key_sign
  launch_url         text,
  created_at         timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, oauth_consumer_key)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_lti1p1_consumer_reg ON lti1p1_consumers(registration_id) WHERE registration_id IS NOT NULL;

-- =====================================================================
-- 8. DEPLOYMENTS
-- =====================================================================
CREATE TABLE IF NOT EXISTS deployments (
  id              uuid PRIMARY KEY DEFAULT uuidv7(),
  registration_id uuid NOT NULL REFERENCES tool_registrations(id) ON DELETE CASCADE,
  deployment_id   text NOT NULL CHECK (length(deployment_id) <= 255),
  account_id      uuid REFERENCES accounts(id) ON DELETE CASCADE,
  context_id      uuid REFERENCES contexts(id) ON DELETE CASCADE,
  label           text,
  is_enabled      boolean NOT NULL DEFAULT true,
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now(),
  UNIQUE (registration_id, deployment_id),
  CHECK ((account_id IS NULL) <> (context_id IS NULL))
);
CREATE INDEX IF NOT EXISTS ix_deployments_account ON deployments(account_id) WHERE account_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_deployments_context ON deployments(context_id) WHERE context_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS context_placement_overrides (
  context_id   uuid NOT NULL REFERENCES contexts(id) ON DELETE CASCADE,
  placement_id uuid NOT NULL REFERENCES placements(id) ON DELETE CASCADE,
  enabled      boolean NOT NULL,
  position     int,
  PRIMARY KEY (context_id, placement_id)
);

-- =====================================================================
-- 9. LTI IDENTIFIERS: opaque per-tool sub + LTI 1.1 legacy id map
-- =====================================================================
CREATE TABLE IF NOT EXISTS lti_user_ids (
  registration_id uuid NOT NULL REFERENCES tool_registrations(id) ON DELETE CASCADE,
  user_id         uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  sub             text NOT NULL CHECK (length(sub) <= 255),
  created_at      timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (registration_id, user_id),
  UNIQUE (registration_id, sub)
);

-- Values for the https://purl.imsglobal.org/spec/lti/claim/lti1p1 claim
CREATE TABLE IF NOT EXISTS lti1p1_id_map (
  consumer_id  uuid NOT NULL REFERENCES lti1p1_consumers(id) ON DELETE CASCADE,
  entity_type  text NOT NULL CHECK (entity_type IN ('user','context','resource_link')),
  entity_id    uuid NOT NULL,                  -- users.id / contexts.id / resource_links.id
  legacy_id    text NOT NULL,                  -- user_id / context_id / resource_link_id sent by 1.1
  created_at   timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (consumer_id, entity_type, entity_id),
  UNIQUE (consumer_id, entity_type, legacy_id)
);

-- =====================================================================
-- 10. COURSE CONTENT: assignment groups, assignments, modules
-- =====================================================================
CREATE TABLE IF NOT EXISTS assignment_groups (
  id          uuid PRIMARY KEY DEFAULT uuidv7(),
  context_id  uuid NOT NULL REFERENCES contexts(id) ON DELETE CASCADE,
  name        text NOT NULL,
  weight      numeric(6,2) CHECK (weight IS NULL OR weight >= 0),
  position    int NOT NULL DEFAULT 0,
  drop_lowest int NOT NULL DEFAULT 0 CHECK (drop_lowest >= 0),
  created_at  timestamptz NOT NULL DEFAULT now(),
  updated_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_assignment_groups_ctx ON assignment_groups(context_id);

CREATE TABLE IF NOT EXISTS assignments (
  id                  uuid PRIMARY KEY DEFAULT uuidv7(),
  context_id          uuid NOT NULL REFERENCES contexts(id) ON DELETE CASCADE,
  assignment_group_id uuid REFERENCES assignment_groups(id) ON DELETE SET NULL,
  title               text NOT NULL,
  description         text,
  points_possible     numeric(12,4) CHECK (points_possible IS NULL OR points_possible >= 0),
  grading_type        text NOT NULL DEFAULT 'points'
                      CHECK (grading_type IN ('points','percent','pass_fail','letter_grade','not_graded')),
  submission_types    text[] NOT NULL DEFAULT '{none}'
                      CHECK (submission_types <@ ARRAY['none','on_paper','online_text_entry','online_url',
                             'online_upload','external_tool','discussion_topic','online_quiz']::text[]
                             AND cardinality(submission_types) > 0),
  due_at              timestamptz,
  unlock_at           timestamptz,
  lock_at             timestamptz,
  allowed_attempts    int CHECK (allowed_attempts IS NULL OR allowed_attempts = -1 OR allowed_attempts > 0),
  omit_from_final_grade boolean NOT NULL DEFAULT false,
  published           boolean NOT NULL DEFAULT false,
  position            int NOT NULL DEFAULT 0,
  sis_id              text,
  workflow_state      text NOT NULL DEFAULT 'active' CHECK (workflow_state IN ('active','deleted')),
  created_by_registration_id uuid REFERENCES tool_registrations(id) ON DELETE SET NULL,  -- AGS-created
  created_at          timestamptz NOT NULL DEFAULT now(),
  updated_at          timestamptz NOT NULL DEFAULT now(),
  CHECK (unlock_at IS NULL OR due_at IS NULL OR unlock_at <= due_at),
  CHECK (lock_at   IS NULL OR due_at IS NULL OR lock_at   >= due_at)
);
CREATE INDEX IF NOT EXISTS ix_assignments_ctx ON assignments(context_id) WHERE workflow_state = 'active';
CREATE INDEX IF NOT EXISTS ix_assignments_due ON assignments(context_id, due_at);

CREATE TABLE IF NOT EXISTS modules (
  id            uuid PRIMARY KEY DEFAULT uuidv7(),
  context_id    uuid NOT NULL REFERENCES contexts(id) ON DELETE CASCADE,
  name          text NOT NULL,
  position      int NOT NULL DEFAULT 0,
  unlock_at     timestamptz,
  require_sequential_progress boolean NOT NULL DEFAULT false,
  published     boolean NOT NULL DEFAULT false,
  workflow_state text NOT NULL DEFAULT 'active' CHECK (workflow_state IN ('active','deleted')),
  created_at    timestamptz NOT NULL DEFAULT now(),
  updated_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_modules_ctx ON modules(context_id, position);

-- =====================================================================
-- 11. DEEP LINKING + RESOURCE LINKS
-- =====================================================================
CREATE TABLE IF NOT EXISTS deep_link_requests (
  id              uuid PRIMARY KEY DEFAULT uuidv7(),
  deployment_id   uuid NOT NULL REFERENCES deployments(id) ON DELETE CASCADE,
  context_id      uuid REFERENCES contexts(id) ON DELETE CASCADE,
  user_id         uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  placement       text NOT NULL,
  target_module_id     uuid REFERENCES modules(id) ON DELETE SET NULL,   -- where to put new items
  target_assignment_id uuid REFERENCES assignments(id) ON DELETE SET NULL,
  data            text NOT NULL,
  accept_types    text[] NOT NULL,
  accept_multiple boolean NOT NULL DEFAULT true,
  status          text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','completed','failed','expired')),
  created_at      timestamptz NOT NULL DEFAULT now(),
  expires_at      timestamptz NOT NULL,
  completed_at    timestamptz
);
CREATE INDEX IF NOT EXISTS ix_dl_requests_pending ON deep_link_requests(expires_at) WHERE status = 'pending';

CREATE TABLE IF NOT EXISTS content_items (
  id                   uuid PRIMARY KEY DEFAULT uuidv7(),
  deep_link_request_id uuid NOT NULL REFERENCES deep_link_requests(id) ON DELETE CASCADE,
  item_type            text NOT NULL CHECK (item_type IN ('ltiResourceLink','link','file','html','image','ltiAssetProcessor')),
  title                text,
  url                  text,
  payload              jsonb NOT NULL,
  sort_order           int NOT NULL DEFAULT 0,
  created_at           timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_content_items_req ON content_items(deep_link_request_id);

CREATE TABLE IF NOT EXISTS resource_links (
  id              uuid PRIMARY KEY DEFAULT uuidv7(),
  deployment_id   uuid NOT NULL REFERENCES deployments(id) ON DELETE CASCADE,
  context_id      uuid REFERENCES contexts(id) ON DELETE CASCADE,
  assignment_id   uuid REFERENCES assignments(id) ON DELETE SET NULL,   -- external_tool assignment
  content_item_id uuid REFERENCES content_items(id) ON DELETE SET NULL,
  link_id         text NOT NULL UNIQUE CHECK (length(link_id) <= 255),
  title           text NOT NULL,
  description     text,
  target_link_uri text,
  custom          jsonb NOT NULL DEFAULT '{}'::jsonb,
  available_from  timestamptz,
  available_until timestamptz,
  submission_from  timestamptz,
  submission_until timestamptz,
  is_deleted      boolean NOT NULL DEFAULT false,
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_resource_links_ctx ON resource_links(context_id) WHERE NOT is_deleted;
CREATE INDEX IF NOT EXISTS ix_resource_links_deploy ON resource_links(deployment_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_resource_links_assignment ON resource_links(assignment_id) WHERE assignment_id IS NOT NULL AND NOT is_deleted;

CREATE TABLE IF NOT EXISTS module_items (
  id               uuid PRIMARY KEY DEFAULT uuidv7(),
  module_id        uuid NOT NULL REFERENCES modules(id) ON DELETE CASCADE,
  item_type        text NOT NULL CHECK (item_type IN ('assignment','external_tool','external_url','page','file','subheader')),
  title            text NOT NULL,
  assignment_id    uuid REFERENCES assignments(id) ON DELETE CASCADE,
  resource_link_id uuid REFERENCES resource_links(id) ON DELETE CASCADE,
  url              text,
  content_ref      jsonb,                         -- page/file reference
  position         int NOT NULL DEFAULT 0,
  indent           int NOT NULL DEFAULT 0 CHECK (indent BETWEEN 0 AND 5),
  new_tab          boolean NOT NULL DEFAULT false,
  published        boolean NOT NULL DEFAULT false,
  created_at       timestamptz NOT NULL DEFAULT now(),
  updated_at       timestamptz NOT NULL DEFAULT now(),
  CHECK (item_type <> 'assignment'    OR assignment_id IS NOT NULL),
  CHECK (item_type <> 'external_tool' OR resource_link_id IS NOT NULL),
  CHECK (item_type <> 'external_url'  OR url IS NOT NULL)
);
CREATE INDEX IF NOT EXISTS ix_module_items_module ON module_items(module_id, position);
CREATE INDEX IF NOT EXISTS ix_module_items_rl ON module_items(resource_link_id) WHERE resource_link_id IS NOT NULL;

-- =====================================================================
-- 12. OIDC LOGIN SESSIONS, NONCES, OAUTH ACCESS TOKENS
-- =====================================================================
CREATE TABLE IF NOT EXISTS oidc_login_sessions (
  id               uuid PRIMARY KEY DEFAULT uuidv7(),
  lti_message_hint text NOT NULL UNIQUE,
  login_hint       text NOT NULL,
  deployment_id    uuid NOT NULL REFERENCES deployments(id) ON DELETE CASCADE,
  user_id          uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  user_session_id  uuid REFERENCES user_sessions(id) ON DELETE CASCADE,
  context_id       uuid REFERENCES contexts(id) ON DELETE CASCADE,
  resource_link_id uuid REFERENCES resource_links(id) ON DELETE CASCADE,
  deep_link_request_id uuid REFERENCES deep_link_requests(id) ON DELETE CASCADE,
  message_type     text NOT NULL,
  placement        text,
  target_link_uri  text NOT NULL,
  consumed_at      timestamptz,
  created_at       timestamptz NOT NULL DEFAULT now(),
  expires_at       timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_oidc_sessions_expiry ON oidc_login_sessions(expires_at);

CREATE TABLE IF NOT EXISTS used_nonces (
  registration_id uuid NOT NULL REFERENCES tool_registrations(id) ON DELETE CASCADE,
  nonce           text NOT NULL,
  purpose         text NOT NULL CHECK (purpose IN ('auth_request','client_assertion_jti','deep_link_response')),
  seen_at         timestamptz NOT NULL DEFAULT now(),
  expires_at      timestamptz NOT NULL,
  PRIMARY KEY (registration_id, purpose, nonce)
);
CREATE INDEX IF NOT EXISTS ix_used_nonces_expiry ON used_nonces(expires_at);

CREATE TABLE IF NOT EXISTS access_tokens (
  id              uuid PRIMARY KEY DEFAULT uuidv7(),
  registration_id uuid NOT NULL REFERENCES tool_registrations(id) ON DELETE CASCADE,
  token_hash      bytea NOT NULL UNIQUE,
  scopes          text[] NOT NULL,
  issued_at       timestamptz NOT NULL DEFAULT now(),
  expires_at      timestamptz NOT NULL,
  revoked_at      timestamptz,
  client_ip       inet,
  CHECK (expires_at > issued_at)
);
CREATE INDEX IF NOT EXISTS ix_access_tokens_reg ON access_tokens(registration_id, expires_at);

-- =====================================================================
-- 13. AGS + GRADEBOOK
-- =====================================================================
CREATE TABLE IF NOT EXISTS line_items (
  id               uuid PRIMARY KEY DEFAULT uuidv7(),
  context_id       uuid NOT NULL REFERENCES contexts(id) ON DELETE CASCADE,
  deployment_id    uuid NOT NULL REFERENCES deployments(id) ON DELETE CASCADE,
  assignment_id    uuid NOT NULL REFERENCES assignments(id) ON DELETE CASCADE,   -- every line item backs a gradebook column
  resource_link_id uuid REFERENCES resource_links(id) ON DELETE SET NULL,
  label            text NOT NULL,
  score_maximum    numeric(12,4) NOT NULL CHECK (score_maximum > 0),
  resource_id      text,
  tag              text,
  start_at         timestamptz,
  end_at           timestamptz,
  grades_released  boolean NOT NULL DEFAULT true,
  is_primary       boolean NOT NULL DEFAULT true,       -- assignment's own line item
  created_by_tool  boolean NOT NULL DEFAULT true,
  created_at       timestamptz NOT NULL DEFAULT now(),
  updated_at       timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_line_items_ctx ON line_items(context_id);
CREATE INDEX IF NOT EXISTS ix_line_items_filters ON line_items(context_id, resource_link_id, resource_id, tag);
CREATE UNIQUE INDEX IF NOT EXISTS ux_line_items_primary ON line_items(assignment_id) WHERE is_primary;

CREATE TABLE IF NOT EXISTS scores (                        -- append-only AGS history
  id                 bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  line_item_id       uuid NOT NULL REFERENCES line_items(id) ON DELETE CASCADE,
  user_id            uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  score_given        numeric(12,4) CHECK (score_given IS NULL OR score_given >= 0),
  score_maximum      numeric(12,4) CHECK (score_maximum IS NULL OR score_maximum > 0),
  comment            text,
  activity_progress  text NOT NULL CHECK (activity_progress IN ('Initialized','Started','InProgress','Submitted','Completed')),
  grading_progress   text NOT NULL CHECK (grading_progress IN ('FullyGraded','Pending','PendingManual','Failed','NotReady')),
  tool_timestamp     timestamptz NOT NULL,
  submission         jsonb,
  posted_by_registration_id uuid REFERENCES tool_registrations(id) ON DELETE SET NULL,
  received_at        timestamptz NOT NULL DEFAULT now(),
  CHECK (score_given IS NULL OR score_maximum IS NOT NULL)
);
CREATE INDEX IF NOT EXISTS ix_scores_item_user_time ON scores(line_item_id, user_id, tool_timestamp DESC);

CREATE TABLE IF NOT EXISTS results (                       -- current AGS result per user per line item
  line_item_id        uuid NOT NULL REFERENCES line_items(id) ON DELETE CASCADE,
  user_id             uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  result_score        numeric(12,4),
  result_maximum      numeric(12,4),
  comment             text,
  activity_progress   text NOT NULL,
  grading_progress    text NOT NULL,
  last_score_id       bigint REFERENCES scores(id) ON DELETE SET NULL,
  last_tool_timestamp timestamptz NOT NULL,
  updated_at          timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (line_item_id, user_id)
);

CREATE TABLE IF NOT EXISTS submissions (                   -- gradebook cell: one per user per assignment
  id              uuid PRIMARY KEY DEFAULT uuidv7(),
  assignment_id   uuid NOT NULL REFERENCES assignments(id) ON DELETE CASCADE,
  user_id         uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  attempt         int NOT NULL DEFAULT 0 CHECK (attempt >= 0),
  workflow_state  text NOT NULL DEFAULT 'unsubmitted'
                  CHECK (workflow_state IN ('unsubmitted','submitted','pending_review','graded')),
  submission_type text,
  submitted_at    timestamptz,
  score           numeric(12,4),
  grade           text,                                    -- display grade (points, %, letter, complete)
  excused         boolean NOT NULL DEFAULT false,
  late            boolean NOT NULL DEFAULT false,
  missing         boolean NOT NULL DEFAULT false,
  graded_at       timestamptz,
  grader_user_id  uuid REFERENCES users(id) ON DELETE SET NULL,
  grader_registration_id uuid REFERENCES tool_registrations(id) ON DELETE SET NULL,
  grade_source    text NOT NULL DEFAULT 'manual' CHECK (grade_source IN ('manual','lti_ags','import','system')),
  instructor_override boolean NOT NULL DEFAULT false,      -- true: AGS sync no longer changes score
  posted_at       timestamptz,                             -- visible to student
  created_at      timestamptz NOT NULL DEFAULT now(),
  updated_at      timestamptz NOT NULL DEFAULT now(),
  UNIQUE (assignment_id, user_id),
  CHECK (NOT (grader_user_id IS NOT NULL AND grader_registration_id IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS ix_submissions_user ON submissions(user_id);
CREATE INDEX IF NOT EXISTS ix_submissions_needs_grading ON submissions(assignment_id) WHERE workflow_state IN ('submitted','pending_review');

CREATE TABLE IF NOT EXISTS grade_changes (                 -- append-only grade history
  id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  submission_id   uuid NOT NULL REFERENCES submissions(id) ON DELETE CASCADE,
  assignment_id   uuid NOT NULL,
  user_id         uuid NOT NULL,
  old_score       numeric(12,4),
  new_score       numeric(12,4),
  old_grade       text,
  new_grade       text,
  old_excused     boolean,
  new_excused     boolean,
  grader_user_id  uuid,
  grader_registration_id uuid,
  grade_source    text NOT NULL,
  changed_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_grade_changes_sub ON grade_changes(submission_id, changed_at DESC);
CREATE INDEX IF NOT EXISTS ix_grade_changes_assignment ON grade_changes(assignment_id, changed_at DESC);

-- scores -> results (latest by tool timestamp wins; AGS 2.0 semantics)
CREATE OR REPLACE FUNCTION lti.apply_score() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE li_max numeric;
BEGIN
  SELECT score_maximum INTO li_max FROM lti.line_items WHERE id = NEW.line_item_id;
  INSERT INTO lti.results AS r (line_item_id, user_id, result_score, result_maximum, comment,
                                activity_progress, grading_progress, last_score_id, last_tool_timestamp)
  VALUES (NEW.line_item_id, NEW.user_id,
          CASE WHEN NEW.score_given IS NULL THEN NULL
               ELSE round(NEW.score_given * li_max / NEW.score_maximum, 4) END,   -- normalise to line item max
          li_max, NEW.comment, NEW.activity_progress, NEW.grading_progress, NEW.id, NEW.tool_timestamp)
  ON CONFLICT (line_item_id, user_id) DO UPDATE
     SET result_score = EXCLUDED.result_score, result_maximum = EXCLUDED.result_maximum,
         comment = EXCLUDED.comment, activity_progress = EXCLUDED.activity_progress,
         grading_progress = EXCLUDED.grading_progress, last_score_id = EXCLUDED.last_score_id,
         last_tool_timestamp = EXCLUDED.last_tool_timestamp, updated_at = now()
   WHERE r.last_tool_timestamp < EXCLUDED.last_tool_timestamp;           -- ignore stale/out-of-order
  RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER trg_scores_apply AFTER INSERT ON scores
  FOR EACH ROW EXECUTE FUNCTION lti.apply_score();

-- results -> submissions (gradebook), only for the assignment's primary line item
CREATE OR REPLACE FUNCTION lti.sync_result_to_submission() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE li record; a record; reg uuid; new_state text; new_score numeric; submitted boolean;
BEGIN
  SELECT * INTO li FROM lti.line_items WHERE id = NEW.line_item_id;
  IF NOT li.is_primary THEN RETURN NEW; END IF;
  SELECT * INTO a FROM lti.assignments WHERE id = li.assignment_id;
  SELECT registration_id INTO reg FROM lti.deployments WHERE id = li.deployment_id;
  submitted := NEW.activity_progress IN ('Submitted','Completed');
  new_state := CASE NEW.grading_progress
                 WHEN 'FullyGraded'   THEN 'graded'
                 WHEN 'PendingManual' THEN 'pending_review'
                 WHEN 'Pending'       THEN CASE WHEN submitted THEN 'submitted' ELSE 'unsubmitted' END
                 ELSE CASE WHEN submitted THEN 'submitted' ELSE 'unsubmitted' END END;
  -- convert to assignment points when they differ from the line item max
  new_score := CASE WHEN NEW.grading_progress = 'FullyGraded' AND NEW.result_score IS NOT NULL
                    THEN round(NEW.result_score * COALESCE(a.points_possible, li.score_maximum) / li.score_maximum, 4)
                    WHEN NEW.grading_progress = 'FullyGraded' THEN NULL END;

  INSERT INTO lti.submissions AS s (assignment_id, user_id, workflow_state, submission_type, submitted_at,
         score, grade, graded_at, grader_registration_id, grade_source)
  VALUES (a.id, NEW.user_id, new_state, 'external_tool',
          CASE WHEN submitted THEN now() END,
          new_score, lti.format_grade(a.grading_type, new_score, a.points_possible),
          CASE WHEN new_state = 'graded' THEN now() END,
          reg, 'lti_ags')
  ON CONFLICT (assignment_id, user_id) DO UPDATE SET
     workflow_state = EXCLUDED.workflow_state,
     submission_type = 'external_tool',
     submitted_at = COALESCE(s.submitted_at, EXCLUDED.submitted_at),
     score  = CASE WHEN NEW.grading_progress = 'FullyGraded' THEN EXCLUDED.score ELSE s.score END,
     grade  = CASE WHEN NEW.grading_progress = 'FullyGraded' THEN EXCLUDED.grade ELSE s.grade END,
     graded_at = CASE WHEN NEW.grading_progress = 'FullyGraded' THEN now() ELSE s.graded_at END,
     grader_user_id = NULL,
     grader_registration_id = EXCLUDED.grader_registration_id,
     grade_source = 'lti_ags'
   WHERE NOT s.instructor_override AND NOT s.excused;
  RETURN NEW;
END $$;

CREATE OR REPLACE FUNCTION lti.format_grade(p_type text, p_score numeric, p_points numeric) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
  SELECT CASE
    WHEN p_score IS NULL THEN NULL
    WHEN p_type = 'points'    THEN trim_scale(p_score)::text
    WHEN p_type = 'percent'   THEN trim_scale(round(p_score * 100 / NULLIF(p_points,0), 2))::text || '%'
    WHEN p_type = 'pass_fail' THEN CASE WHEN p_score > 0 THEN 'complete' ELSE 'incomplete' END
    ELSE trim_scale(p_score)::text            -- letter_grade: app maps via grading scheme
  END $$;

CREATE OR REPLACE TRIGGER trg_results_sync AFTER INSERT OR UPDATE ON results
  FOR EACH ROW EXECUTE FUNCTION lti.sync_result_to_submission();

-- submissions -> grade_changes (every score/grade/excused change, any source)
CREATE OR REPLACE FUNCTION lti.log_grade_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'INSERT' AND NEW.score IS NULL AND NEW.grade IS NULL AND NOT NEW.excused THEN RETURN NEW; END IF;
  IF TG_OP = 'UPDATE' AND NEW.score IS NOT DISTINCT FROM OLD.score AND NEW.grade IS NOT DISTINCT FROM OLD.grade
     AND NEW.excused = OLD.excused THEN RETURN NEW; END IF;
  INSERT INTO lti.grade_changes(submission_id, assignment_id, user_id, old_score, new_score, old_grade, new_grade,
         old_excused, new_excused, grader_user_id, grader_registration_id, grade_source)
  VALUES (NEW.id, NEW.assignment_id, NEW.user_id,
          CASE WHEN TG_OP = 'UPDATE' THEN OLD.score END, NEW.score,
          CASE WHEN TG_OP = 'UPDATE' THEN OLD.grade END, NEW.grade,
          CASE WHEN TG_OP = 'UPDATE' THEN OLD.excused END, NEW.excused,
          NEW.grader_user_id, NEW.grader_registration_id, NEW.grade_source);
  RETURN NEW;
END $$;
CREATE OR REPLACE TRIGGER trg_submissions_grade_log AFTER INSERT OR UPDATE ON submissions
  FOR EACH ROW EXECUTE FUNCTION lti.log_grade_change();

-- =====================================================================
-- 14. DYNAMIC REGISTRATION, PNS, OUTBOX
-- =====================================================================
CREATE TABLE IF NOT EXISTS dynamic_registrations (
  id                      uuid PRIMARY KEY DEFAULT uuidv7(),
  tenant_id               uuid NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  account_id              uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
  initiated_by            uuid REFERENCES users(id) ON DELETE SET NULL,
  initiate_url            text NOT NULL,
  registration_token_hash bytea NOT NULL UNIQUE,
  status                  text NOT NULL DEFAULT 'initiated'
                          CHECK (status IN ('initiated','received','approved','rejected','expired')),
  tool_request            jsonb,
  registration_id         uuid REFERENCES tool_registrations(id) ON DELETE SET NULL,
  created_at              timestamptz NOT NULL DEFAULT now(),
  expires_at              timestamptz NOT NULL,
  decided_at              timestamptz
);

CREATE TABLE IF NOT EXISTS notice_handlers (
  deployment_id  uuid NOT NULL REFERENCES deployments(id) ON DELETE CASCADE,
  notice_type    text NOT NULL,
  handler_url    text NOT NULL CHECK (handler_url ~ '^https://'),
  max_batch_size int CHECK (max_batch_size IS NULL OR max_batch_size > 0),
  updated_at     timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (deployment_id, notice_type)
);

CREATE TABLE IF NOT EXISTS outbox_jobs (
  id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  kind         text NOT NULL,
  payload      jsonb NOT NULL,
  run_after    timestamptz NOT NULL DEFAULT now(),
  attempts     int NOT NULL DEFAULT 0,
  max_attempts int NOT NULL DEFAULT 8,
  locked_by    text,
  locked_until timestamptz,
  last_error   text,
  completed_at timestamptz,
  created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_outbox_ready ON outbox_jobs(run_after) WHERE completed_at IS NULL;

-- =====================================================================
-- 15. AUDIT LOG (monthly range partitions)
-- =====================================================================
CREATE TABLE IF NOT EXISTS audit_log (
  id              bigint GENERATED ALWAYS AS IDENTITY,
  occurred_at     timestamptz NOT NULL DEFAULT now(),
  tenant_id       uuid,
  actor_user_id   uuid,
  actor_client_id text,
  event           text NOT NULL,
  target_type     text,
  target_id       text,
  correlation_id  text,
  ip              inet,
  details         jsonb NOT NULL DEFAULT '{}'::jsonb,
  PRIMARY KEY (id, occurred_at)
) PARTITION BY RANGE (occurred_at);
CREATE TABLE IF NOT EXISTS audit_log_default PARTITION OF audit_log DEFAULT;
CREATE INDEX IF NOT EXISTS ix_audit_tenant_time ON audit_log(tenant_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_event ON audit_log(event, occurred_at DESC);

-- Create (or backfill from DEFAULT) the partition for the month containing p_month.
-- SECURITY DEFINER so wgulti_app's worker can call it without DDL rights.
CREATE OR REPLACE FUNCTION lti.ensure_audit_partition(p_month date) RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path = lti, pg_temp AS $$
DECLARE
  m_start date := date_trunc('month', p_month)::date;
  m_end   date := (date_trunc('month', p_month) + interval '1 month')::date;
  part    text := format('audit_log_y%sm%s', to_char(m_start,'YYYY'), to_char(m_start,'MM'));
BEGIN
  IF to_regclass('lti.' || part) IS NOT NULL THEN RETURN part; END IF;
  EXECUTE format('CREATE TABLE lti.%I (LIKE lti.audit_log INCLUDING DEFAULTS INCLUDING CONSTRAINTS)', part);
  -- move any rows that already landed in DEFAULT for this month, then attach
  EXECUTE format('WITH moved AS (DELETE FROM lti.audit_log_default WHERE occurred_at >= %L AND occurred_at < %L RETURNING *)
                  INSERT INTO lti.%I SELECT * FROM moved', m_start, m_end, part);
  EXECUTE format('ALTER TABLE lti.audit_log ATTACH PARTITION lti.%I FOR VALUES FROM (%L) TO (%L)', part, m_start, m_end);
  EXECUTE format('REVOKE ALL ON lti.%I FROM PUBLIC, wgulti_app, wgulti_readonly', part);
  RETURN part;
END $$;

CREATE OR REPLACE FUNCTION lti.ensure_audit_partitions(p_months_ahead int DEFAULT 3) RETURNS SETOF text
LANGUAGE sql SECURITY DEFINER SET search_path = lti, pg_temp AS $$
  SELECT lti.ensure_audit_partition((date_trunc('month', now()) + make_interval(months => g))::date)
  FROM generate_series(0, p_months_ahead) AS g
$$;

-- Detach + drop partitions entirely older than the retention window (default 7 years).
CREATE OR REPLACE FUNCTION lti.drop_audit_partitions_older_than(p_months int DEFAULT 84) RETURNS SETOF text
LANGUAGE plpgsql SECURITY DEFINER SET search_path = lti, pg_temp AS $$
DECLARE r record; cutoff date := (date_trunc('month', now()) - make_interval(months => p_months))::date;
BEGIN
  FOR r IN SELECT c.relname FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid
           WHERE i.inhparent = 'lti.audit_log'::regclass AND c.relname ~ '^audit_log_y\d{4}m\d{2}$'
             AND to_date(substr(c.relname, 12, 4) || substr(c.relname, 17, 2), 'YYYYMM') < cutoff
  LOOP
    EXECUTE format('ALTER TABLE lti.audit_log DETACH PARTITION lti.%I', r.relname);
    EXECUTE format('DROP TABLE lti.%I', r.relname);
    RETURN NEXT r.relname;
  END LOOP;
END $$;

-- Housekeeping (worker calls every few minutes; runs with caller's DML rights)
CREATE OR REPLACE FUNCTION lti.purge_expired() RETURNS void LANGUAGE sql AS $$
  DELETE FROM lti.used_nonces         WHERE expires_at < now();
  DELETE FROM lti.oidc_login_sessions WHERE expires_at < now() - interval '1 day';
  DELETE FROM lti.access_tokens       WHERE expires_at < now() - interval '1 day';
  DELETE FROM lti.user_sessions       WHERE expires_at < now() - interval '30 days';
  UPDATE lti.deep_link_requests SET status = 'expired' WHERE status = 'pending' AND expires_at < now();
  UPDATE lti.dynamic_registrations SET status = 'expired' WHERE status IN ('initiated','received') AND expires_at < now();
$$;

-- =====================================================================
-- 16. updated_at triggers
-- =====================================================================
DO $$
DECLARE t text;
BEGIN
  FOR t IN SELECT c.table_name FROM information_schema.columns c
           JOIN information_schema.tables tb ON tb.table_schema = c.table_schema AND tb.table_name = c.table_name
           WHERE c.table_schema = 'lti' AND c.column_name = 'updated_at' AND tb.table_type = 'BASE TABLE'
  LOOP
    EXECUTE format('CREATE OR REPLACE TRIGGER trg_%1$s_touch BEFORE UPDATE ON lti.%1$I
                    FOR EACH ROW EXECUTE FUNCTION lti.touch_updated_at()', t);
  END LOOP;
END $$;

DO $$ BEGIN PERFORM lti.ensure_audit_partitions(3); END $$;   -- current month + 3 ahead

-- =====================================================================
-- 17. PRIVILEGES
-- =====================================================================
REVOKE ALL ON SCHEMA lti FROM PUBLIC;
GRANT USAGE ON SCHEMA lti TO wgulti_app, wgulti_readonly;

-- app: DML only
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA lti TO wgulti_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA lti TO wgulti_app;
-- append-only / reference tables
REVOKE INSERT, UPDATE, DELETE ON schema_migrations, lis_roles, permissions FROM wgulti_app;
REVOKE UPDATE, DELETE ON audit_log, audit_log_default, grade_changes, scores, enrollment_changes FROM wgulti_app;

-- readonly: everything except credentials and session material
GRANT SELECT ON ALL TABLES IN SCHEMA lti TO wgulti_readonly;
REVOKE SELECT ON user_logins, user_sessions, api_tokens, access_tokens, oidc_login_sessions,
                 platform_keys, dynamic_registrations, lti1p1_consumers, identity_providers
  FROM wgulti_readonly;

-- functions: nothing for PUBLIC; app may run triggers' helpers + maintenance
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA lti FROM PUBLIC;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA lti TO wgulti_app;
REVOKE EXECUTE ON FUNCTION lti.drop_audit_partitions_older_than(int) FROM wgulti_app;   -- owner/ops only
GRANT EXECUTE ON FUNCTION lti.format_grade(text, numeric, numeric) TO wgulti_readonly;

-- future objects created by wgulti_owner in V002+ inherit the same grants
ALTER DEFAULT PRIVILEGES FOR ROLE wgulti_owner IN SCHEMA lti
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO wgulti_app;
ALTER DEFAULT PRIVILEGES FOR ROLE wgulti_owner IN SCHEMA lti
  GRANT USAGE, SELECT ON SEQUENCES TO wgulti_app;
ALTER DEFAULT PRIVILEGES FOR ROLE wgulti_owner IN SCHEMA lti
  GRANT SELECT ON TABLES TO wgulti_readonly;
ALTER DEFAULT PRIVILEGES FOR ROLE wgulti_owner IN SCHEMA lti
  REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
ALTER DEFAULT PRIVILEGES FOR ROLE wgulti_owner IN SCHEMA lti
  GRANT EXECUTE ON FUNCTIONS TO wgulti_app;

INSERT INTO schema_migrations(version, description, script)
VALUES ('001', 'baseline', 'V001__baseline.sql') ON CONFLICT (version) DO NOTHING;

RESET ROLE;
