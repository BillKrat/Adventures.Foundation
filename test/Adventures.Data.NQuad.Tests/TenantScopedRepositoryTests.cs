using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// T2 (tenant data scoping, decision 2026-10 option C: a graph per tenant). Threats: cross-tenant read, cross-tenant write, a silent
/// default. The wall lives in <see cref="NQuadEntityRepository{TEntity}"/>, so these tests drive the repository, not a controller.
/// </summary>
public sealed class TenantScopedRepositoryTests
{
    private const string SchemaIri = "urn:test:Widget";
    private const string Base = "urn:test:widget/";
    private const string Graph = "urn:test:graph/content";

    private sealed class Widget(EntitySchema schema) : DynamicEntity(schema);

    private static readonly EntitySchema Schema = EntitySchema.Create(SchemaIri,
    [
        new EntitySchemaField(SchemaIri, "f-id", "Id", "string", "urn:p:id"),
        new EntitySchemaField(SchemaIri, "f-name", "Name", "string", "urn:p:name"),
    ]);

    private static NQuadEntityRepository<Widget> Repo(InMemoryNQuadStore store, string? tenantKey) =>
        new(store, Schema, Base, SchemaIri, Graph, s => new Widget(s), scope: new FixedScope(tenantKey));

    private sealed class FixedScope(string? key) : IGraphScope
    {
        public string? TenantKey { get; } = key;
    }

    private static Widget NewWidget(string id, string name)
    {
        var widget = new Widget(Schema);
        widget.Set("Id", id, id);
        widget.Set("Name", Guid.NewGuid().ToString(), name);
        return widget;
    }

    [Fact]
    public async Task Each_tenant_lists_only_its_own_entities()
    {
        var store = new InMemoryNQuadStore();
        await Repo(store, null).CreateAsync(NewWidget("p1", "primary"));
        await Repo(store, "tenant-a").CreateAsync(NewWidget("a1", "tenant a"));
        await Repo(store, "tenant-b").CreateAsync(NewWidget("b1", "tenant b"));

        Assert.Equal(["p1"], (await Repo(store, null).ListAsync()).Select(w => w.EntityId!.Value.ToString()).Order());
        Assert.Equal(["a1"], (await Repo(store, "tenant-a").ListAsync()).Select(w => w.EntityId!.Value.ToString()).Order());
        Assert.Equal(["b1"], (await Repo(store, "tenant-b").ListAsync()).Select(w => w.EntityId!.Value.ToString()).Order());
        Assert.Equal(1, await Repo(store, "tenant-a").CountAsync());
        Assert.Equal(1, await Repo(store, null).CountAsync());
    }

    [Fact]
    public async Task A_get_by_another_tenants_id_finds_nothing()
    {
        var store = new InMemoryNQuadStore();
        await Repo(store, "tenant-b").CreateAsync(NewWidget("b1", "tenant b"));

        Assert.Null(await Repo(store, "tenant-a").GetAsync("b1"));
        Assert.Null(await Repo(store, null).GetAsync("b1"));
        Assert.NotNull(await Repo(store, "tenant-b").GetAsync("b1"));
    }

    [Fact]
    public async Task A_write_goes_only_to_the_tenants_own_graph()
    {
        var store = new InMemoryNQuadStore();
        await Repo(store, "tenant-a").CreateAsync(NewWidget("a1", "tenant a"));
        await Repo(store, null).CreateAsync(NewWidget("p1", "primary"));

        Assert.All(await store.QueryAsync(subject: Base + "a1"), q => Assert.Equal(Graph + "/tenant/tenant-a", q.Graph));
        Assert.All(await store.QueryAsync(subject: Base + "p1"), q => Assert.Equal(Graph, q.Graph));
    }

    [Fact]
    public async Task Update_and_delete_of_another_tenants_entity_change_nothing()
    {
        var store = new InMemoryNQuadStore();
        await Repo(store, "tenant-b").CreateAsync(NewWidget("b1", "tenant b"));
        var before = await store.CountAsync();

        var stranger = Repo(store, "tenant-a");
        var attempt = NewWidget("b1", "hijacked");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => stranger.UpdateAsync(attempt));
        Assert.False(await stranger.DeleteAsync("b1"));
        Assert.Equal(0, await stranger.DeleteManyAsync(["b1"]));

        Assert.Equal(before, await store.CountAsync());
        Assert.Equal("tenant b", (await Repo(store, "tenant-b").GetAsync("b1"))!.GetValue("Name"));
    }

    [Fact]
    public async Task The_same_id_can_exist_in_two_tenants_without_colliding()
    {
        var store = new InMemoryNQuadStore();
        await Repo(store, "tenant-a").CreateAsync(NewWidget("x", "from a"));
        await Repo(store, "tenant-b").CreateAsync(NewWidget("x", "from b"));

        Assert.Equal("from a", (await Repo(store, "tenant-a").GetAsync("x"))!.GetValue("Name"));
        Assert.Equal("from b", (await Repo(store, "tenant-b").GetAsync("x"))!.GetValue("Name"));
    }

    [Fact]
    public async Task The_primary_tenant_still_reads_data_in_whatever_graph_it_was_stored()
    {
        // Live data was seeded and imported into graphs the registration may not name exactly; "untouched" means the primary tenant sees all of it.
        var store = new InMemoryNQuadStore();
        var id = Guid.NewGuid();
        await store.InsertAsync(new NQuad(Guid.NewGuid(), Base + "old", "http://www.w3.org/1999/02/22-rdf-syntax-ns#type", SchemaIri, "urn:some:other:graph"));
        await store.InsertAsync(new NQuad(id, Base + "old", "urn:p:name", "legacy", "urn:some:other:graph"));

        Assert.Single(await Repo(store, null).ListAsync());
        Assert.Empty(await Repo(store, "tenant-a").ListAsync());
    }

    [Fact]
    public void A_tenant_key_that_could_escape_the_graph_is_refused()
    {
        var store = new InMemoryNQuadStore();

        Assert.Throws<ArgumentException>(() => Repo(store, "../system"));
        Assert.Throws<ArgumentException>(() => Repo(store, "a/b"));
        Assert.Throws<ArgumentException>(() => Repo(store, " "));
    }

    [Fact]
    public async Task A_type_registered_without_tenant_scoping_ignores_the_scope()
    {
        var store = new InMemoryNQuadStore();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddScoped<IGraphScope>(_ => new FixedScope("tenant-a"));
        services.AddNQuadEntityRepository(Schema, Base, SchemaIri, Graph, s => new Widget(s));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IEntityRepository<Widget>>().CreateAsync(NewWidget("g1", "global"));

        Assert.All(await store.QueryAsync(subject: Base + "g1"), q => Assert.Equal(Graph, q.Graph));
    }

    [Fact]
    public async Task A_type_registered_with_tenant_scoping_takes_the_request_scope()
    {
        var store = new InMemoryNQuadStore();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddScoped<IGraphScope>(_ => new FixedScope("tenant-a"));
        services.AddNQuadEntityRepository(Schema, Base, SchemaIri, Graph, s => new Widget(s), tenantScoped: true);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IEntityRepository<Widget>>().CreateAsync(NewWidget("a1", "tenant a"));

        Assert.All(await store.QueryAsync(subject: Base + "a1"), q => Assert.Equal(Graph + "/tenant/tenant-a", q.Graph));
    }

    [Fact]
    public async Task A_scoped_type_with_no_scope_registered_fails_closed()
    {
        var store = new InMemoryNQuadStore();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(Schema, Base, SchemaIri, Graph, s => new Widget(s), tenantScoped: true);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.ThrowsAny<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IEntityRepository<Widget>>());
    }
}
