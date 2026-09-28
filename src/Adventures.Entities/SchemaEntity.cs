namespace Adventures.Entities;

/// <summary>
/// A schema definition, itself an ordinary <see cref="DynamicEntity"/> - CRUDL'd through the same
/// <c>NQuadEntityRepository{TEntity}</c> as any other entity, with no repository changes required.
/// Its one field, "Field", is multi-valued and holds the IRIs of the <see cref="SchemaFieldEntity"/>
/// instances that describe its shape.
/// </summary>
public sealed class SchemaEntity : DynamicEntity
{
    public SchemaEntity(EntitySchema schema)
        : base(schema)
    {
    }

    /// <summary>
    /// Hand-authored, not loaded from any store: a schema describes every other entity's shape,
    /// including a <see cref="SchemaEntity"/>'s own, so this one fixed point can't be loaded from
    /// the store without infinite regress. Mirrors the POC's <c>PocConstants</c> - a small, fixed,
    /// compile-time vocabulary.
    /// </summary>
    public static EntitySchema MetaSchema { get; } = EntitySchema.Create(
        EntityConstants.Schema.EntityTypeIri,
        [
            new EntitySchemaField(
                EntityConstants.Schema.EntityTypeIri,
                Id: EntityConstants.Schema.EntityTypeIri + "#Id",
                Name: DynamicEntity.EntityIdPropertyName,
                Type: "String",
                Predicate: EntityConstants.Schema.IdentifierPredicate),
            new EntitySchemaField(
                EntityConstants.Schema.EntityTypeIri,
                Id: EntityConstants.Schema.EntityTypeIri + "#Field",
                Name: "Field",
                Type: "String",
                Predicate: EntityConstants.Schema.FieldPredicate),
        ]);
}
