-- Seeds a tenant admin user, one per new domain/tenant being initialized.
-- Password is the deliberately-simple 'Password' (mirrors BlogEngine.NET's Admin/Password
-- convention) - must_change_password = true forces a change on first login, so this is
-- intentionally not a security risk left in place.
--
-- Usage: replace :'tenant' / :'org' with the new domain/blog being initialized and run via
-- `psql -v tenant=acme.com -v org=default -f seed-tenant-admin.sql`.
-- Password hash below is a placeholder - generate the real value via
-- Adventures.Security.PasswordHasher.Hash("Password") (PBKDF2-HMAC-SHA256, 210k iterations,
-- format "{iterations}:{saltBase64}:{hashBase64}") before this is used against a live tenant;
-- do not ship this literal placeholder. This hasher is deliberately distinct from
-- ClientSecretHasher (used for M2M client secrets) - user passwords are low-entropy and require
-- a slow, iterated KDF; security requirements for human credentials take priority over reusing
-- the faster M2M hasher for convenience.

DO $$
DECLARE
	admin_id UUID := gen_random_uuid();
BEGIN
	INSERT INTO entities (id, tenant, org, entity_type, standard_fields)
	VALUES (
		admin_id,
		:'tenant',
		:'org',
		'user',
		jsonb_build_object(
			'username', 'Admin',
			'email', 'admin@' || :'tenant',
			'display_name', 'Tenant Administrator',
			'status', 'must_change_password',
			'roles', jsonb_build_array('TenantAdmin')
		)
	);

	INSERT INTO user_credentials (user_id, password_hash, password_algorithm, must_change_password)
	VALUES (
		admin_id,
		'<REPLACE-WITH-OUTPUT-OF-PasswordHasher.Hash("Password")>',
		'pbkdf2-sha256',
		true
	);
END $$;
