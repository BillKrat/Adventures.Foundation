using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// <see cref="SchemaBll.ToEntitySchema"/> is pure/storage-agnostic - tested here with plain,
/// in-memory <see cref="SchemaEntity"/>/<see cref="SchemaFieldEntity"/> instances, no store
/// involved. The async, repository-composing side (<see cref="SchemaBll.LoadEntitySchemaAsync"/>)
/// is covered against a real store in Adventures.Data.NQuad.Tests.
/// </summary>
public sealed class SchemaBllTests
{
    private static SchemaFieldEntity NewField(string id, string name, string fieldType, string predicate, string? valuePrefix = null)
    {
        var field = new SchemaFieldEntity(SchemaFieldEntity.MetaSchema)
            .Set(DynamicEntity.EntityIdPropertyName, id, id)
            .Set("Name", Guid.NewGuid().ToString(), name)
            .Set("FieldType", Guid.NewGuid().ToString(), fieldType)
            .Set("Predicate", Guid.NewGuid().ToString(), predicate) as SchemaFieldEntity
            ?? throw new InvalidOperationException();

        if (valuePrefix is not null)
        {
            field.Set("ValuePrefix", Guid.NewGuid().ToString(), valuePrefix);
        }

        return field;
    }

    private static SchemaEntity NewSchema(string id) =>
        new SchemaEntity(SchemaEntity.MetaSchema)
            .Set(DynamicEntity.EntityIdPropertyName, id, id) as SchemaEntity
        ?? throw new InvalidOperationException();

    [Fact]
    public void ToEntitySchema_BuildsFieldsFromSchemaFieldEntities()
    {
        var schema = NewSchema("Widget");
        var idField = NewField("id-1", "Id", "Guid", "urn:widget#id");
        var emailField = NewField("id-2", "Email", "String", "urn:widget#email", valuePrefix: "mailto:");

        var entitySchema = SchemaBll.ToEntitySchema(schema, [idField, emailField]);

        Assert.Equal("Widget", entitySchema.SchemaIri);
        Assert.Equal("Guid", entitySchema.Fields["Id"].Type);
        Assert.Equal("urn:widget#email", entitySchema.Fields["Email"].Predicate);
        Assert.Equal("mailto:", entitySchema.Fields["Email"].ValuePrefix);
    }

    [Fact]
    public void ToEntitySchema_FieldMissingName_Throws()
    {
        var schema = NewSchema("Widget");
        var field = new SchemaFieldEntity(SchemaFieldEntity.MetaSchema)
            .Set(DynamicEntity.EntityIdPropertyName, "id-1", "id-1")
            .Set("FieldType", Guid.NewGuid().ToString(), "String")
            .Set("Predicate", Guid.NewGuid().ToString(), "urn:widget#x") as SchemaFieldEntity
            ?? throw new InvalidOperationException();

        Assert.Throws<InvalidOperationException>(() => SchemaBll.ToEntitySchema(schema, [field]));
    }

    [Fact]
    public void ToEntitySchema_TwoFieldsMappedToSamePredicate_Throws()
    {
        var schema = NewSchema("Widget");
        var a = NewField("id-1", "A", "String", "urn:widget#same");
        var b = NewField("id-2", "B", "String", "urn:widget#same");

        Assert.Throws<InvalidOperationException>(() => SchemaBll.ToEntitySchema(schema, [a, b]));
    }

    [Fact]
    public void ToEntitySchema_NoFields_Throws()
    {
        var schema = NewSchema("Widget");

        Assert.Throws<InvalidOperationException>(() => SchemaBll.ToEntitySchema(schema, []));
    }
}
