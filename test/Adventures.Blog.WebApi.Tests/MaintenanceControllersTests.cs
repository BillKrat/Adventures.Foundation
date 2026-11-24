using System.Reflection;
using System.Text.Json;
using Adventures.Blog;
using Adventures.Blog.WebApi;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Blog.WebApi.Tests;

public sealed class MaintenanceControllersTests
{
    private sealed class FakeExporter : IContentExporter
    {
        public Task<ContentExportDocument> ExportAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContentExportDocument(ContentExport.FormatName, 1, new DateTimeOffset(2026, 11, 22, 12, 30, 0, TimeSpan.Zero), [], []));
    }

    [Fact]
    public async Task TheExport_IsAJsonFileDownload_NamedWithTheDate()
    {
        var result = Assert.IsType<FileContentResult>(await new ExportController(new FakeExporter()).Content(CancellationToken.None));

        Assert.Equal("application/json", result.ContentType);
        Assert.Equal("content-export-20261122-1230.json", result.FileDownloadName);
        using var parsed = JsonDocument.Parse(result.FileContents);
        Assert.Equal("adventures-content-export", parsed.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public void ClearingTheCache_FiresTheInvalidator_AndWorksWithoutOne()
    {
        var caches = new TenantCacheInvalidator();
        var token = caches.Token;
        Assert.False(token.HasChanged);

        Assert.IsType<NoContentResult>(new CacheController(caches).Clear());

        Assert.True(token.HasChanged);
        Assert.IsType<NoContentResult>(new CacheController().Clear());
    }

    [Theory]
    [InlineData(typeof(ExportController))]
    [InlineData(typeof(CacheController))]
    public void BothRoutes_NeedTheSettingsRight_AndAreNeverAnonymous(Type controller)
    {
        Assert.Contains(controller.GetCustomAttributes<RequireRightAttribute>(), a => a.Right == Rights.SettingsManage);
        Assert.All(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>()));
    }
}
