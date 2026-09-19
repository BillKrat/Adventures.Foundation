namespace Adventures.Data;

/// <summary>
/// A generic, tenant/org-scoped record. "Standard" application fields (title, body, user info,
/// grid/form field values the app ships with) are stored as a JSONB document (<see cref="StandardFields"/>),
/// while fully user-defined/extension fields are stored separately as W3C N-Quad style
/// <see cref="Triple"/> statements (see <see cref="ITripleStore"/>). This hybrid lets the app
/// query/index its own known fields efficiently via JSONB while still allowing users to design
/// and populate arbitrary custom form fields/grids without schema changes.
/// </summary>
/// <param name="Id">The entity's unique identifier.</param>
/// <param name="Tenant">The tenant (domain) this entity belongs to.</param>
/// <param name="Org">The org (blog) within the tenant this entity belongs to.</param>
/// <param name="EntityType">A discriminator naming the kind of entity (e.g. "post", "user", "form-definition").</param>
/// <param name="StandardFieldsJson">The entity's standard fields, serialized as a JSON document (stored as JSONB).</param>
/// <param name="CreatedAt">UTC creation timestamp.</param>
/// <param name="UpdatedAt">UTC last-updated timestamp.</param>
/// <param name="RowVersion">Optimistic concurrency token, incremented on every update.</param>
public sealed record Entity(
    Guid Id,
    string Tenant,
    string Org,
    string EntityType,
    string StandardFieldsJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long RowVersion);
