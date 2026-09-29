using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// <see cref="EntityFormModel.From"/> is the "standard object" projection - a schema-driven form
/// screen's schema part and value part, built purely from an <see cref="EntitySchema"/> and a
/// <see cref="DynamicEntity"/>, no store involved. Deliberately plain records (not
/// <see cref="EntitySchema"/>/<see cref="DynamicEntity"/> themselves): those are DAL/domain-shaped
/// and not something a UI form control should depend on directly.
/// </summary>
public sealed class EntityFormModelTests
{
    private static EntitySchema WidgetSchema() => EntitySchema.Create(
        "Widget",
        [
            new EntitySchemaField("Widget", "id-1", "Id", "String", "urn:widget#id"),
            new EntitySchemaField("Widget", "id-2", "Name", "String", "urn:widget#name", IsRequired: true),
        ]);

    [Fact]
    public void From_BuildsSchemaAndDataParts()
    {
        var schema = WidgetSchema();
        var entity = new User(schema)
            .Set(DynamicEntity.EntityIdPropertyName, "w-1", "w-1")
            .Set("Name", "q-1", "Lamp");

        var model = EntityFormModel.From(schema, entity);

        Assert.Equal("Widget", model.Schema.SchemaIri);
        Assert.Equal(2, model.Schema.Fields.Count);
        Assert.Contains(model.Schema.Fields, f => f.Name == "Name" && f.Type == "String" && f.IsRequired);
        Assert.Contains(model.Schema.Fields, f => f.Name == "Id" && !f.IsRequired);

        Assert.Equal("w-1", model.Entity.EntityId);
        Assert.Equal("Lamp", model.Entity.Values["Name"]);
    }

    [Fact]
    public void From_FieldWithNoValue_IsNullInValues()
    {
        var schema = WidgetSchema();
        var entity = new User(schema).Set(DynamicEntity.EntityIdPropertyName, "w-1", "w-1");

        var model = EntityFormModel.From(schema, entity);

        Assert.Null(model.Entity.Values["Name"]);
    }
}
