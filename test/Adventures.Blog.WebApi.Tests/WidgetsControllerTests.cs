using System.Reflection;
using Adventures.Blog;
using Adventures.Blog.WebApi;
using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.MEF;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Blog.WebApi.Tests;

/// <summary>The Widgets screen's API (needs the site-settings right) and the public read of a zone (anonymous, rendered HTML only).</summary>
public sealed class WidgetsControllerTests
{
    private sealed class FakeRenderer : IWidgetRenderer
    {
        public Task<IReadOnlyList<WidgetInfo>> AvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WidgetInfo>>([new WidgetInfo("p", new WidgetDefinition("plain", "Plain", "A plain widget."))]);

        public Task<string?> RenderAsync(string widgetKey, string zone, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default) => Task.FromResult<string?>("<p>x</p>");
    }

    private sealed class FakePlacements : IWidgetPlacements
    {
        private readonly WidgetPlacementModel _one = new("id1", "left", "plain", null, new Dictionary<string, string>(), 1, true);

        public (string Zone, string Key)? Added { get; private set; }

        public int? MovedBy { get; private set; }

        public bool Missing { get; set; }

        public bool Refuse { get; set; }

        public Task<IReadOnlyList<WidgetPlacementModel>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WidgetPlacementModel>>([_one]);

        public Task<WidgetPlacementModel> AddAsync(string zone, string widgetKey, string? title, IReadOnlyDictionary<string, string>? settings, CancellationToken cancellationToken = default)
        {
            if (Refuse)
            {
                throw new BusinessRuleException("That widget cannot be placed.");
            }

            Added = (zone, widgetKey);
            return Task.FromResult(_one with { Zone = zone, WidgetKey = widgetKey });
        }

        public Task<WidgetPlacementModel?> UpdateAsync(string id, string? title, IReadOnlyDictionary<string, string>? settings, CancellationToken cancellationToken = default) =>
            Task.FromResult<WidgetPlacementModel?>(Missing ? null : _one with { Title = title });

        public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(!Missing);

        public Task<bool> MoveAsync(string id, int direction, CancellationToken cancellationToken = default)
        {
            MovedBy = direction;
            return Task.FromResult(!Missing);
        }

        public Task<IReadOnlyList<RenderedWidget>> RenderZoneAsync(string zone, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RenderedWidget>>([new RenderedWidget("id1", "plain", "Plain", "<p>x</p>")]);
    }

    private static WidgetsController Admin(FakePlacements? placements = null) => new(placements ?? new FakePlacements(), new FakeRenderer());

    [Fact]
    public async Task TheOverview_HasTheAvailableWidgets_ThePlacements_AndTheFourZones()
    {
        var overview = Assert.IsType<WidgetsOverview>(Assert.IsType<OkObjectResult>((await Admin().Overview(CancellationToken.None)).Result).Value);

        Assert.Equal("plain", Assert.Single(overview.Available).Definition.Key);
        Assert.Equal("id1", Assert.Single(overview.Placements).Id);
        Assert.Equal(["header", "left", "right", "footer"], overview.Zones);
    }

    [Fact]
    public async Task Placing_AWidget_StoresItAndAnswersThePlacement()
    {
        var placements = new FakePlacements();

        var result = await Admin(placements).Place(new NewPlacement("right", "plain", "T", null), CancellationToken.None);

        Assert.Equal(("right", "plain"), placements.Added);
        Assert.Equal("right", Assert.IsType<WidgetPlacementModel>(Assert.IsType<OkObjectResult>(result).Value).Zone);
    }

    [Fact]
    public async Task ARefusedPlacement_IsAConflict_WithTheReason()
    {
        var result = await Admin(new FakePlacements { Refuse = true }).Place(new NewPlacement("left", "plain", null, null), CancellationToken.None);

        Assert.Equal(409, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task ChangingMovingAndRemoving_AnUnknownPlacement_IsNotFound()
    {
        var controller = Admin(new FakePlacements { Missing = true });

        Assert.IsType<NotFoundResult>(await controller.Change("nope", new PlacementChange("T", null), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Move("nope", new MovePlacement(1), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Remove("nope", CancellationToken.None));
    }

    [Fact]
    public async Task ChangingMovingAndRemoving_AKnownPlacement_Succeeds()
    {
        var placements = new FakePlacements();
        var controller = Admin(placements);

        Assert.IsType<OkObjectResult>(await controller.Change("id1", new PlacementChange("New", null), CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.Move("id1", new MovePlacement(-1), CancellationToken.None));
        Assert.Equal(-1, placements.MovedBy);
        Assert.IsType<NoContentResult>(await controller.Remove("id1", CancellationToken.None));
    }

    [Fact]
    public async Task ThePublicZone_IsTheRenderedWidgets_AndAnUnknownZoneIsNotFound()
    {
        var controller = new PublicWidgetsController(new FakePlacements());

        var shown = Assert.IsAssignableFrom<IReadOnlyList<RenderedWidget>>(Assert.IsType<OkObjectResult>((await controller.Zone("Footer", CancellationToken.None)).Result).Value);

        Assert.Equal("<p>x</p>", Assert.Single(shown).Html);
        Assert.IsType<NotFoundResult>((await controller.Zone("sidebar", CancellationToken.None)).Result);
    }

    [Fact]
    public void TheAdminApi_NeedsTheSettingsRight_AndNoActionIsAnonymous_WhileThePublicReadIsAnonymousOnlyUnderApiPublic()
    {
        var admin = typeof(WidgetsController);
        Assert.Contains(admin.GetCustomAttributes<RequireRightAttribute>(), a => a.Right == Rights.SettingsManage);
        Assert.Contains(admin.GetCustomAttributes<RouteAttribute>(), a => a.Template == "api/widgets");
        Assert.All(admin.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>()));

        var open = typeof(PublicWidgetsController);
        Assert.NotEmpty(open.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Contains(open.GetCustomAttributes<RouteAttribute>(), a => a.Template == "api/public/widgets");
    }
}
