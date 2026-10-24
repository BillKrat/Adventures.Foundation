-- Seeds the real dev/login accounts for global-webnet.com: Admin (BlogEngine.NET's well-known Admin/Admin
-- convention - not a secret, a known default) and Claude (same convention, for Claude's own
-- testing/dev account). Hashes below are real output of Adventures.Security.PasswordHasher.Hash(...)
-- (PBKDF2-HMAC-SHA256, 210k iterations), not placeholders.
--
-- These rows live in the OLDER Postgres JSONB `entities`/`user_credentials` tables, which is what
-- Adventures.Identity.UserAccountService.LoginAsync actually queries (by tenant + username) - a
-- deliberately separate id space from the NEW N-Quad `User : DynamicEntity` store seeded by
-- Adventures.Data.NQuad/Sql/seed/seed.nq. The two stores are bridged by *username*, not id: after
-- login, the JWT's Name claim carries the username, and ProfileController/UsersController look the
-- profile up by that username against the N-Quad store (see ProfileController's doc comment). So
-- the UUIDs generated here do not need to, and deliberately do not, match seed.nq's Admin/Claude
-- entity ids.
--
-- must_change_password is left true for both, matching the BlogEngine.NET Admin/Admin convention -
-- it is surfaced in the login response today but not yet enforced (no force-password-change code
-- exists yet; this is deliberately deferred, low priority).
--
-- Usage: psql -f seed-global-webnet-accounts.sql - genuinely idempotent: re-running looks each
-- username up first and updates its existing credential row in place rather than inserting a
-- second entity (there is no DB-level unique constraint on standard_fields->>'username' to lean
-- on for ON CONFLICT, so this does the lookup explicitly).

DO $$
DECLARE
	admin_id UUID;
	claude_id UUID;
BEGIN
	SELECT id INTO admin_id FROM entities
		WHERE tenant = 'global-webnet.com' AND entity_type = 'user' AND standard_fields ->> 'username' = 'Admin';
	IF admin_id IS NULL THEN
		admin_id := gen_random_uuid();
		INSERT INTO entities (id, tenant, org, entity_type, standard_fields)
		VALUES (
			admin_id,
			'global-webnet.com',
			'default',
			'user',
			jsonb_build_object(
				'username', 'Admin',
				'email', 'admin@global-webnet.com',
				'display_name', 'Admin User',
				'status', 'must_change_password',
				'roles', jsonb_build_array('TenantAdmin')
			)
		);
	END IF;

	INSERT INTO user_credentials (user_id, password_hash, password_algorithm, must_change_password)
	VALUES (admin_id, '210000:IKci9xdnJGc64pqeduhkHA==:+Io8UP5qP3Wr35x+aqL7CwADuBx3eI4CIZF+PcBOCqA=', 'pbkdf2-sha256', true)
	ON CONFLICT (user_id) DO UPDATE SET
		password_hash = EXCLUDED.password_hash,
		must_change_password = EXCLUDED.must_change_password,
		updated_at = now();

	SELECT id INTO claude_id FROM entities
		WHERE tenant = 'global-webnet.com' AND entity_type = 'user' AND standard_fields ->> 'username' = 'Claude';
	IF claude_id IS NULL THEN
		claude_id := gen_random_uuid();
		INSERT INTO entities (id, tenant, org, entity_type, standard_fields)
		VALUES (
			claude_id,
			'global-webnet.com',
			'default',
			'user',
			jsonb_build_object(
				'username', 'Claude',
				'email', 'claude@global-webnet.com',
				'display_name', 'Claude',
				'status', 'must_change_password',
				'roles', jsonb_build_array('User')
			)
		);
	END IF;

	INSERT INTO user_credentials (user_id, password_hash, password_algorithm, must_change_password)
	VALUES (claude_id, '210000:xwHQOh/SloN5q4y7+123bg==:Fl8mXZ3/UyEZFSg1CfoqsHq8ZLcs9uJSmuE8Ld8WtAQ=', 'pbkdf2-sha256', true)
	ON CONFLICT (user_id) DO UPDATE SET
		password_hash = EXCLUDED.password_hash,
		must_change_password = EXCLUDED.must_change_password,
		updated_at = now();
END $$;
