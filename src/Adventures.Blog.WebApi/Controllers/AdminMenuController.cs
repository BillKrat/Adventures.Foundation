using Adventures.Blog;
using Adventures.Entities;
using Adventures.MEF;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Adventures.Blog.WebApi;

/// <summary>The admin menu names (sections and entries) as server text: declared with their English name so a language file can translate them (R3 of the host reuse review: the entries are contributed by the features).</summary>
public sealed class AdminMenuTextSource(AdminMenuCatalog catalog) : IServerTextSource
{
    public IEnumerable<ServerTextDeclaration> Declarations =>
        catalog.All.Select(item => new ServerTextDeclaration($"menu.{item.Id}", item.Label))
            .Concat(catalog.All.Select(item => item.Section).Distinct(StringComparer.Ordinal).Select(section => new ServerTextDeclaration($"menu.section.{section}", section)));
}

public static class AdminMenuLocalization
{
    /// <summary>The menu with its section and entry names in the site language; a name with no translation keeps its English text.</summary>
    public static AdminMenuModel Localize(AdminMenuModel menu, IStringLocalizer text) =>
        menu with
        {
            Sections = menu.Sections
                .Select(section => section with
                {
                    Label = Pick(text, $"menu.section.{section.Id}", section.Label),
                    Items = section.Items.Select(item => item with { Label = Pick(text, $"menu.{item.Id}", item.Label) }).ToList(),
                })
                .ToList(),
        };

    private static string Pick(IStringLocalizer text, string key, string english)
    {
        var found = text[key];
        return found.ResourceNotFound ? english : found.Value;
    }
}

/// <summary>The admin navigation for the caller (empty for anyone who is not an administrator), each entry from the feature that owns the screen, with the counts the features contribute as badges.</summary>
[ApiController]
[Route("api/admin/menu")]
[Authorize]
public sealed class AdminMenuController(AdminMenuCatalog catalog, AdminCounts counts, IPlugins plugins, IRightsAuthorizer authorizer, IRequestLanguage language, IStringLocalizer text) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdminMenuModel>> Get(CancellationToken cancellationToken)
    {
        var rights = await authorizer.RightsOfAsync(User, cancellationToken);
        var badges = rights.Contains(Rights.AdminAccess) ? await counts.GetAllAsync(cancellationToken) : null;
        await language.LoadAsync(cancellationToken);
        return Ok(AdminMenuLocalization.Localize(AdminMenuBuilder.Build(catalog.Visible(await plugins.EnabledKeysAsync(cancellationToken)), User, badges, rights), text));
    }
}
