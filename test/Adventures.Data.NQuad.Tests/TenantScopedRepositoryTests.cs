using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// T2 (tenant data scoping, decision 2026-10 option C: a graph per tenant; every tenant is equal, none keeps the shared graphs). Threats: cross-tenant read,
/// cross-tenant write, a silent default. The wall lives in <see cref="NQuadEntityRepository{TEntity}"/>, so these tests drive the repository, not a controller.
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

    private static NQuadEntityRepository<Widget> Repo(InMemoryNQuadStore store, string tenantKey) =>
        new(store, Schema, Base, SchemaIri, Graph, s => new Widget(s), scope: new FixedScope(tenantKey));

    private sealed class FixedScope(string key) : IGraphScope
    {
        public string TenantKey { get; } = key;
    }

    private sealed class NoTenantScope : IGraphScope
    {
        public string TenantKey => throw new TenantRequiredException();
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
        await Repo(store, "tenant-a").CreateAsync(NewWidget("a1", "tenant a"));
        await Repo(store, "tenant-b").CreateAsync(NewWidget("b1", "tenant b"));
        await Repo(store, "tenant-c").CreateAsync(NewWidget("c1", "tenant c"));

        Assert.Equal(["a1"], (await Repo(store, "tenant-a").ListAsync()).Select(w => w.EntityId!.Value.ToString()).Order());
        Assert.Equal(["b1"], (await Repo(store, "tenant-b").ListAsync()).Select(w => w.EntityId!.Value.ToString()).Order());
        Assert.Equal(1, await Repo(store, "tenant-a").CountAsync());
        Assert.Equal(1, await Repo(store, "tenant-c").CountAsync());
    }

    [Fact]
    public async Task A_get_by_another_tenants_id_finds_nothing()
    {
        var store = new InMemoryNQuadStore();
        await Repo(store, "tenant-b").CreateAsync(NewWidget("b1", "tenant b"));

        Assert.Null(await Repo(store, "tenant-a").GetAsync("b1"));
        Assert.NotNull(await Repo(store, "tenant-b").GetAsync("b1"));
    }

    [Fact]
    public async Task A_write_goes_only_to_the_tenants_own_graph()
    {
        var store = new InMemoryNQuadStore();
        await Repo(store, "tenant-a").CreateAsync(NewWidget("a1", "tenant a"));

        Assert.All(await store.QueryAsync(subject: Base + "a1"), q => Assert.Equal(Graph + "/tenant/tenant-a", q.Graph));
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
    public async Task Data_left_in_the_shared_graph_is_visible_to_no_tenant()
    {
        // There is no primary tenant: content that has not been moved into a tenant's graph (the one-time relabel) is invisible, never shown to the wrong site.
        var store = new InMemoryNQuadStore();
        await store.InsertAsync(new NQuad(Guid.NewGuid(), Base + "old", "http://www.w3.org/1999/02/22-rdf-syntax-ns#type", SchemaIri, Graph));
        await store.InsertAsync(new NQuad(Guid.NewGuid(), Base + "old", "urn:p:name", "unmoved", Graph));

        Assert.Empty(await Repo(store, "tenant-a").ListAsync());
        Assert.Null(await Repo(store, "tenant-a").GetAsync("old"));
    }

    [Fact]
    public async Task A_request_with_no_tenant_cannot_read_or_write()
    {
        var store = new InMemoryNQuadStore();
        var repository = new NQuadEntityRepository<Widget>(store, Schema, Base, SchemaIri, Graph, s => new Widget(s), scope: new NoTenantScope());

        await Assert.ThrowsAsync<TenantRequiredException>(() => repository.ListAsync());
        await Assert.ThrowsAsync<TenantRequiredException>(() => repository.GetAsync("a"));
        await Assert.ThrowsAsync<TenantRequiredException>(() => repository.CreateAsync(NewWidget("a", "x")));
        Assert.Equal(0, await store.CountAsync());
    }

    [Fact]
    public async Task A_tenant_key_that_could_escape_the_graph_is_refused()
    {
        var store = new InMemoryNQuadStore();

        await Assert.ThrowsAsync<ArgumentException>(() => Repo(store, "../system").ListAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => Repo(store, "a/b").ListAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => Repo(store, " ").ListAsync());
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
    public void A_scoped_type_with_no_scope_registered_fails_closed()
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
