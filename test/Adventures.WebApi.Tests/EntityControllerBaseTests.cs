// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Data.NQuad;
using Adventures.Entities;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>
/// <see cref="EntityControllerBase{TEntity}"/>'s protected helpers and
/// <see cref="ApiControllerBase.GuardedAsync"/>, exercised through <see cref="TestEntityController"/> -
/// a real <see cref="IEntityPresenter{TEntity}"/> backed by a real store, not a fake, since the
/// whole point of this layer is thin response-shaping over an already-tested presenter.
/// </summary>
public sealed class EntityControllerBaseTests : IAsyncLifetime
{
    private const string Graph = "https://global-webnet.com/graph/test";

    private TestEntityController _controller = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seedPath = Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq");
        await store.SeedFromFileAsync(seedPath);
        var quads = await store.QueryAsync();

        // No hardcoded MetaSchema anymore - loaded the same way any other schema is, via SchemaDal.
        var schemaEntitySchema = SchemaDal.Load(quads, EntityConstants.Schema.EntityTypeIri);

        var repository = new NQuadEntityRepository<SchemaEntity>(
            store,
            schemaEntitySchema,
            EntityConstants.Schema.EntityBaseIri,
            EntityConstants.Schema.EntityTypeIri,
            Graph,
            schema => new SchemaEntity(schema));
        var presenter = new EntityPresenter<SchemaEntity>(new EntityBll<SchemaEntity>(new EntityDal<SchemaEntity>(repository)), schemaEntitySchema, schema => new SchemaEntity(schema));

        _controller = new TestEntityController(presenter);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static EntityDataModel FieldData(string entityId, string value) =>
        new(entityId, new Dictionary<string, string?> { ["Field"] = value });

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        var result = await _controller.Get(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsStatus201WithTheForm()
    {
        var result = await _controller.Create(FieldData(string.Empty, "urn:widget#x"), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, objectResult.StatusCode);
        var form = Assert.IsType<EntityFormModel>(objectResult.Value);
        Assert.Equal("urn:widget#x", form.Entity.Values["Field"]);
    }

    [Fact]
    public async Task Get_AfterCreate_ReturnsOkWithTheForm()
    {
        var created = await _controller.Create(FieldData(string.Empty, "urn:widget#x"), CancellationToken.None);
        var createdForm = (EntityFormModel)((ObjectResult)created.Result!).Value!;

        var result = await _controller.Get(createdForm.Entity.EntityId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(createdForm.Entity.EntityId, ((EntityFormModel)ok.Value!).Entity.EntityId);
    }

    [Fact]
    public async Task List_IncludesCreatedEntity()
    {
        var created = await _controller.Create(FieldData(string.Empty, "urn:widget#x"), CancellationToken.None);
        var createdForm = (EntityFormModel)((ObjectResult)created.Result!).Value!;

        var result = await _controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var all = Assert.IsAssignableFrom<IReadOnlyList<EntityDataModel>>(ok.Value);
        Assert.Contains(all, e => e.EntityId == createdForm.Entity.EntityId);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var result = await _controller.Update(Guid.NewGuid().ToString(), FieldData(string.Empty, "urn:widget#y"), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_KnownId_ReturnsOkWithUpdatedForm()
    {
        var created = await _controller.Create(FieldData(string.Empty, "urn:widget#x"), CancellationToken.None);
        var createdForm = (EntityFormModel)((ObjectResult)created.Result!).Value!;

        var result = await _controller.Update(createdForm.Entity.EntityId, FieldData(createdForm.Entity.EntityId, "urn:widget#y"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("urn:widget#y", ((EntityFormModel)ok.Value!).Entity.Values["Field"]);
    }

    [Fact]
    public async Task GuardedAsync_PassesThroughNormalResult()
    {
        var result = await _controller.DeleteGuarded(() => Task.FromResult<IActionResult>(new NoContentResult()));

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task GuardedAsync_BusinessRuleException_ReturnsConflictWithItsMessage()
    {
        var result = await _controller.DeleteGuarded(() => throw new Adventures.Common.Exceptions.BusinessRuleException("You cannot delete your own account."));

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("You cannot delete your own account.", conflict.Value);
    }

    [Fact]
    public async Task GuardedAsync_InputValidationException_ReturnsBadRequestWithFieldErrors()
    {
        var result = await _controller.DeleteGuarded(() => throw new Adventures.Common.Exceptions.InputValidationException("DOB", "DOB is not a valid date."));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal(400, problem.Status);
        Assert.Equal("DOB is not a valid date.", Assert.Single(problem.Errors["DOB"]));
    }

    [Fact]
    public async Task GuardedAsync_SystemException_IsNotCaught_SoItsDetailsNeverReachTheClient()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _controller.DeleteGuarded(() => throw new InvalidOperationException("Schema https://internal/iri was not found.")));
    }

    [Fact]
    public async Task Create_WithInvalidInput_ReturnsBadRequestNotAnException()
    {
        var data = new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["NoSuchField"] = "x" });

        var result = await _controller.Create(data, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
