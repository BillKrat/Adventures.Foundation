// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Entities;

/// <summary>
/// One field of a <see cref="SchemaEntity"/>'s shape, itself an ordinary <see cref="DynamicEntity"/> -
/// mirrors the same Name/FieldType/Predicate/ValuePrefix quads <c>SchemaDal</c> already reads off a
/// hand-authored schema-describing subject, so a <see cref="SchemaEntity"/>'s "Field" values can point
/// at real, CRUDL'able <see cref="SchemaFieldEntity"/> instances instead of opaque strings. Its own
/// <see cref="EntitySchema"/> is not hardcoded - see <see cref="SchemaEntity"/>'s doc comment; load it
/// via <c>SchemaDal.Load(quads, EntityConstants.Schema.FieldEntityTypeIri)</c>.
/// </summary>
public sealed class SchemaFieldEntity : DynamicEntity
{
    public SchemaFieldEntity(EntitySchema schema)
        : base(schema)
    {
    }
}
