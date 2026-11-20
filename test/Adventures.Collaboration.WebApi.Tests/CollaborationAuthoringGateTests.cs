using Adventures.Collaboration.WebApi;
using System.Reflection;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

using Adventures.Blog.WebApi;

namespace Adventures.Collaboration.WebApi.Tests;

/// <summary>Reading an item's text (backlog 3b) goes through the same gates as every other collaboration authoring route.</summary>
public sealed class CollaborationAuthoringGateTests
{
    [Fact]
    public void ReadingAnItem_IsBehindTheAuthoringScope_AndTheCollaborationPlugins_AndIsNotAnonymous()
    {
        var type = typeof(CollaborationAuthoringController);
        var show = type.GetMethod("Show", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(show);
        Assert.Contains(show!.GetCustomAttributes<HttpGetAttribute>(), a => a.Template == "{id}");
        Assert.Empty(show.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "Scope:" + AuthoringController.RequiredScope);
        Assert.Contains(type.GetCustomAttributes<RequiresPluginAttribute>(), _ => true);
    }
}
