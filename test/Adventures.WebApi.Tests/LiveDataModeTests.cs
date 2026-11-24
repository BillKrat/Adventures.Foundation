using Adventures.WebApi;
// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Adventures.WebApi.Tests;

public class LiveDataModeTests
{
    private static ConfigurationManager Config(params (string Key, string Value)[] values)
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value));
        return config;
    }

    [Fact]
    public void Is_off_by_default_and_changes_nothing()
    {
        var config = Config(("ConnectionStrings:Postgres", "local"));

        Assert.False(LiveDataMode.Apply(config, isDevelopment: true));
        Assert.Equal("local", config["ConnectionStrings:Postgres"]);
    }

    [Fact]
    public void When_enabled_in_development_points_the_app_at_the_live_connection()
    {
        var config = Config(("LiveData:Enabled", "true"), ("ConnectionStrings:LiveData", "live"), ("ConnectionStrings:Postgres", "local"));

        Assert.True(LiveDataMode.Apply(config, isDevelopment: true));
        Assert.Equal("live", config["ConnectionStrings:Postgres"]);
    }

    [Fact]
    public void Refuses_to_run_outside_development()
    {
        var config = Config(("LiveData:Enabled", "true"), ("ConnectionStrings:LiveData", "live"));

        var error = Assert.Throws<InvalidOperationException>(() => LiveDataMode.Apply(config, isDevelopment: false));
        Assert.Contains("Development", error.Message);
    }

    [Fact]
    public void Refuses_without_a_live_connection_string()
    {
        var config = Config(("LiveData:Enabled", "true"), ("ConnectionStrings:Postgres", "local"));

        var error = Assert.Throws<InvalidOperationException>(() => LiveDataMode.Apply(config, isDevelopment: true));
        Assert.Contains("ConnectionStrings:LiveData", error.Message);
        Assert.Equal("local", config["ConnectionStrings:Postgres"]);
    }

    [Fact]
    public void Describe_names_host_and_database_but_never_the_password()
    {
        var text = LiveDataMode.Describe("Host=db.example;Port=6432;Database=blog;Username=u;Password=s3cret");

        Assert.Equal("db.example/blog", text);
        Assert.Equal("none", LiveDataMode.Describe(null));
    }

    [Fact]
    public void Seeding_is_applied_in_live_data_mode_unless_it_is_switched_off()
    {
        Assert.False(LiveDataMode.SkipSeeding(Config(("LiveData:Enabled", "true"))));
        Assert.True(LiveDataMode.SkipSeeding(Config(("LiveData:SkipSeeding", "true"))));
    }
}
