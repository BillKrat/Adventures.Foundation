// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary><see cref="EntityBll{TEntity}"/>: delegation to the repository plus the shared required-field rule.</summary>
public sealed class EntityBllTests
{
    private const string SchemaIri = "urn:test:Widget";
    private const string BaseIri = "urn:test:widget/";
    private const string TypeIri = "urn:test:Widget";
    private const string Graph = "urn:test:graph";

    private sealed class Widget(EntitySchema schema) : DynamicEntity(schema);

    private static readonly EntitySchema Schema = EntitySchema.Create(SchemaIri,
    [
        new EntitySchemaField(SchemaIri, "f-id", "Id", "string", "urn:p:id"),
        new EntitySchemaField(SchemaIri, "f-name", "Name", "string", "urn:p:name", IsRequired: true),
    ]);

    private static EntityBll<Widget> NewBll() =>
        new(new EntityDal<Widget>(new NQuadEntityRepository<Widget>(new InMemoryNQuadStore(), Schema, BaseIri, TypeIri, Graph, s => new Widget(s))));

    private static Widget NewWidget(string id, string? name)
    {
        var widget = new Widget(Schema);
        widget.Set("Id", id, id);
        if (name is not null)
        {
            widget.Set("Name", Guid.NewGuid().ToString(), name);
        }

        return widget;
    }

    [Fact]
    public async Task CreateAsync_WithRequiredFieldMissing_ThrowsInputValidationException()
    {
        var ex = await Assert.ThrowsAsync<InputValidationException>(() => NewBll().CreateAsync(NewWidget("w1", name: null)));

        Assert.Equal("Name is required.", Assert.Single(ex.Errors["Name"]));
    }

    [Fact]
    public async Task CreateAsync_ThenGetAsync_RoundTrips()
    {
        var bll = NewBll();

        await bll.CreateAsync(NewWidget("w1", "Gadget"));
        var read = await bll.GetAsync("w1");

        Assert.Equal("Gadget", read!.GetValue("Name"));
    }

    [Fact]
    public async Task DeleteAsync_TombstonesTheEntity()
    {
        var bll = NewBll();
        await bll.CreateAsync(NewWidget("w1", "Gadget"));

        Assert.True(await bll.DeleteAsync("w1"));
        Assert.Null(await bll.GetAsync("w1"));
    }
}
