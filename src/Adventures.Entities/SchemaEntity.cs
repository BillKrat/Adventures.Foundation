namespace Adventures.Entities;

/// <summary>
/// A schema definition, itself an ordinary <see cref="DynamicEntity"/> - CRUDL'd through the same
/// <c>NQuadEntityRepository{TEntity}</c> as any other entity, with no repository changes required.
/// Its one field, "Field", is multi-valued and holds the IRIs of the <see cref="SchemaFieldEntity"/>
/// instances that describe its shape. Its own <see cref="EntitySchema"/> is not hardcoded - load it
/// via <c>Adventures.Data.NQuad.SchemaDal.Load(quads, EntityConstants.Schema.EntityTypeIri)</c>, the
/// same way any other schema is loaded. The real fixed point this relies on is the parsing
/// algorithm, not the data: <c>SchemaDal</c> already knows how to read any <c>schema#type</c>/
/// <c>schema#field</c>/... description, so "Schema" describing its own shape with that exact same
/// vocabulary is not infinite regress - it is just one more schema.
/// </summary>
public sealed class SchemaEntity : DynamicEntity
{
    public SchemaEntity(EntitySchema schema)
        : base(schema)
    {
    }
}
