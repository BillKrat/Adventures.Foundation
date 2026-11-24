using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// The profile fields BlogEngine has and ours lacked: About me, Website and a Picture address. They are text a person types about themselves, so the rules
/// keep them to what can be shown safely: a website and a picture are plain http(s) addresses (or a path on this site), never script or data addresses.
/// </summary>
public sealed class ProfileFieldsTests : IAsyncLifetime
{
    private IUserBll _bll = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed.nq"));
        await store.SeedFromFileAsync(Path.Combine(seeds, "seed-027-profile-fields.nq"));
        var schema = SchemaDal.Load(await store.QueryAsync(), UserConstants.SchemaIri);
        var repository = new NQuadEntityRepository<User>(store, schema, UserConstants.BaseIri, UserConstants.TypeIri, "https://global-webnet.com/graph/test", s => new User(s));
        _bll = new UserBll(new EntityDal<User>(repository));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<User> AdminWith(string? about = null, string? website = null, string? picture = null)
    {
        var admin = (await _bll.FindByUserNameAsync("Admin"))!;
        foreach (var (name, value) in new[] { ("AboutMe", about), ("Website", website), ("Picture", picture) })
        {
            if (value is not null)
            {
                admin.Set(name, Guid.NewGuid().ToString(), value);
            }
        }

        return admin;
    }

    [Fact]
    public async Task TheSchemaOffersTheThreeFields_SoTheProfilePageShowsThem()
    {
        var admin = (await _bll.FindByUserNameAsync("Admin"))!;

        var names = admin.Fields.Select(f => f.Name).ToList();
        Assert.Contains("AboutMe", names);
        Assert.Contains("Website", names);
        Assert.Contains("Picture", names);
    }

    [Fact]
    public async Task APersonCanSaveAboutMeAWebsiteAndAPicture()
    {
        var admin = await AdminWith("I write about MCP.", "https://example.org/me", "https://example.org/me.png");

        await _bll.UpdateAsync(admin);

        var saved = (await _bll.FindByUserNameAsync("Admin"))!;
        Assert.Equal(("I write about MCP.", "https://example.org/me", "https://example.org/me.png"), (saved.GetValue("AboutMe"), saved.GetValue("Website"), saved.GetValue("Picture")));
    }

    [Fact]
    public async Task AllThreeAreOptional_AndAPicturePathOnThisSiteIsFine()
    {
        await _bll.UpdateAsync(await AdminWith());
        await _bll.UpdateAsync(await AdminWith(picture: "/media/me.png"));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ftp://example.org/x")]
    [InlineData("example.org")]
    [InlineData("https://example.org/a b")]
    [InlineData("https://user:pw@example.org/")]
    [InlineData("https://example.org/\"><script>")]
    public async Task AWebsite_IsAnHttpOrHttpsAddress_NothingElse(string website)
    {
        var admin = await AdminWith(website: website);
        var error = await Assert.ThrowsAsync<InputValidationException>(() => _bll.UpdateAsync(admin));

        Assert.Contains("Website", error.Errors.Keys);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("//evil.example/x.png")]
    [InlineData("mailto:a@b.c")]
    [InlineData("pic.png")]
    public async Task APicture_IsAnHttpAddressOrAPathOnThisSite_NeverScriptOrData(string picture)
    {
        var admin = await AdminWith(picture: picture);
        var error = await Assert.ThrowsAsync<InputValidationException>(() => _bll.UpdateAsync(admin));

        Assert.Contains("Picture", error.Errors.Keys);
    }

    [Fact]
    public async Task TheLengthsAreLimited()
    {
        var longAbout = await AdminWith(about: new string('x', UserBll.MaxAboutLength + 1));
        var longSite = await AdminWith(website: "https://example.org/" + new string('a', UserBll.MaxAddressLength));
        var about = await Assert.ThrowsAsync<InputValidationException>(() => _bll.UpdateAsync(longAbout));
        var site = await Assert.ThrowsAsync<InputValidationException>(() => _bll.UpdateAsync(longSite));

        Assert.Contains("AboutMe", about.Errors.Keys);
        Assert.Contains("Website", site.Errors.Keys);
        await _bll.UpdateAsync(await AdminWith(about: new string('x', UserBll.MaxAboutLength)));
    }
}
