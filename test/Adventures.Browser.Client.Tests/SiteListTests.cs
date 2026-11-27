using Xunit;

namespace Adventures.Browser.Client.Tests;

public sealed class SiteListTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("browser-settings-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private BrowserSettingsFile File_(string? content = null)
    {
        var path = Path.Combine(_folder, "sub", "settings.json");
        if (content is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return new BrowserSettingsFile(path);
    }

    [Theory]
    [InlineData("One.Example", "one.example")]
    [InlineData("  https://one.example/blog  ", "one.example")]
    [InlineData("http://one.example:8080/", "one.example")]
    [InlineData("localhost", "localhost")]
    [InlineData("https://ann:pw@one.example", null)]
    [InlineData("ftp://one.example", null)]
    [InlineData("one.example/path", null)]
    [InlineData("two words", null)]
    [InlineData("", null)]
    public void A_typed_domain_becomes_a_host_name_or_nothing(string typed, string? expected) => Assert.Equal(expected, DomainName.Normalize(typed));

    [Fact]
    public async Task The_list_is_saved_and_read_back_and_the_api_address_is_kept()
    {
        var file = File_("""{ "apiBaseUrl": "https://browser.example/", "domains": [ "One.example", "one.example", "bad name" ] }""");

        Assert.Equal(["one.example"], await file.LoadAsync());
        await file.SaveAsync(["one.example", "two.example"]);

        Assert.Equal(["one.example", "two.example"], await file.LoadAsync());
        Assert.Equal("https://browser.example/", file.Read().ApiBaseUrl);
    }

    [Fact]
    public async Task No_file_is_an_empty_list_and_saving_makes_it()
    {
        var file = File_();

        Assert.Empty(await file.LoadAsync());
        await file.SaveAsync(["one.example"]);

        Assert.Equal(["one.example"], await file.LoadAsync());
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_reported_and_never_overwritten()
    {
        var file = File_("{ not json");

        Assert.Contains(file.Path, (await Assert.ThrowsAsync<InvalidOperationException>(() => file.LoadAsync())).Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => file.SaveAsync(["one.example"]));
        Assert.Equal("{ not json", File.ReadAllText(file.Path));
    }
}
