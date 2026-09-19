using Adventures.Data;
using Xunit;

namespace Adventures.Data.Tests;

public class PostgresTripleStoreTests
{
    private static Triple SampleTriple(Guid? subject = null, string graph = "tenant:acme.com/org:my-blog/user:42") => new(
        Subject: subject ?? Guid.NewGuid(),
        Predicate: "favoriteColor",
        Object: "blue",
        ObjectDatatype: "xsd:string",
        Graph: graph);

    [Fact]
    public async Task AddTripleAsync_ExecutesUpsert()
    {
        var executor = new FakeSqlExecutor();
        var store = new PostgresTripleStore(executor);

        await store.AddTripleAsync(SampleTriple());

        Assert.Single(executor.ExecuteCalls);
        Assert.Contains("INSERT INTO entity_triples", executor.ExecuteCalls[0].Sql);
        Assert.Contains("ON CONFLICT", executor.ExecuteCalls[0].Sql);
    }

    [Fact]
    public async Task AddTripleAsync_ThrowsForNullTriple()
    {
        var store = new PostgresTripleStore(new FakeSqlExecutor());

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AddTripleAsync(null!));
    }

    [Fact]
    public async Task GetTriplesAsync_ReturnsAllTriplesForSubjectWhenGraphNotSpecified()
    {
        var subject = Guid.NewGuid();
        var expected = new List<Triple> { SampleTriple(subject), SampleTriple(subject, "tenant:acme.com/org:my-blog") };
        var executor = new FakeSqlExecutor { QueryHandler = (_, _) => (IReadOnlyList<Triple>)expected };
        var store = new PostgresTripleStore(executor);

        var results = await store.GetTriplesAsync(subject);

        Assert.Equal(2, results.Count);
        Assert.Single(executor.QueryCalls);
    }

    [Fact]
    public async Task GetTriplesAsync_PassesGraphFilterToExecutor()
    {
        const string graph = "tenant:acme.com/org:my-blog/user:42";
        object? capturedParameters = null;
        var executor = new FakeSqlExecutor
        {
            QueryHandler = (_, parameters) =>
            {
                capturedParameters = parameters;
                return (IReadOnlyList<Triple>)new List<Triple>();
            }
        };
        var store = new PostgresTripleStore(executor);

        await store.GetTriplesAsync(Guid.NewGuid(), graph);

        Assert.NotNull(capturedParameters);
        var graphProperty = capturedParameters!.GetType().GetProperty("Graph");
        Assert.Equal(graph, graphProperty!.GetValue(capturedParameters));
    }

    [Fact]
    public async Task RemoveTripleAsync_ReturnsTrueWhenRowAffected()
    {
        var executor = new FakeSqlExecutor { ExecuteResult = 1 };
        var store = new PostgresTripleStore(executor);

        var removed = await store.RemoveTripleAsync(Guid.NewGuid(), "favoriteColor", "tenant:acme.com/org:my-blog/user:42");

        Assert.True(removed);
        Assert.Contains("DELETE FROM entity_triples", executor.ExecuteCalls[0].Sql);
    }

    [Fact]
    public async Task RemoveTripleAsync_ReturnsFalseWhenNoRowAffected()
    {
        var executor = new FakeSqlExecutor { ExecuteResult = 0 };
        var store = new PostgresTripleStore(executor);

        var removed = await store.RemoveTripleAsync(Guid.NewGuid(), "favoriteColor", "tenant:acme.com/org:my-blog/user:42");

        Assert.False(removed);
    }
}
