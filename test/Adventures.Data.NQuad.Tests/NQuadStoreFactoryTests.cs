using Xunit;

namespace Adventures.Data.NQuad.Tests;

/// <summary>The setting that picks the PostgreSQL layout: today's text table unless the dictionary layout is asked for by name; a typo stops the start instead of silently using a different store.</summary>
public sealed class NQuadStoreFactoryTests
{
    private const string Connection = "Host=unused;Database=unused";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Text")]
    [InlineData("text")]
    public void The_default_is_todays_text_table(string? layout) =>
        Assert.IsType<NpgsqlNQuadStore>(NQuadStoreFactory.CreatePostgres(Connection, layout, null, null));

    [Theory]
    [InlineData("Dictionary")]
    [InlineData("dictionary")]
    public void The_dictionary_layout_is_chosen_by_name(string layout) =>
        Assert.IsType<NpgsqlDictionaryNQuadStore>(NQuadStoreFactory.CreatePostgres(Connection, layout, null, null));

    [Theory]
    [InlineData("Dictionnary")]
    [InlineData("Hybrid")]
    public void An_unknown_layout_stops_the_start_naming_the_setting(string layout)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => NQuadStoreFactory.CreatePostgres(Connection, layout, null, null));

        Assert.Contains("NQuadStore:Layout", failure.Message);
    }

    [Fact]
    public void A_bad_table_or_prefix_name_is_refused_by_the_store_it_is_given_to()
    {
        Assert.Throws<ArgumentException>(() => NQuadStoreFactory.CreatePostgres(Connection, "Text", "Bad Name", null));
        Assert.Throws<ArgumentException>(() => NQuadStoreFactory.CreatePostgres(Connection, "Dictionary", null, "Bad Name"));
    }
}
