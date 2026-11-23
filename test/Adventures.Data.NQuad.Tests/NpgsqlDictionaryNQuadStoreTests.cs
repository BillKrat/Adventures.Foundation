using Npgsql;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// What is specific to the hybrid-dictionary layout (decision 2026-11, quad storage): which objects go to the dictionary, that nothing about the text changes on the way through, that the
/// dictionary only grows, that a failed batch leaves no stale id in the cache, and the bounded term cache. The shared contract (same behaviour as the text table) is run by
/// <see cref="PostgresDictionaryNQuadStoreContractTests"/>. Needs "ConnectionStrings:Postgres" like the other live tests and skips (passes) without it.
/// </summary>
public sealed class NpgsqlDictionaryNQuadStoreTests : IAsyncLifetime
{
    private static readonly PostgresConnectionFixture Connection = new();
    private string _prefix = string.Empty;
    private NpgsqlDictionaryNQuadStore? _store;

    public async Task InitializeAsync()
    {
        if (Connection.ConnectionString is null) return;

        _prefix = "dqs_" + Guid.NewGuid().ToString("N");
        _store = new NpgsqlDictionaryNQuadStore(Connection.ConnectionString, _prefix);
        await _store.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (Connection.ConnectionString is null) return;

        await using var connection = new NpgsqlConnection(Connection.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP TABLE IF EXISTS {_prefix}_quads; DROP TABLE IF EXISTS {_prefix}_terms;", connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Connection.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static NQuad Q(string s, string p, string o, string? g = "https://example.org/graph/a") => new(Guid.NewGuid(), s, p, o, g);

    [Theory]
    [InlineData("https://example.org/id/user/1", true)]
    [InlineData("http://example.org/x", true)]
    [InlineData("urn:adventures:system", true)]
    [InlineData("Hello world", false)]
    [InlineData("", false)]
    [InlineData("ftp://example.org/x", false)]
    [InlineData("2026-11-23T12:00:00Z", false)]
    public void IsIri_decides_only_from_the_text(string text, bool expected) =>
        Assert.Equal(expected, NpgsqlDictionaryNQuadStore.IsIri(text));

    [Fact]
    public void IsIri_keeps_an_over_long_address_out_of_the_dictionary()
    {
        Assert.True(NpgsqlDictionaryNQuadStore.IsIri("https://example.org/" + new string('a', 1900)));
        Assert.False(NpgsqlDictionaryNQuadStore.IsIri("https://example.org/" + new string('a', 2100)));
        Assert.False(NpgsqlDictionaryNQuadStore.IsIri("https://example.org/" + new string('é', 1000)), "counted in UTF-8 bytes, not characters");
    }

    [Fact]
    public async Task An_address_that_is_really_a_literal_comes_back_exactly_as_written_and_is_found_by_value()
    {
        if (_store is null) return;
        var website = Q("https://example.org/id/person/1", "https://example.org/ont#Website", "https://visitor.example.net/home?x=1");
        await _store.InsertAsync(website);

        var byObject = await _store.QueryAsync(@object: "https://visitor.example.net/home?x=1");

        Assert.Equal(website, Assert.Single(byObject));
    }

    [Fact]
    public async Task Text_literals_stay_in_the_quad_row_and_do_not_grow_the_dictionary()
    {
        if (_store is null) return;
        await _store.InsertManyAsync([Q("https://example.org/id/post/1", "https://example.org/ont#Title", "A title"), Q("https://example.org/id/post/1", "https://example.org/ont#Body", new string('x', 5000))]);

        // subject, predicate x 2, graph: four dictionary terms for two quads; neither literal is a term
        Assert.Equal(4, await ScalarAsync($"SELECT count(*) FROM {_prefix}_terms"));
        Assert.Equal(2, await ScalarAsync($"SELECT count(*) FROM {_prefix}_quads WHERE lit IS NOT NULL AND o IS NULL"));
    }

    [Fact]
    public async Task An_object_that_is_an_address_is_stored_as_a_term_and_found_by_value()
    {
        if (_store is null) return;
        var link = Q("https://example.org/id/post/1", "https://example.org/ont#Author", "https://example.org/id/user/7");
        await _store.InsertAsync(link);

        Assert.Equal(1, await ScalarAsync($"SELECT count(*) FROM {_prefix}_quads WHERE o IS NOT NULL AND lit IS NULL"));
        Assert.Equal(link, Assert.Single(await _store.QueryAsync(@object: "https://example.org/id/user/7")));
    }

    [Fact]
    public async Task A_term_that_was_never_written_matches_nothing_and_creates_nothing()
    {
        if (_store is null) return;
        await _store.InsertAsync(Q("https://example.org/id/post/1", "https://example.org/ont#Title", "A"));
        var before = await ScalarAsync($"SELECT count(*) FROM {_prefix}_terms");

        Assert.Empty(await _store.QueryAsync(subject: "https://example.org/id/nobody"));
        Assert.Empty(await _store.QueryAsync(graph: "https://example.org/graph/unknown"));
        Assert.Empty(await _store.QueryAsync(@object: "https://example.org/id/unknown"));
        Assert.Empty(await _store.QueryBySubjectsAsync(["https://example.org/id/nobody"]));

        Assert.Equal(before, await ScalarAsync($"SELECT count(*) FROM {_prefix}_terms"));
    }

    [Fact]
    public async Task A_subject_predicate_or_graph_too_long_for_the_dictionary_is_refused_and_nothing_is_stored()
    {
        if (_store is null) return;
        var tooLong = "https://example.org/" + new string('a', 2100);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.InsertAsync(Q(tooLong, "https://example.org/p", "x")));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.InsertAsync(Q("https://example.org/s", tooLong, "x")));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.InsertAsync(Q("https://example.org/s", "https://example.org/p", "x", tooLong)));

        Assert.Equal(0, await _store.CountAsync());
    }

    [Fact]
    public async Task A_failed_batch_leaves_no_stale_term_in_the_cache_so_the_next_batch_works()
    {
        if (_store is null) return;
        var duplicate = Guid.NewGuid();
        await _store.InsertAsync(new NQuad(duplicate, "https://example.org/id/old", "https://example.org/p", "x", null));

        // the batch adds new terms (the new subject) and then fails on the duplicate id: the transaction rolls back, taking the new term with it
        var failing = new[]
        {
            Q("https://example.org/id/new-subject", "https://example.org/p", "y", null),
            new NQuad(duplicate, "https://example.org/id/other", "https://example.org/p", "z", null),
        };
        await Assert.ThrowsAnyAsync<Exception>(() => _store.InsertManyAsync(failing));

        // the same new subject again must get a fresh, valid id (a cache that kept the rolled-back id would write a quad pointing nowhere)
        var again = Q("https://example.org/id/new-subject", "https://example.org/p", "y", null);
        await _store.InsertAsync(again);

        Assert.Equal(again, Assert.Single(await _store.QueryAsync(subject: "https://example.org/id/new-subject")));
    }

    [Fact]
    public async Task Purge_removes_the_quads_but_keeps_the_dictionary_so_cached_ids_stay_valid()
    {
        if (_store is null) return;
        await _store.InsertAsync(Q("https://example.org/id/post/1", "https://example.org/p", "x"));
        var terms = await ScalarAsync($"SELECT count(*) FROM {_prefix}_terms");

        Assert.Equal(1, await _store.PurgeAsync());
        Assert.Equal(0, await _store.CountAsync());
        Assert.Equal(terms, await ScalarAsync($"SELECT count(*) FROM {_prefix}_terms"));

        var again = Q("https://example.org/id/post/1", "https://example.org/p", "x");
        await _store.InsertAsync(again);
        Assert.Equal(again, Assert.Single(await _store.QueryAsync()));
    }

    [Fact]
    public async Task A_second_store_instance_reads_what_the_first_wrote_the_cache_is_only_a_cache()
    {
        if (_store is null) return;
        var written = Q("https://example.org/id/post/9", "https://example.org/p", "https://example.org/id/user/3");
        await _store.InsertAsync(written);

        var other = new NpgsqlDictionaryNQuadStore(Connection.ConnectionString!, _prefix, maxCachedTerms: 2);

        Assert.Equal(written, Assert.Single(await other.QueryAsync(subject: "https://example.org/id/post/9")));
        Assert.Equal(written, Assert.Single(await other.QueryAsync()));
    }

    [Fact]
    public async Task A_tiny_cache_changes_nothing_but_speed()
    {
        if (_store is null) return;
        var tiny = new NpgsqlDictionaryNQuadStore(Connection.ConnectionString!, _prefix, maxCachedTerms: 3);
        var quads = Enumerable.Range(0, 40).Select(i => Q($"https://example.org/id/post/{i}", $"https://example.org/p{i % 5}", i % 2 == 0 ? $"https://example.org/id/user/{i}" : $"title {i}", $"https://example.org/graph/{i % 3}")).ToList();

        await tiny.InsertManyAsync(quads);

        var back = await tiny.QueryAsync();
        Assert.Equal(quads.OrderBy(q => q.Id), back.OrderBy(q => q.Id));
        Assert.Equal(quads.Count(q => q.Graph == "https://example.org/graph/1"), (await tiny.QueryAsync(graph: "https://example.org/graph/1")).Count);
    }

    [Fact]
    public void The_term_cache_is_bounded_and_drops_the_least_recently_used_term()
    {
        var cache = new TermCache(2);
        cache.Add("a", 1);
        cache.Add("b", 2);
        Assert.True(cache.TryGetId("a", out _));

        cache.Add("c", 3);

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGetId("a", out var a) && a == 1);
        Assert.True(cache.TryGetValue(3, out var c) && c == "c");
        Assert.False(cache.TryGetId("b", out _));
        Assert.False(cache.TryGetValue(2, out _));
    }

    [Fact]
    public void A_cache_of_no_capacity_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TermCache(0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("with space")]
    [InlineData("a;drop table x")]
    public void The_table_prefix_must_be_a_plain_lower_case_identifier(string prefix)
    {
        Assert.Throws<ArgumentException>(() => new NpgsqlDictionaryNQuadStore("Host=unused", prefix));
    }
}
