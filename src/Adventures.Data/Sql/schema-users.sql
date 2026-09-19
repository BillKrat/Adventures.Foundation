-- Users + credential vault, extending the JSONB Hybrid + N-Quads schema in schema.sql.
-- Requires the pgcrypto extension (for gen_random_uuid() used by seed-tenant-admin.sql):
--   CREATE EXTENSION IF NOT EXISTS pgcrypto;
-- A "user" is just another `entities` row (entity_type = 'user'); its standard_fields JSONB
-- holds the fields every user has. Anything a tenant/org wants beyond these standard fields
-- (custom profile fields, preferences, etc.) is added as entity_triples rows against the same
-- entities.id, at whatever tenant/org/user graph granularity is needed - no schema change required.
--
-- Standard user fields (entities.standard_fields JSONB shape, entity_type = 'user'):
--   username          text   - unique login name within the tenant
--   email             text
--   display_name      text
--   status            text   - 'active' | 'disabled' | 'must_change_password'
--   roles             text[] - e.g. ["TenantAdmin", "Author"] - drives scope/claim issuance at login
--
-- The user's row in `entities` (tenant, org, id) is what MCP servers/tool calls receive as the
-- caller's identity (the `sub` claim is the entities.id UUID) - never a password or secret.
-- Only the security/auth service reads `user_credentials` below to verify a login and mint a token.

CREATE TABLE IF NOT EXISTS user_credentials (
	user_id               UUID PRIMARY KEY REFERENCES entities (id) ON DELETE CASCADE,
	password_hash         TEXT NOT NULL,
	password_algorithm    TEXT NOT NULL DEFAULT 'pbkdf2-sha256',
	must_change_password  BOOLEAN NOT NULL DEFAULT true,
	failed_login_attempts INT NOT NULL DEFAULT 0,
	locked_until          TIMESTAMPTZ NULL,
	last_login_at         TIMESTAMPTZ NULL,
	created_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
	updated_at            TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Fast lookup of a user by (tenant, username) at login time, without scanning JSONB.
CREATE INDEX IF NOT EXISTS ix_entities_user_tenant_username
	ON entities ((standard_fields ->> 'username'))
	WHERE e