using Adventures.Data;
using Xunit;

namespace Adventures.Data.Tests;

public class PostgresEntityRepositoryTests
{
    private static Entity SampleEntity(Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        Tenant: "acme.com",
        Org: "my-blog",
        EntityType: "post",
        StandardFieldsJson: """{"title":"Hello"}""",
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        RowVersion: 1);

    [Fact]
    public async Task CreateAsync_ExecutesInsertAndReturnsStampedEntity()
    {
        var executor = new FakeSqlExecutor();
        var repository = new PostgresEntityRepository(executor);
        var entity = SampleEntity();

        var created = await repository.CreateAsync(entity);

        Assert.Single(executor.ExecuteCalls);
        Assert.Contains("INSERT INTO entities", executor.ExecuteCalls[0].Sql);
        Assert.Equal(entity.Id, created.Id);
        Assert.Equal(1, created.RowVersion);
    }

    [Fact]
    public async Task CreateAsync_ThrowsForNullEntity()
    {
        var repository = new PostgresEntityRepository(new FakeSqlExecutor());

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.CreateAsync(null!));
    }

    [Fact]
    public async Task GetAsync_ReturnsEntityWhenFound()
    {
        var executor = new FakeSqlExecutor();
        var expected = SampleEntity();
        executor.QuerySingleOrDefaultHandler = (_, _) => expected;
        var repository = new PostgresEntityRepository(executor);

        var result = await repository.GetAsync(expected.Id);

        Assert.NotNull(result);
        Assert.Equal(expected.Id, result!.Id);
        Assert.Single(executor.QuerySingleCalls);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenNotFound()
    {
        var executor = new FakeSqlExecutor();
        var repository = new PostgresEntityRepository(executor);

        var result = await repository.GetAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsTrueWhenRowAffected()
    {
        var executor = new FakeSqlExecutor { ExecuteResult = 1 };
        var repository = new PostgresEntityRepository(executor);

        var updated = await repository.UpdateAsync(SampleEntity());

        Assert.True(updated);
        Assert.Contains("UPDATE entities", executor.ExecuteCalls[0].Sql);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalseWhenNoRowAffected()
    {
        var executor = new FakeSqlExecutor { ExecuteResult = 0 };
        var repository = new PostgresEntityRepository(executor);

        var updated = await repository.UpdateAsync(SampleEntity());

        Assert.False(updated);
    }

    [Fact]
    public async Task UpdateAsync_FiltersOnRowVersionForOptimisticConcurrency()
    {
        var executor = new FakeSqlExecutor { ExecuteResult = 1 };
        var repository = new PostgresEntityRepository(executor);
        var entity = SampleEntity() with { RowVersion = 3 };

        await repository.UpdateAsync(entity);

        Assert.Contains("row_version = @RowVersion", executor.ExecuteCalls[0].Sql);
        var sentEntity = Assert.IsType<Entity>(executor.ExecuteCalls[0].Parameters);
        Assert.Equal(3L, sentEntity.RowVersion);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalseOnStaleRowVersion()
    {
        // Simulates another writer having already advanced row_version: the WHERE clause matches
        // zero rows even though the entity still exists, which must surface as a failed update.
        var executor = new FakeSqlExecutor { ExecuteResult = 0 };
        var repository = new PostgresEntityRepository(executor);

        var updated = await repository.UpdateAsync(SampleEntity() with { RowVersion = 1 });

        Assert.False(updated);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrueWhenRowAffected()
    {
        var executor = new FakeSqlExecutor { ExecuteResult = 1 };
        var repository = new PostgresEntityRepository(executor);

        var deleted = await repository.DeleteAsync(Guid.NewGuid());

        Assert.True(deleted);
        Assert.Contains("DELETE FROM entities", executor.ExecuteCalls[0].Sql);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseWhenNoRowAffected()
    {
        var executor = new FakeSqlExecutor { ExecuteResult = 0 };
        var repository = new PostgresEntityRepository(executor);

        var deleted = await repository.DeleteAsync(Guid.NewGuid());

        Assert.False(deleted);
    }

    [Fact]
    public async Task ListAsync_ReturnsMatchingEntitiesFromExecutor()
    {
        var expected = new List<Entity> { SampleEntity(), SampleEntity() };
        var executor = new FakeSqlExecutor { QueryHandler = (_, _) => (IReadOnlyList<Entity>)expected };
        var repository = new PostgresEntityRepository(executor);

        var results = await repository.ListAsync(new EntityQuery(Tenant: "acme.com"));

        Assert.Equal(2, results.Count);
        Assert.Single(executor.QueryCalls);
    }

    [Fact]
    public async Task ListAsync_ThrowsForNullQuery()
    {
        var repository = new PostgresEntityRepository(new FakeSqlExecutor());

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.ListAsync(null!));
    }
}
