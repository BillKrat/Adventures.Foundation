using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>
/// Language files (row 13, stage 2): one stored table of label texts per language and tenant, saved after checks (a bad file can never reach the
/// screen: codes, keys and texts are limited) and served as the table of the site language, English when none is stored.
/// </summary>
public sealed class LanguageFilesTests : IAsyncLifetime
{
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        await store.SeedFromFileAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed-026-language-files.nq"));
        var quads = await store.QueryAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, LanguageConstants.SchemaIri), LanguageConstants.BaseIri, LanguageConstants.TypeIri, LanguageConstants.DefaultGraph, s => new LanguageFile(s));
        services.AddScoped<IEntityBll<LanguageFile>>(p => new EntityBll<LanguageFile>(p.GetRequiredService<IEntityDal<LanguageFile>>()));
        services.AddScoped<ILanguageFiles, LanguageFiles>();
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ILanguageFiles Files() => _services.CreateScope().ServiceProvider.GetRequiredService<ILanguageFiles>();

    [Fact]
    public async Task ASavedFile_IsServedByItsCode_AndACodeWithNoFileServesNothing()
    {
        await Files().SaveAsync("DE", new Dictionary<string, string> { ["save"] = "Speichern", ["removeNamed"] = "{0} entfernen" });

        var table = await Files().GetAsync("de");
        Assert.Equal("Speichern", table["save"]);
        Assert.Equal("{0} entfernen", table["removeNamed"]);
        Assert.Empty(await Files().GetAsync("fr"));
        Assert.Empty(await Files().GetAsync("not a code"));
    }

    [Fact]
    public async Task SavingAgain_ReplacesTheTable_NeverAddsASecondFile()
    {
        await Files().SaveAsync("de", new Dictionary<string, string> { ["save"] = "Speichern", ["delete"] = "Löschen" });
        await Files().SaveAsync("de", new Dictionary<string, string> { ["save"] = "Sichern" });

        var table = await Files().GetAsync("de");
        Assert.Equal(new Dictionary<string, string> { ["save"] = "Sichern" }, table);
        Assert.Single(await Files().ListAsync());
    }

    [Fact]
    public async Task TheList_ShowsEachLanguageWithHowManyTextsItHolds()
    {
        await Files().SaveAsync("de", new Dictionary<string, string> { ["save"] = "a", ["delete"] = "b" });
        await Files().SaveAsync("fr", new Dictionary<string, string> { ["save"] = "c" });

        var list = await Files().ListAsync();

        Assert.Equal([("de", 2), ("fr", 1)], list.Select(f => (f.Language, f.Count)));
    }

    [Fact]
    public async Task ARemovedFile_ServesNothing_AndRemovingAnUnknownCodeIsReported()
    {
        await Files().SaveAsync("de", new Dictionary<string, string> { ["save"] = "a" });

        Assert.True(await Files().RemoveAsync("de"));
        Assert.Empty(await Files().GetAsync("de"));
        Assert.False(await Files().RemoveAsync("de"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("d")]
    [InlineData("german language")]
    [InlineData("de_DE!")]
    [InlineData("../x")]
    public async Task ABadLanguageCode_IsRefused(string code)
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Files().SaveAsync(code, new Dictionary<string, string> { ["save"] = "a" }));
    }

    [Fact]
    public async Task AnAcceptedCode_MayCarryARegion_AndIsKeptInLowerCase()
    {
        await Files().SaveAsync("pt-BR", new Dictionary<string, string> { ["save"] = "Salvar" });

        Assert.Equal("Salvar", (await Files().GetAsync("PT-br"))["save"]);
        Assert.Equal("pt-br", Assert.Single(await Files().ListAsync()).Language);
    }

    [Fact]
    public async Task ABadFile_IsRefusedWhole_KeysAndTextsAreLimited()
    {
        var tooLong = new string('x', LanguageConstants.MaxTextLength + 1);
        var tooMany = Enumerable.Range(0, LanguageConstants.MaxKeys + 1).ToDictionary(i => $"key{i}", _ => "x");

        await Assert.ThrowsAsync<BusinessRuleException>(() => Files().SaveAsync("de", new Dictionary<string, string> { ["save"] = tooLong }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Files().SaveAsync("de", new Dictionary<string, string> { ["not a key!"] = "x" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Files().SaveAsync("de", new Dictionary<string, string> { ["save"] = "   " }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Files().SaveAsync("de", new Dictionary<string, string> { ["save"] = "bad\u0007control" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Files().SaveAsync("de", tooMany));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Files().SaveAsync("de", null!));
        Assert.Empty(await Files().ListAsync());   // nothing half-saved
    }

    [Fact]
    public async Task EnglishMayBeOverriddenToo_SoASiteCanRewordTheDefaults()
    {
        await Files().SaveAsync("en", new Dictionary<string, string> { ["save"] = "Store" });

        Assert.Equal("Store", (await Files().GetAsync("en"))["save"]);
    }

    [Fact]
    public async Task AProposal_IsKeptApart_ItNeverChangesWhatTheSiteServes()
    {
        await Files().SaveAsync("de", new Dictionary<string, string> { ["save"] = "Speichern" });

        await Files().ProposeAsync("de", new Dictionary<string, string> { ["save"] = "Sichern", ["delete"] = "Loeschen" });

        Assert.Equal("Speichern", (await Files().GetAsync("de"))["save"]);
        Assert.Equal([("de", 1)], (await Files().ListAsync()).Select(f => (f.Language, f.Count)));
        var proposal = Assert.Single(await Files().ListProposalsAsync());
        Assert.Equal(("de", 2), (proposal.Language, proposal.Count));
        Assert.Equal("Sichern", (await Files().GetProposalAsync("de"))!["save"]);
    }

    [Fact]
    public async Task AProposalForALanguageWithNoFile_IsNotServedEither()
    {
        await Files().ProposeAsync("fr", new Dictionary<string, string> { ["save"] = "Enregistrer" });

        Assert.Empty(await Files().GetAsync("fr"));
        Assert.Empty(await Files().ListAsync());
        Assert.Single(await Files().ListProposalsAsync());
    }

    [Fact]
    public async Task ProposingAgain_ReplacesTheProposal_AndTheSameChecksApply()
    {
        await Files().ProposeAsync("fr", new Dictionary<string, string> { ["save"] = "a" });
        await Files().ProposeAsync("fr", new Dictionary<string, string> { ["save"] = "b", ["delete"] = "c" });

        Assert.Equal(2, Assert.Single(await Files().ListProposalsAsync()).Count);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Files().ProposeAsync("bad code", new Dictionary<string, string> { ["save"] = "a" }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Files().ProposeAsync("fr", new Dictionary<string, string> { ["save"] = "  " }));
        Assert.Equal(2, Assert.Single(await Files().ListProposalsAsync()).Count);   // the refused ones changed nothing
    }

    [Fact]
    public async Task ApplyingAProposal_MakesItTheServedFile_AndRemovesTheProposal()
    {
        await Files().SaveAsync("de", new Dictionary<string, string> { ["save"] = "Speichern" });
        await Files().ProposeAsync("de", new Dictionary<string, string> { ["save"] = "Sichern" });

        Assert.True(await Files().ApplyProposalAsync("de"));

        Assert.Equal("Sichern", (await Files().GetAsync("de"))["save"]);
        Assert.Empty(await Files().ListProposalsAsync());
        Assert.Single(await Files().ListAsync());
        Assert.False(await Files().ApplyProposalAsync("de"));   // nothing left to apply
    }

    [Fact]
    public async Task DiscardingAProposal_LeavesTheServedFileAlone()
    {
        await Files().SaveAsync("de", new Dictionary<string, string> { ["save"] = "Speichern" });
        await Files().ProposeAsync("de", new Dictionary<string, string> { ["save"] = "Sichern" });

        Assert.True(await Files().DiscardProposalAsync("de"));

        Assert.Equal("Speichern", (await Files().GetAsync("de"))["save"]);
        Assert.Empty(await Files().ListProposalsAsync());
        Assert.False(await Files().DiscardProposalAsync("de"));
        Assert.Null(await Files().GetProposalAsync("de"));
    }

    [Fact]
    public async Task RemovingAFile_DoesNotRemoveItsProposal()
    {
        await Files().SaveAsync("de", new Dictionary<string, string> { ["save"] = "a" });
        await Files().ProposeAsync("de", new Dictionary<string, string> { ["save"] = "b" });

        Assert.True(await Files().RemoveAsync("de"));

        Assert.Empty(await Files().GetAsync("de"));
        Assert.Single(await Files().ListProposalsAsync());
    }
}
