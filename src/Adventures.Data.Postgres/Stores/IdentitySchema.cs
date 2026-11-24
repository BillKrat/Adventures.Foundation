namespace Adventures.Data.Postgres;

/// <summary>
/// The tables the sign-in accounts live in (the JSONB entity table, its triples and the credential vault). The same statements as Sql/schema.sql and Sql/schema-users.sql (a test keeps them equal),
/// every one a CREATE ... IF NOT EXISTS, so they can run on every start and a fresh install needs no script run by hand.
/// </summary>
public static class IdentitySchema
{
    public static IReadOnlyList<string> Statements { get; } =
    [
        """
        CREATE TABLE IF NOT EXISTS entities (
            id               UUID PRIMARY KEY,
            tenant           TEXT NOT NULL,
            org              TEXT NOT NULL,
            entity_type      TEXT NOT NULL,
            standard_fields  JSONB NOT NULL DEFAULT '{}'::jsonb,
            created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
            updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
            row_version      BIGINT NOT NULL DEFAULT 1
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_entities_tenant_org_type ON entities (tenant, org, entity_type)",
        "CREATE INDEX IF NOT EXISTS ix_entities_standard_fields_gin ON entities USING GIN (standard_fields)",
        """
        CREATE TABLE IF NOT EXISTS entity_triples (
            subject          UUID NOT NULL REFERENCES entities (id) ON DELETE CASCADE,
            predicate        TEXT NOT NULL,
            object           TEXT NOT NULL,
            object_datatype  TEXT NOT NULL DEFAULT 'xsd:string',
            graph            TEXT NOT NULL,
            PRIMARY KEY (subject, predicate, graph)
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_entity_triples_subject_graph ON entity_triples (subject, graph)",
        """
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
        )
        """,
        """
        CREATE INDEX IF NOT EXISTS ix_entities_user_tenant_username
            ON entities ((standard_fields ->> 'username'))
            WHERE entity_type = 'user'
        """,
    ];
}

/// <summary>Creates the sign-in tables when they are missing. Safe to run on every start.</summary>
public sealed class IdentitySchemaInitializer(ISqlExecutor executor)
{
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        foreach (var statement in IdentitySchema.Statements)
        {
            await executor.ExecuteAsync(statement, null, cancellationToken).ConfigureAwait(false);
        }
    }
}
