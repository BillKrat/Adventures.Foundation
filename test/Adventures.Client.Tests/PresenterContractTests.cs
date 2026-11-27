using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Client.Tests;

/// <summary>
/// The client's presenter against the real server contract: the same grid and form JSON the Angular admin reads, over a real controller pipeline and a
/// real entity repository, in memory. (The refusals of a server are covered by <c>HttpEntityPresenterTests</c>.)
/// </summary>
public sealed class PresenterContractTests
{
    [Fact]
    public async Task The_grid_and_a_create_read_update_delete_round_trip_through_the_presenter()
    {
        var store = new InMemoryNQuadStore();
        await store.SeedFromFileAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed-004-identity.nq"));
        var schema = SchemaDal.Load(await store.QueryAsync(), IdentityConstants.RoleSchemaIri);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(ContractRolesController).Assembly);
        builder.Services.AddSingleton<IEntityRepository<DynamicRecord>>(new NQuadEntityRepository<DynamicRecord>(
            store, schema, IdentityConstants.RoleBaseIri, IdentityConstants.RoleTypeIri, IdentityConstants.DefaultGraph, s => new DynamicRecord(s)));
        builder.Services.AddSingleton(schema);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        var presenter = new HttpEntityPresenter<DynamicRecord>(app.GetTestClient(), "contract/roles");

        var grid = await presenter.GetGridAsync(new EntityGridQuery(Page: 1, PageSize: 10));
        Assert.Contains(grid.Rows, r => r.Values.TryGetValue("Name", out var name) && name == "Developer");
        Assert.Equal(1, grid.Paging!.Page);

        var created = await presenter.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["Name"] = "Desktop test role" }));
        var id = created.Entity.EntityId;
        Assert.False(string.IsNullOrEmpty(id));
        Assert.NotNull(await presenter.UpdateAsync(id, new EntityDataModel(id, new Dictionary<string, string?> { ["Name"] = "Desktop test role (edited)" })));
        Assert.Equal("Desktop test role (edited)", (await presenter.GetFormAsync(id))!.Entity.Values["Name"]);
        Assert.True(await presenter.DeleteAsync(id));
        Assert.Null(await presenter.GetFormAsync(id));
    }
}

[ApiController]
[Route("contract/roles")]
public sealed class ContractRolesController(IEntityRepository<DynamicRecord> repository, EntitySchema schema) : EntityControllerBase<DynamicRecord>
{
    private EntityPresenter<DynamicRecord> Presenter =>
        new(new EntityBll<DynamicRecord>(new EntityDal<DynamicRecord>(repository)), schema, s => new DynamicRecord(s));

    [HttpGet]
    public Task<ActionResult<EntityGridModel>> List([FromQuery] EntityGridQuery query, CancellationToken cancellationToken) => GridAsync(Presenter, query, cancellationToken);

    [HttpGet("{id}")]
    public Task<ActionResult<EntityFormModel>> Get(string id, CancellationToken cancellationToken) => GetAsync(Presenter, id, cancellationToken);

    [HttpPost]
    public Task<ActionResult<EntityFormModel>> Create([FromBody] EntityDataModel request, CancellationToken cancellationToken) => CreateAsync(Presenter, request, cancellationToken);

    [HttpPut("{id}")]
    public Task<ActionResult<EntityFormModel>> Update(string id, [FromBody] EntityDataModel request, CancellationToken cancellationToken) => UpdateAsync(Presenter, id, request, cancellationToken);

    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken cancellationToken) => DeleteEntityAsync(Presenter, id, cancellationToken);
}
