using System.Security.Claims;
using Adventures.Entities;
using Adventures.Identity.WebApi;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Identity.WebApi.Tests.Controllers;

/// <summary>Beta readiness: the choices for a pointer field (role, tenant and organization names) are offered only to a person who manages or reads what the forms edit; admin access alone is not enough.</summary>
public sealed class LookupsRightsTests
{
    private sealed class Held(params string[] rights) : IRightsAuthorizer
    {
        public Task<IReadOnlySet<string>> RightsOfAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(rights.ToHashSet());

        public Task<bool> IsAllowedAsync(ClaimsPrincipal user, string right, CancellationToken cancellationToken = default) => Task.FromResult(rights.Contains(right));
    }

    private sealed class Provider : IEntityRelationProvider
    {
        public int Asked;

        public Task<IReadOnlyList<RelationOption>> GetOptionsAsync(string schemaIri, string? excludeId = null, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult<IReadOnlyList<RelationOption>>([new RelationOption("1", "Admin")]);
        }

        public Task<IReadOnlyList<EntityRelationModel>> GetRelationsAsync(EntitySchema parentSchema, string parentEntityId, bool editable = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<EntityDataModel>> ResolveDisplayAsync(EntitySchema schema, IReadOnlyList<EntityDataModel> rows, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Admin_access_alone_is_refused_and_the_provider_is_never_asked()
    {
        var provider = new Provider();
        var result = await new LookupsController(provider, new Held(Rights.AdminAccess)).Options("https://x/Role", null, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Equal(0, provider.Asked);
    }

    [Theory]
    [InlineData(Rights.UsersManage)]
    [InlineData(Rights.RolesManage)]
    [InlineData(Rights.PostsRead)]
    [InlineData(Rights.PagesRead)]
    [InlineData(Rights.TaxonomyManage)]
    [InlineData(Rights.SettingsManage)]
    public async Task A_person_who_manages_or_reads_what_the_forms_edit_gets_the_choices(string right)
    {
        var result = await new LookupsController(new Provider(), new Held(Rights.AdminAccess, right)).Options("https://x/Role", null, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
