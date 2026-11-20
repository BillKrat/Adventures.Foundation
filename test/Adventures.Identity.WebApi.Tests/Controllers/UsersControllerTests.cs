using Adventures.Identity.WebApi;
// Assessed 2026-10-24 17:51 CT (Claude): automated scan for secrets, injection and unsafe patterns, re-run after the fixes - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Security.Claims;
using Adventures.Entities;
using Adventures.Identity;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Adventures.Blog;

namespace Adventures.Identity.WebApi.Tests;

public class UsersControllerTests
{
    private const string BillId = "user-bill";
    private const string ClaudeId = "user-claude";

    private static EntityFormModel BuildForm(string id, string userName) =>
        new(
            new EntitySchemaModel("https://global-webnet.com/schema/User",
            [
                new EntityFieldSchema("UserName", "String", IsRequired: true),
            ]),
            new EntityDataModel(id, new Dictionary<string, string?> { ["UserName"] = userName }));

    [Fact]
    public async Task List_ReturnsASortedPagedGrid_WithServerBuiltLinks()
    {
        var presenter = new FakeUserPresenter
        {
            Summaries =
            [
                new EntityDataModel(BillId, new Dictionary<string, string?> { ["UserName"] = "BillKrat" }),
                new EntityDataModel(ClaudeId, new Dictionary<string, string?> { ["UserName"] = "Claude" }),
            ],
        };
        var controller = BuildController(presenter, callerUserName: "BillKrat");
        controller.ControllerContext.HttpContext.Request.Path = "/api/users";

        var result = await controller.List(new EntityGridQuery(Page: 2, PageSize: 1, Sort: "UserName", Descending: true), CancellationToken.None);

        var grid = Assert.IsType<EntityGridModel>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2, grid.Rows.Count);
        Assert.Equal(new EntityGridQuery(2, 1, "UserName", true), presenter.LastQuery);
        Assert.Equal("/api/users?page=2&pageSize=1&sort=UserName&descending=true", grid.Links!.Self);
        Assert.Equal("/api/users", grid.Links.Create);
        Assert.Null(grid.Links.Next);
        Assert.Equal("/api/users?page=1&pageSize=1&sort=UserName&descending=true", grid.Links.Previous);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenPresenterHasNoForm()
    {
        var presenter = new FakeUserPresenter { FormById = null };
        var controller = BuildController(presenter, callerUserName: "BillKrat");

        var result = await controller.Get(ClaudeId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WithTheNewForm()
    {
        var presenter = new FakeUserPresenter { CreateResult = BuildForm(ClaudeId, "Claude") };
        var controller = BuildController(presenter, callerUserName: "BillKrat");
        var request = new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = "Claude" });

        var result = await controller.Create(request, CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
        var form = Assert.IsType<EntityFormModel>(created.Value);
        Assert.Equal("Claude", form.Entity.Values["UserName"]);
        Assert.Same(request, presenter.LastCreateRequest);
    }

    [Fact]
    public async Task Delete_OtherAccount_ReturnsNoContent()
    {
        var presenter = new FakeUserPresenter
        {
            FormByUserName = BuildForm(BillId, "BillKrat"),
            DeleteResult = true,
        };
        var controller = BuildController(presenter, callerUserName: "BillKrat");

        var result = await controller.Delete(ClaudeId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal((ClaudeId, BillId), presenter.LastDeleteCall);
    }

    [Fact]
    public async Task Delete_OwnAccount_ReturnsConflict()
    {
        var presenter = new FakeUserPresenter
        {
            FormByUserName = BuildForm(BillId, "BillKrat"),
            ThrowOnDelete = true,
        };
        var controller = BuildController(presenter, callerUserName: "BillKrat");

        var result = await controller.Delete(BillId, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsUnauthorized_WhenCallerCannotBeResolved()
    {
        var presenter = new FakeUserPresenter();
        var controller = BuildController(presenter, callerUserName: null);

        var result = await controller.Delete(ClaudeId, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    internal static readonly EntitySchema UserSchemaWithRelation = EntitySchema.Create(
        "urn:test:User",
        [new EntitySchemaField("urn:test:User", "f1", "UserName", "String", "urn:p:name")],
        [new EntityRelationDefinition("TenantRoles", "Tenant roles", "urn:test:Assignment", "urn:p:has")]);

    [Fact]
    public async Task AddRelationChild_Returns201WithTheChild_AndPassesTheRequestToTheWriter()
    {
        var writer = new FakeRelationWriter();
        var controller = BuildController(new FakeUserPresenter(), "BillKrat", writer);
        var request = new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["Role"] = "r1", ["Tenant"] = "t1" });

        var result = await controller.AddRelationChild(BillId, "TenantRoles", request, CancellationToken.None);

        Assert.Equal(201, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal((BillId, "TenantRoles", request), writer.LastAdd);
    }

    [Fact]
    public async Task AddRelationChild_IsNotFound_ForAnUnknownRelationOrAMissingParent_AndMapsRuleRefusals()
    {
        var writer = new FakeRelationWriter();
        var controller = BuildController(new FakeUserPresenter(), "BillKrat", writer);
        var request = new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["Tenant"] = "t1" });

        Assert.IsType<NotFoundResult>(await controller.AddRelationChild(BillId, "NoSuchRelation", request, CancellationToken.None));
        Assert.Null(writer.LastAdd);

        writer.AddResult = null;
        Assert.IsType<NotFoundResult>(await controller.AddRelationChild(BillId, "TenantRoles", request, CancellationToken.None));

        writer.Throw = new Adventures.Common.Exceptions.BusinessRuleException("This entry already exists.");
        Assert.IsType<ConflictObjectResult>(await controller.AddRelationChild(BillId, "TenantRoles", request, CancellationToken.None));

        writer.Throw = new Adventures.Common.Exceptions.InputValidationException(new Dictionary<string, string[]> { ["Role"] = ["Role is required."] });
        Assert.IsType<BadRequestObjectResult>(await controller.AddRelationChild(BillId, "TenantRoles", request, CancellationToken.None));
    }

    [Fact]
    public async Task RemoveRelationChild_Returns204_404_OrConflictForTheLastAdministrator()
    {
        var writer = new FakeRelationWriter();
        var controller = BuildController(new FakeUserPresenter(), "BillKrat", writer, relations: new FakeRelations { Rows = { ("TenantRoles", "c1", new Dictionary<string, string?> { ["Tenant"] = "t1" }) } });

        Assert.IsType<NoContentResult>(await controller.RemoveRelationChild(BillId, "TenantRoles", "c1", CancellationToken.None));
        Assert.Equal((BillId, "TenantRoles", "c1"), writer.LastRemove);

        writer.RemoveResult = false;
        Assert.IsType<NotFoundResult>(await controller.RemoveRelationChild(BillId, "TenantRoles", "c1", CancellationToken.None));

        writer.Throw = new Adventures.Common.Exceptions.BusinessRuleException("A tenant must keep at least one administrator.");
        Assert.IsType<ConflictObjectResult>(await controller.RemoveRelationChild(BillId, "TenantRoles", "c1", CancellationToken.None));
    }

    /// <summary>T1: a person set up on one tenant's domain gets their sign-in in that tenant, not in the one from configuration.</summary>
    [Fact]
    public async Task SetPassword_CreatesTheSignInInTheTenantOfTheRequestHost()
    {
        var presenter = new FakeUserPresenter { FormById = BuildForm(BillId, "BillKrat") };
        var provisioning = new FakeProvisioning();
        var tenant = new FixedRequestTenant(new CurrentTenant("t2", "Other", "Other.example", "api.other.example", TenantMatch.Domain));
        var controller = BuildController(presenter, "BillKrat", provisioning: provisioning, requestTenant: tenant);

        await controller.SetPassword(BillId, CancellationToken.None);

        Assert.Equal("other.example", provisioning.LastTenant);
    }

    private sealed class RefusingProvisioning : IUserProvisioning
    {
        public Task<ProvisionedLogin> SetInitialPasswordAsync(string tenant, string username, string? email, string? displayName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No sign-in accounts here.");
    }

    [Fact]
    public async Task SetPassword_ExplainsWhenTheHostCannotCreateSignIns()
    {
        var presenter = new FakeUserPresenter { FormById = BuildForm(BillId, "BillKrat") };
        var controller = BuildController(presenter, "BillKrat", provisioning: new RefusingProvisioning());

        var result = await controller.SetPassword(BillId, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status501NotImplemented, problem.StatusCode);
        Assert.Equal("No sign-in accounts here.", Assert.IsType<ProblemDetails>(problem.Value).Title);
    }

    internal static UsersController BuildController(FakeUserPresenter presenter, string? callerUserName, FakeRelationWriter? writer = null, IUserProvisioning? provisioning = null, Adventures.WebApi.IRequestTenant? requestTenant = null, FakeMembership? membership = null, FakeRelations? relations = null, UserVisibility? visibility = null)
    {
        var identity = callerUserName is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.Name, callerUserName)], authenticationType: "Test");

        return new UsersController(presenter, writer ?? new FakeRelationWriter(), new EntitySchemaOf<User>(UserSchemaWithRelation), membership ?? new FakeMembership(), new FixedTenantContext("t1"), relations ?? new FakeRelations(),
            provisioning ?? new FakeProvisioning(), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            requestTenant ?? new FixedRequestTenant(new CurrentTenant(null, null, null, "api.host", TenantMatch.None)),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UsersController>.Instance, visibility)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    [Fact]
    public void Controller_NeedsTheUsersManageRight()
    {
        var guard = Assert.Single(
            typeof(UsersController).GetCustomAttributes(typeof(Adventures.WebApi.RequireRightAttribute), inherit: true)
                .Cast<Adventures.WebApi.RequireRightAttribute>());

        Assert.Equal(Adventures.Entities.Rights.UsersManage, guard.Right);
    }

    [Fact]
    public void NoActionOverridesTheClassPolicyWithAnAnonymousOrWeakerOne()
    {
        var weakened = typeof(UsersController).GetMethods()
            .Where(m => m.DeclaringType == typeof(UsersController))
            .Where(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true).Length > 0);

        Assert.Empty(weakened);
    }
}

/// <summary>Stands in for the provisioning service: these tests never reach it.</summary>
internal sealed class FakeProvisioning : IUserProvisioning
{
    public string? LastTenant { get; private set; }

    public Task<ProvisionedLogin> SetInitialPasswordAsync(string tenant, string username, string? email, string? displayName, CancellationToken cancellationToken = default)
    {
        LastTenant = tenant;
        return Task.FromResult(new ProvisionedLogin(username, "test-only", AccountCreated: false));
    }
}

internal sealed class FixedRequestTenant(Adventures.Entities.CurrentTenant current) : Adventures.WebApi.IRequestTenant
{
    public Adventures.Entities.CurrentTenant Current { get; } = current;
}

internal sealed class FixedTenantContext(string id) : ITenantContext
{
    public string TenantId { get; } = id;
}

/// <summary>Everybody is a member of tenant t1 unless the test says who else belongs where.</summary>
internal sealed class FakeMembership : IUserMembership
{
    public HashSet<string>? Members { get; init; }

    public Dictionary<string, HashSet<string>> Tenants { get; init; } = [];

    public HashSet<string> OrganisationsHere { get; init; } = [];

    public Task<IReadOnlySet<string>> MemberIdsAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(Members ?? new HashSet<string> { "user-bill", "user-claude" });

    public Task<IReadOnlySet<string>> TenantIdsOfAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(Tenants.TryGetValue(userId, out var set) ? set : ["t1"]);

    public Task AddMemberAsync(string userId, string tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> OrganisationInTenantAsync(string organisationId, string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(OrganisationsHere.Contains(organisationId));
}

/// <summary>Returns the assignments a test lists (relation name, child id, values).</summary>
internal sealed class FakeRelations : IEntityRelationProvider
{
    public List<(string Relation, string ChildId, Dictionary<string, string?> Values)> Rows { get; } = [];

    public Task<IReadOnlyList<EntityRelationModel>> GetRelationsAsync(EntitySchema parentSchema, string parentEntityId, bool editable = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EntityRelationModel>>(Rows.GroupBy(r => r.Relation).Select(g => new EntityRelationModel(
            g.Key, g.Key, new EntityGridModel(null!, g.Select(r => new EntityDataModel(r.ChildId, r.Values)).ToList()))).ToList());

    public Task<IReadOnlyList<RelationOption>> GetOptionsAsync(string schemaIri, string? excludeId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RelationOption>>([]);

    public Task<IReadOnlyList<EntityDataModel>> ResolveDisplayAsync(EntitySchema schema, IReadOnlyList<EntityDataModel> rows, CancellationToken cancellationToken = default) =>
        Task.FromResult(rows);
}

/// <summary>T3b: what an administrator of one tenant may do to people and assignments, with another tenant in the picture.</summary>
public sealed class UsersTenantTests
{
    private static UsersController Controller(FakeUserPresenter presenter, FakeMembership membership, FakeRelationWriter writer, FakeRelations? relations = null, IUserProvisioning? provisioning = null) =>
        new(presenter, writer, new EntitySchemaOf<User>(EntitySchema.Create(
                "urn:test:User", [new EntitySchemaField("urn:test:User", "f1", "UserName", "String", "urn:p:name")],
                [new EntityRelationDefinition("TenantRoles", "Tenant roles", "urn:test:Assignment", "urn:p:has")])),
            membership, new FixedTenantContext("t1"), relations ?? new FakeRelations(), provisioning ?? new FakeProvisioning(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            new FixedRequestTenant(new CurrentTenant("t1", "n", "t1.test", "api.t1.test", TenantMatch.Domain)),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static EntityDataModel Assignment(string relation, string value) =>
        new(string.Empty, new Dictionary<string, string?> { [relation] = value, ["Role"] = "r1" });

    [Fact]
    public async Task APersonOfAnotherTenantCannotGetAnAssignmentAdded_OrRemoved()
    {
        var writer = new FakeRelationWriter();
        var controller = Controller(new FakeUserPresenter(), new FakeMembership { Members = ["mine"] }, writer);

        Assert.IsType<NotFoundResult>(await controller.AddRelationChild("theirs", "TenantRoles", Assignment("Tenant", "t1"), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.RemoveRelationChild("theirs", "TenantRoles", "c1", CancellationToken.None));
        Assert.Null(writer.LastAdd);
        Assert.Null(writer.LastRemove);
    }

    [Fact]
    public async Task AnAssignmentCannotNameAnotherTenant()
    {
        var writer = new FakeRelationWriter();
        var controller = Controller(new FakeUserPresenter(), new FakeMembership { Members = ["mine"] }, writer);

        var refused = await controller.AddRelationChild("mine", "TenantRoles", Assignment("Tenant", "t2"), CancellationToken.None);
        var allowed = await controller.AddRelationChild("mine", "TenantRoles", Assignment("Tenant", "t1"), CancellationToken.None);

        Assert.Equal(403, Assert.IsType<ObjectResult>(refused).StatusCode);
        Assert.Equal(201, Assert.IsType<ObjectResult>(allowed).StatusCode);
    }

    [Fact]
    public async Task AnOrganisationAssignmentMustNameAnOrganisationOfThisTenant()
    {
        var writer = new FakeRelationWriter();
        var controller = Controller(new FakeUserPresenter(), new FakeMembership { Members = ["mine"], OrganisationsHere = ["org-here"] }, writer);

        var elsewhere = await controller.AddRelationChild("mine", "OrganizationRoles", Assignment("Organization", "org-there"), CancellationToken.None);
        var here = await controller.AddRelationChild("mine", "OrganizationRoles", Assignment("Organization", "org-here"), CancellationToken.None);

        Assert.Equal(403, Assert.IsType<ObjectResult>(elsewhere).StatusCode);
        Assert.NotEqual(403, (here as ObjectResult)?.StatusCode ?? 0);
    }

    [Fact]
    public async Task OnlyAnAssignmentOfThisTenantCanBeRemoved_APersonKeepsTheRolesTheyHoldElsewhere()
    {
        var writer = new FakeRelationWriter();
        var relations = new FakeRelations
        {
            Rows =
            {
                ("TenantRoles", "mine-row", new Dictionary<string, string?> { ["Tenant"] = "t1" }),
                ("TenantRoles", "theirs-row", new Dictionary<string, string?> { ["Tenant"] = "t2" }),
            },
        };
        var controller = Controller(new FakeUserPresenter(), new FakeMembership { Members = ["both"] }, writer, relations);

        Assert.IsType<NotFoundResult>(await controller.RemoveRelationChild("both", "TenantRoles", "theirs-row", CancellationToken.None));
        Assert.Null(writer.LastRemove);
        Assert.IsType<NoContentResult>(await controller.RemoveRelationChild("both", "TenantRoles", "mine-row", CancellationToken.None));
        Assert.Equal(("both", "TenantRoles", "mine-row"), writer.LastRemove);
    }

    [Fact]
    public async Task APasswordCannotBeResetFromHereForAPersonOfAnotherTenantToo()
    {
        var provisioning = new FakeProvisioning();
        var presenter = new FakeUserPresenter { FormById = BuildForm("both", "shared-person") };
        var membership = new FakeMembership { Members = ["both", "solo"], Tenants = { ["both"] = ["t1", "t2"] } };
        var controller = Controller(presenter, membership, new FakeRelationWriter(), provisioning: provisioning);

        var refused = await controller.SetPassword("both", CancellationToken.None);

        Assert.Equal(409, Assert.IsType<ObjectResult>(refused).StatusCode);
        Assert.Null(provisioning.LastTenant);
    }

    private static EntityFormModel BuildForm(string id, string userName) =>
        new(
            new EntitySchemaModel("https://global-webnet.com/schema/User", [new EntityFieldSchema("UserName", "String", IsRequired: true)]),
            new EntityDataModel(id, new Dictionary<string, string?> { ["UserName"] = userName }));
}
