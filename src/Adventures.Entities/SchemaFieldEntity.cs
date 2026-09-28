namespace Adventures.Entities;

/// <summary>
/// One field of a <see cref="SchemaEntity"/>'s shape, itself an ordinary <see cref="DynamicEntity"/> -
/// mirrors the same Name/FieldType/Predicate/ValuePrefix quads <c>SchemaDal</c> already reads off a
/// hand-authored schema-describing subject, so a <see cref="SchemaEntity"/>'s "Field" values can point
/// at real, CRUDL'able <see cref="SchemaFieldEntity"/> instances instead of opaque strings.
/// </summary>
public sealed class SchemaFieldEntity : DynamicEntity
{
    public SchemaFieldEntity(EntitySchema schema)
        : base(schema)
    {
    }

    /// <summary>Hand-authored for the same reason as <see cref="SchemaEntity.MetaSchema"/>.</summary>
    public static EntitySchema MetaSchema { get; } = EntitySchema.Create(
        EntityConstants.Schema.FieldEntityTypeIri,
        [
            new EntitySchemaField(
                EntityConstants.Schema.FieldEntityTypeIri,
                Id: EntityConstants.Schema.FieldEntityTypeIri + "#Id",
                Name: DynamicEntity.EntityIdPropertyName,
                Type: "String",
                Predicate: EntityConstants.Schema.IdentifierPredicate),
            new EntitySchemaField(
                EntityConstants.Schema.FieldEntityTypeIri,
                Id: EntityConstants.Schema.FieldEntityTypeIri + "#Name",
                Name: "Name",
                Type: "String",
                Predicate: EntityConstants.Schema.FieldNamePredicate),
            new EntitySchemaField(
                EntityConstants.Schema.FieldEntityTypeIri,
                Id: EntityConstants.Schema.FieldEntityTypeIri + "#FieldType",
                Name: "FieldType",
                Type: "String",
                Predicate: EntityConstants.Schema.FieldTypePredicate),
            new EntitySchemaField(
                EntityConstants.Schema.FieldEntityTypeIri,
                Id: EntityConstants.Schema.FieldEntityTypeIri + "#Predicate",
                Name: "Predicate",
                Type: "String",
                Predicate: EntityConstants.Schema.FieldRdfPredicate),
            new EntitySchemaField(
                EntityConstants.Schema.FieldEntityTypeIri,
                Id: EntityConstants.Schema.FieldEntityTypeIri + "#ValuePrefix",
                Name: "ValuePrefix",
                Type: "String",
                Predicate: EntityConstants.Schema.FieldValuePrefixPredicate),
        ]);
}
