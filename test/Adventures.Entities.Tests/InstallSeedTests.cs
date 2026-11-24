using System.Text.RegularExpressions;
using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// Onboarding stage 2: a customer's install must not ship our people and sites. The baseline seed (seed.nq) mixes the schemas and roles every install needs with our development data
/// (tenants, people, blogs, organisations and their assignments). The install seed is that file minus exactly that data, and a test keeps the two from drifting apart.
/// </summary>
public sealed class InstallSeedTests
{
    private static readonly Regex OurData = new("^<https://global-webnet\\.com/id/(tenant|user|blog|organization|tenant-role|organization-role)/", RegexOptions.CultureInvariant);

    private static string[] Lines(string file) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Sql", "seed", file)).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

    [Fact]
    public void TheInstallSeed_IsTheBaselineMinusOurTenantsPeopleBlogsAndAssignments_Exactly()
    {
        var expected = Lines("seed.nq").Where(l => !OurData.IsMatch(l)).ToArray();

        Assert.Equal(expected, Lines("seed-install.nq"));
    }

    [Fact]
    public void TheInstallSeed_ShipsNoTenantPersonBlogOrOrganisation()
    {
        var lines = Lines("seed-install.nq");

        Assert.DoesNotContain(lines, l => OurData.IsMatch(l));
        Assert.DoesNotContain(lines, l => l.Contains("global-webnet.com/id/user/", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("adventuresontheedge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheInstallSeed_KeepsWhatEveryInstallNeeds_TheUserSchemaAndTheRoles()
    {
        var lines = Lines("seed-install.nq");

        Assert.Contains(lines, l => l.StartsWith("<https://global-webnet.com/schema/User>", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("<https://global-webnet.com/id/role/", StringComparison.Ordinal));
        Assert.True(lines.Count(l => l.StartsWith("<https://global-webnet.com/id/role/", StringComparison.Ordinal)) >= 4);
    }

    [Fact]
    public async Task TheInstallSeed_LoadsAndStillDescribesAUser()
    {
        var store = new InMemoryNQuadStore();
        await store.SeedFromFileAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed-install.nq"));

        var schema = SchemaDal.Load(await store.QueryAsync(), UserConstants.SchemaIri);

        Assert.Contains(schema.Fields.Values, f => f.Name == "UserName");
    }
}
