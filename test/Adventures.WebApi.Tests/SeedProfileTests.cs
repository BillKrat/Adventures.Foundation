using Adventures.WebApi;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Adventures.WebApi.Tests;

/// <summary>Which baseline a new store gets: a customer's install (schemas and roles only) or our development data. Development keeps the full baseline unless told otherwise; a deployed site is an install.</summary>
public sealed class SeedProfileTests
{
    private static IConfiguration Config(string? profile = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(profile is null ? [] : new Dictionary<string, string?> { ["Seed:Profile"] = profile }).Build();

    [Theory]
    [InlineData(false, null, "seed-install.nq")]
    [InlineData(true, null, "seed.nq")]
    [InlineData(false, "dev", "seed.nq")]
    [InlineData(true, "install", "seed-install.nq")]
    [InlineData(false, "INSTALL", "seed-install.nq")]
    [InlineData(false, "Dev", "seed.nq")]
    public void TheBaselineFile_FollowsTheEnvironmentAndTheExplicitSetting(bool development, string? setting, string expected) =>
        Assert.Equal(expected, SeedProfile.BaselineFile(Config(setting), development));

    [Theory]
    [InlineData("production")]
    [InlineData("")]
    [InlineData("everything")]
    public void AnUnknownSetting_NeverShipsOurData_ADeployedSiteStaysAnInstall(string setting) =>
        Assert.Equal("seed-install.nq", SeedProfile.BaselineFile(Config(setting), development: false));
}
