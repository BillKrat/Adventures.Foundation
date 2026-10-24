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

    private static ServiceProvider Build(InMemoryNQuadStore store, Func<IServiceProvider, string?>? changedBy)
    {
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(Schema, "urn:test:widget/", SchemaIri, "urn:test:graph", s => new Widget(s), changedBy);
        return services.BuildServiceProvider();
    }

    private static async Task<IReadOnlyList<string>> CreateAndReadActors(ServiceProvider provider, InMemoryNQuadStore store)
    {
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEntityRepository<Widget>>();
        var widget = new Widget(Schema);
        widget.Set("Id", "w1", "w1");
        widget.Set("Name", Guid.NewGuid().ToString(), "Gadget");
        await repository.CreateAsync(widget);

        var actors = await store.QueryAsync(predicate: EntityConstants.AuditRecord.ChangedByPredicate);
        return actors.Select(q => q.Object).Distinct().ToArray();
    }

    [Fact]
    public async Task AddNQuadEntityRepository_RecordsTheSuppliedActorInTheAuditTrail()
    {
        var store = new InMemoryNQuadStore();

        var actors = await CreateAndReadActors(Build(store, _ => "alice"), store);

        Assert.Equal(["alice"], actors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AddNQuadEntityRepository_WithNoActor_FallsBackToSystem(string? actor)
    {
        var store = new InMemoryNQuadStore();

        var actors = await CreateAndReadActors(Build(store, _ => actor), store);

        Assert.Equal([NQuadEntityServiceCollectionExtensions.SystemActor], actors);
    }

    [Fact]
    public async Task AddNQuadEntityRepository_EvaluatesTheActorPerScope()
    {
        var store = new InMemoryNQuadStore();
        var current = "alice";
        using var provider = Build(store, _ => current);

        using (var scope = provider.CreateScope())
        {
            var first = scope.ServiceProvider.GetRequiredService<IEntityRepository<Widget>>();
            current = "bob";
            using var scope2 = provider.CreateScope();
            var second = scope2.ServiceProvider.GetRequiredService<IEntityRepository<Widget>>();
            var w = new Widget(Schema);
            w.Set("Id", "w2", "w2");
            w.Set("Name", Guid.NewGuid().ToString(), "Second");
            await second.CreateAsync(w);
            Assert.NotNull(first);
        }

        var actors = (await store.QueryAsync(predicate: EntityConstants.AuditRecord.ChangedByPredicate)).Select(q => q.Object).Distinct();
        Assert.Equal(["bob"], actors);
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
