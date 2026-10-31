using Adventures.Data.NQuad;
using Adventures.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Blog.Legacy.Tests;

/// <summary>Runs only when <c>LEGACY_SQL_CONNECTION</c> names a real BlogEngine database; otherwise it is reported as skipped. SELECT-only: it never writes.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public static string? ConnectionString { get; } = new ConfigurationBuilder()
        .AddUserSecrets<LiveFactAttribute>(optional: true)
        .Build()
        .GetConnectionString("BlogEngine");

    public LiveFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            Skip = "Set ConnectionStrings:BlogEngine in user-secrets to run the live read check.";
        }
    }
}

public class LiveLegacyReadTests
{
    [LiveFact]
    public async Task The_primary_blog_resolves_and_its_posts_pages_categories_and_tags_read_without_error()
    {
        var connection = LiveFactAttribute.ConnectionString!;
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        foreach (var seed in new[] { "seed.nq", "seed-006-content.nq", "seed-007-post-taxonomy.nq", "seed-008-post-revision.nq", "seed-013-post-approval.nq" })
        {
            await store.SeedFromFileAsync(Path.Combine(seeds, seed));
        }

        var quads = await store.QueryAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);
        services.AddBlogLegacy(connection);
        const string graph = BlogConstants.Content.DefaultGraph;
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.PostSchemaIri), BlogConstants.Content.PostBaseIri, BlogConstants.Content.PostTypeIri, graph, s => new Post(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.PageSchemaIri), BlogConstants.Content.PageBaseIri, BlogConstants.Content.PageTypeIri, graph, s => new Page(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.CategorySchemaIri), BlogConstants.Content.CategoryBaseIri, BlogConstants.Content.CategoryTypeIri, graph, s => new Category(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.TagSchemaIri), BlogConstants.Content.TagBaseIri, BlogConstants.Content.TagTypeIri, graph, s => new Tag(s));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var blogId = await scope.ServiceProvider.GetRequiredService<ILegacyBlog>().BlogIdAsync();
        var posts = await scope.ServiceProvider.GetRequiredService<IEntityDal<Post>>().ListAsync();
        var pages = await scope.ServiceProvider.GetRequiredService<IEntityDal<Page>>().ListAsync();
        var categories = await scope.ServiceProvider.GetRequiredService<IEntityDal<Category>>().ListAsync();
        var tags = await scope.ServiceProvider.GetRequiredService<IEntityDal<Tag>>().ListAsync();

        Assert.NotEqual(Guid.Empty, blogId);
        Assert.All(posts, p => Assert.False(string.IsNullOrEmpty(p.EntityId?.Value)));
        Assert.All(pages, p => Assert.False(string.IsNullOrEmpty(p.EntityId?.Value)));
        Assert.True(categories.Count >= 0 && tags.Count >= 0);
    }
}
