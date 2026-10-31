using Adventures.Data.NQuad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// S4 per-role matrix: what each shipped role may do, written out in full. This is the security contract of the default roles; a seed or an
/// upgrade that widens or narrows a role fails here until the change is made on purpose. The app route matrix test (ai-research-blog) uses
/// the same literal sets, so the two cannot drift apart without one of them failing.
/// </summary>
public sealed class RoleMatrixTests : IAsyncLifetime
{
    /// <summary>The shipped roles and every right each holds, once the startup upgrade has run. Admin is "*" (every right).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Expected = new Dictionary<string, string[]>
    {
        ["Admin"] = [.. Rights.Catalog.Select(r => r.Name)],
        ["Editor"] =
        [
            Rights.AdminAccess, Rights.PostsRead, Rights.PostsCreate, Rights.PostsEditAny, Rights.PostsDeleteOwn, Rights.PostsPublish,
            Rights.PagesRead, Rights.PagesEdit, Rights.PagesPublish, Rights.TaxonomyManage, Rights.MediaUpload, Rights.CollaborationRead, Rights.DecisionsRead,
        ],
        ["Reviewer"] =
        [
            Rights.AdminAccess, Rights.PostsRead, Rights.PagesRead, Rights.CollaborationRead, Rights.CollaborationWrite, Rights.CollaborationReview, Rights.DecisionsRead,
        ],
        ["Developer"] = [Rights.AdminAccess, Rights.PostsRead, Rights.CollaborationRead, Rights.CollaborationWrite, Rights.DiagnosticsRead],
    };

    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-004-identity.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-005-identity-required.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-018-role-rights.nq"));
        var quads = await store.QueryAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, EntityConstants.Identity.RoleSchemaIri), EntityConstants.Identity.RoleBaseIri, EntityConstants.Identity.RoleTypeIri, EntityConstants.Identity.DefaultGraph, s => new Role(s));
        _services = services.BuildServiceProvider();
        await RoleRightsUpgrade.ApplyAsync(new EntityDal<Role>(_services.GetRequiredService<IEntityRepository<Role>>()));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EveryShippedRoleHoldsExactlyItsExpectedRights_AndNoOtherRoleShips()
    {
        var roles = await _services.GetRequiredService<IEntityRepository<Role>>().ListAsync();

        Assert.Equivalent(Expected.Keys, roles.Select(r => (string)r.GetValue("Name")!));
        foreach (var role in roles)
        {
            var name = (string)role.GetValue("Name")!;
            Assert.True(
                Rights.Expand(Expected[name]).ToHashSet().SetEquals(Rights.Parse(role.GetValue("Rights") as string)),
                $"{name} holds a different set of rights than the matrix says.");
        }
    }

    [Theory]
    [InlineData("Editor")]
    [InlineData("Reviewer")]
    [InlineData("Developer")]
    public void NoRoleButAdminManagesPeopleRolesSettingsOrPlugins(string role)
    {
        string[] administrative = [Rights.UsersManage, Rights.RolesManage, Rights.SettingsManage, Rights.PluginsManage, Rights.DiagnosticsAi];

        Assert.Empty(Expected[role].Intersect(administrative));
    }

    [Fact]
    public void OnlyTheReviewerRecordsDecisions_AndOnlyTheEditorPublishes()
    {
        Assert.Equal(["Admin", "Reviewer"], Expected.Where(p => p.Value.Contains(Rights.CollaborationReview)).Select(p => p.Key).Order());
        Assert.Equal(["Admin", "Editor"], Expected.Where(p => p.Value.Contains(Rights.PostsPublish)).Select(p => p.Key).Order());
    }
}
