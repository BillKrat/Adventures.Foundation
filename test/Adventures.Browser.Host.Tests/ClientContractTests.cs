using System.Net.Http.Headers;
using System.Security.Claims;
using Adventures.Client;
using Adventures.Entities;
using Adventures.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Browser.Host.Tests;

/// <summary>
/// The desktop client's presenter against the real server contract (S9): the same grid and form JSON the Angular admin reads, over a real controller
/// pipeline, in memory. Proves the wire shape round-trips and that refusals reach the client as ApiException, with the rights check still on the server.
/// </summary>
public sealed class ClientContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ClientContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", "test");
            builder.UseSetting("Jwt:Audience", "test");
            builder.UseSetting("Jwt:SigningKey", "0123456789abcdef0123456789abcdef0123456789abcdef");
            builder.UseSetting("NQuadStore:Provider", "Memory");
        });
    }

    private HttpEntityPresenter<DynamicRecord> Presenter(string? roles)
    {
        var http = _factory.CreateClient();
        if (roles is not null)
        {
            var token = _factory.Services.GetRequiredService<IJwtTokenService>()
                .IssueToken(Guid.NewGuid().ToString(), [new Claim(ClaimTypes.Name, "tester"), new Claim(WellKnownClaims.Roles, roles)]);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        }

        return new HttpEntityPresenter<DynamicRecord>(http, "api/browser/roles");
    }

    [Fact]
    public async Task The_grid_comes_back_as_the_schema_driven_model()
    {
        var grid = await Presenter("TenantAdmin").GetGridAsync(new EntityGridQuery(Page: 1, PageSize: 10));

        Assert.NotEmpty(grid.Rows);
        Assert.Contains(grid.Schema.Fields, f => f.Name == "Name");
        Assert.Contains(grid.Rows, r => r.Values.TryGetValue("Name", out var name) && name == "Admin");
        Assert.Equal(1, grid.Paging!.Page);
    }

    [Fact]
    public async Task Create_read_update_and_delete_round_trip_through_the_presenter()
    {
        var presenter = Presenter("TenantAdmin");

        var created = await presenter.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["Name"] = "Desktop test role" }));
        var id = created.Entity.EntityId;
        Assert.False(string.IsNullOrEmpty(id));

        var updated = await presenter.UpdateAsync(id, new EntityDataModel(id, new Dictionary<string, string?> { ["Name"] = "Desktop test role (edited)" }));
        Assert.NotNull(updated);
        Assert.Equal("Desktop test role (edited)", (await presenter.GetFormAsync(id))!.Entity.Values["Name"]);

        Assert.True(await presenter.DeleteAsync(id));
        Assert.Null(await presenter.GetFormAsync(id));
    }

    [Fact]
    public async Task Without_a_token_the_client_sees_unauthorized_and_without_the_role_forbidden()
    {
        Assert.True((await Assert.ThrowsAsync<ApiException>(() => Presenter(null).GetGridAsync())).IsUnauthorized);
        Assert.True((await Assert.ThrowsAsync<ApiException>(() => Presenter("Editor").GetGridAsync())).IsForbidden);
    }
}
