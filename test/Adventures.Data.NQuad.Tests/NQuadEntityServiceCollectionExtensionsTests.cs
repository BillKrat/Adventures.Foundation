// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

public sealed class NQuadEntityServiceCollectionExtensionsTests
{
    private const string SchemaIri = "urn:test:Widget";

    private sealed class Widget(EntitySchema schema) : DynamicEntity(schema);

    private static readonly EntitySchema Schema = EntitySchema.Create(SchemaIri,
    [
        new EntitySchemaField(SchemaIri, "f-id", "Id", "string", "urn:p:id"),
        new EntitySchemaField(SchemaIri, "f-name", "Name", "string", "urn:p:name"),
    ]);

    [Fact]
    public async Task AddNQuadEntityRepository_RegistersAScopedRepository_ThatWritesNoActorOrAuditRecord()
    {
        var store = new InMemoryNQuadStore();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(Schema, "urn:test:widget/", SchemaIri, "urn:test:graph", s => new Widget(s));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var widget = new Widget(Schema);
        widget.Set("Id", "w1", "w1");
        widget.Set("Name", Guid.NewGuid().ToString(), "Gadget");

        await scope.ServiceProvider.GetRequiredService<IEntityRepository<Widget>>().CreateAsync(widget);

        Assert.DoesNotContain(await store.QueryAsync(), q => q.Predicate.Contains("/audit#", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddEntityBll_ResolvesTheGenericBllOverTheRegisteredRepository()
    {
        var store = new InMemoryNQuadStore();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(Schema, "urn:test:widget/", SchemaIri, "urn:test:graph", s => new Widget(s));
        services.AddEntityBll();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var bll = scope.ServiceProvider.GetRequiredService<IEntityBll<Widget>>();

        Assert.IsType<EntityBll<Widget>>(bll);
    }
}
