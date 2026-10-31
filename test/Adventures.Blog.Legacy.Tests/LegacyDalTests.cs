using Adventures.Blog;
using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Adventures.Data.Sql;
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Blog.Legacy.Tests;

/// <summary>The legacy Dals against a scripted SQL Server, wired the way the host wires Legacy mode: <c>AddBlogLegacy</c> first, then the usual entity registrations.</summary>
public sealed class LegacyDalTests : IAsyncLifetime
{
    private static readonly Guid BlogId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PostId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CategoryId = new("33333333-3333-3333-3333-333333333333");

    private readonly FakeSqlServer _sql = new();
    private IServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        var store = new InMemoryNQuadStore();
        var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        foreach (var seed in new[] { "seed.nq", "seed-006-content.nq", "seed-007-post-taxonomy.nq", "seed-008-post-revision.nq", "seed-013-post-approval.nq" })
        {
            await store.SeedFromFileAsync(Path.Combine(seeds, seed));
        }

        var quads = await store.QueryAsync();
        var services = new ServiceCollection();
        services.AddSingleton<INQuadStore>(store);

        // the whole switch: SQL Dals first, the fake executor in place of the real one ...
        services.AddBlogLegacy("Server=unused;Database=unused", BlogId);
        services.AddSingleton<ISqlServerExecutor>(_sql);

        // ... then the registrations that do not change between modes (they add a Dal only where none is registered)
        const string graph = BlogConstants.Content.DefaultGraph;
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.PostSchemaIri), BlogConstants.Content.PostBaseIri, BlogConstants.Content.PostTypeIri, graph, s => new Post(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.PageSchemaIri), BlogConstants.Content.PageBaseIri, BlogConstants.Content.PageTypeIri, graph, s => new Page(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.CategorySchemaIri), BlogConstants.Content.CategoryBaseIri, BlogConstants.Content.CategoryTypeIri, graph, s => new Category(s));
        services.AddNQuadEntityRepository(SchemaDal.Load(quads, BlogConstants.Content.TagSchemaIri), BlogConstants.Content.TagBaseIri, BlogConstants.Content.TagTypeIri, graph, s => new Tag(s));
        services.AddNQuadRelations();
        services.AddBlog();
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void Registering_AddBlogLegacy_first_is_the_whole_switch_the_Dals_stay_SQL()
    {
        using var scope = _services.CreateScope();

        Assert.IsType<LegacyPostDal>(scope.ServiceProvider.GetRequiredService<IEntityDal<Post>>());
        Assert.IsType<LegacyPageDal>(scope.ServiceProvider.GetRequiredService<IEntityDal<Page>>());
        Assert.IsType<LegacyCategoryDal>(scope.ServiceProvider.GetRequiredService<IEntityDal<Category>>());
        Assert.IsType<LegacyTagDal>(scope.ServiceProvider.GetRequiredService<IEntityDal<Tag>>());
        Assert.IsType<SqlServerStoreProbe>(scope.ServiceProvider.GetRequiredService<Adventures.Common.Diagnostics.IStoreProbe>());
    }

    [Fact]
    public async Task A_post_is_read_from_be_Posts_with_its_categories_and_tags_and_the_deleted_are_left_out()
    {
        _sql.Answer("FROM be_Posts", new[] { new LegacyPostDal.PostRow { PostID = PostId, Title = "Hello", Slug = "hello", PostContent = "<p>Hi</p>", Author = "Admin", IsPublished = true, IsCommentEnabled = true, DateCreated = new DateTime(2020, 1, 2, 3, 4, 5) } });
        _sql.Answer("FROM be_PostCategory", new[] { new LegacyPostDal.CategoryLink { PostID = PostId, CategoryID = CategoryId } });
        _sql.Answer("FROM be_PostTag", new[] { new LegacyPostDal.TagLink { PostID = PostId, Tag = "AI" } });
        using var scope = _services.CreateScope();

        var posts = await scope.ServiceProvider.GetRequiredService<IPostBll>().ListAsync();

        var post = Assert.Single(posts);
        Assert.Equal(PostId.ToString(), post.EntityId!.Value);
        Assert.Equal("Hello", post.GetValue("Title"));
        Assert.Equal("<p>Hi</p>", post.GetValue("Content"));
        Assert.Equal(true, post.GetValue("IsPublished"));
        Assert.Equal(CategoryId.ToString(), Assert.Single(post.GetValues("Categories")));
        Assert.Equal(LegacyIds.ForTagName("AI").ToString(), Assert.Single(post.GetValues("Tags")));
        Assert.Contains(_sql.Statements, s => s.Sql.Contains("FROM be_Posts") && s.Sql.Contains("IsDeleted = 0") && FakeSqlServer.Param(s.Parameters, "BlogId") is Guid g && g == BlogId);
    }

    [Fact]
    public async Task Creating_a_post_inserts_the_row_then_rewrites_its_category_and_tag_links_with_bound_parameters()
    {
        _sql.Answer("FROM be_Posts", (object?)null);
        _sql.Answer("SELECT DISTINCT Tag", new[] { "AI" });
        using var scope = _services.CreateScope();
        var dal = scope.ServiceProvider.GetRequiredService<IEntityDal<Post>>();
        var schema = scope.ServiceProvider.GetRequiredService<EntitySchemaOf<Post>>();
        var post = new Post(schema.Schema);
        post.Set("Title", "g1", "A title");
        post.Set("Slug", "g2", "a-title");
        post.Set("Content", "g3", "body");
        post.Set("IsPublished", "g4", false);
        post.Add("Categories", "g5", CategoryId.ToString());
        post.Add("Tags", "g6", LegacyIds.ForTagName("AI").ToString());

        var created = await dal.CreateAsync(post);

        var insert = Assert.Single(_sql.Executed("INSERT INTO be_Posts"));
        Assert.Equal("A title", FakeSqlServer.Param(insert.Parameters, "Title"));
        Assert.Equal("a-title", FakeSqlServer.Param(insert.Parameters, "Slug"));
        Assert.Equal(BlogId, FakeSqlServer.Param(insert.Parameters, "BlogId"));
        Assert.Equal(created.EntityId!.Value, FakeSqlServer.Param(insert.Parameters, "Id")!.ToString());
        Assert.Equal(CategoryId, FakeSqlServer.Param(Assert.Single(_sql.Executed("INSERT INTO be_PostCategory")).Parameters, "CategoryId"));
        Assert.Equal("AI", FakeSqlServer.Param(Assert.Single(_sql.Executed("INSERT INTO be_PostTag")).Parameters, "Tag"));
        // no value is ever concatenated into the statement
        Assert.All(_sql.Statements, s => Assert.DoesNotContain("A title", s.Sql));
    }

    [Fact]
    public async Task A_revision_is_refused_because_the_BlogEngine_tables_have_no_place_for_it()
    {
        using var scope = _services.CreateScope();
        var dal = scope.ServiceProvider.GetRequiredService<IEntityDal<Post>>();
        var post = new Post(scope.ServiceProvider.GetRequiredService<EntitySchemaOf<Post>>().Schema);
        post.Set("Title", "g1", "Proposed change");
        post.Set("RevisionOf", "g2", PostId.ToString());

        await Assert.ThrowsAsync<BusinessRuleException>(() => dal.CreateAsync(post));
        Assert.Empty(_sql.Executed("INSERT INTO be_Posts"));
    }

    [Fact]
    public async Task Deleting_a_post_sets_the_tombstone_and_reports_false_when_nothing_matched()
    {
        using var scope = _services.CreateScope();
        var dal = scope.ServiceProvider.GetRequiredService<IEntityDal<Post>>();

        Assert.True(await dal.DeleteAsync(PostId.ToString()));
        Assert.Contains("IsDeleted = 1", Assert.Single(_sql.Executed("UPDATE be_Posts")).Sql);

        _sql.RowsAffected = 0;
        Assert.False(await dal.DeleteAsync(PostId.ToString()));
        Assert.False(await dal.DeleteAsync("not-a-guid"));
    }

    [Fact]
    public async Task Updating_a_post_that_does_not_exist_throws_and_leaves_the_links_alone()
    {
        _sql.RowsAffected = 0;
        using var scope = _services.CreateScope();
        var dal = scope.ServiceProvider.GetRequiredService<IEntityDal<Post>>();
        var post = new Post(scope.ServiceProvider.GetRequiredService<EntitySchemaOf<Post>>().Schema);
        post.Set("Id", "g0", PostId);
        post.Set("Title", "g1", "x");

        await Assert.ThrowsAsync<InvalidOperationException>(() => dal.UpdateAsync(post));
        Assert.Empty(_sql.Executed("DELETE FROM be_PostCategory"));
    }

    [Fact]
    public async Task Categories_and_tags_name_themselves_for_the_pointer_fields_from_SQL_not_from_the_N_Quad_store()
    {
        _sql.Answer("FROM be_Categories", new[] { new LegacyCategoryDal.CategoryRow { CategoryID = CategoryId, CategoryName = "General" } });
        _sql.Answer("SELECT DISTINCT Tag", new[] { "AI", "MCP" });
        using var scope = _services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IEntityRelationProvider>();

        var categories = await provider.GetOptionsAsync(BlogConstants.Content.CategorySchemaIri);
        var tags = await provider.GetOptionsAsync(BlogConstants.Content.TagSchemaIri);

        Assert.Equal([("General", CategoryId.ToString())], categories.Select(o => (o.Text, o.Id)));
        Assert.Equal(["AI", "MCP"], tags.Select(o => o.Text));
    }

    [Fact]
    public async Task A_page_is_read_with_its_front_page_flag_and_a_new_tag_exists_until_a_post_uses_it()
    {
        _sql.Answer("FROM be_Pages", new[] { new LegacyPageDal.PageRow { PageID = PostId, Title = "About", Slug = "about", PageContent = "x", IsPublished = true, IsFrontPage = true, ShowInList = true, Parent = Guid.Empty, SortOrder = 2 } });
        _sql.Answer("SELECT DISTINCT Tag", Array.Empty<string>());
        using var scope = _services.CreateScope();

        var page = Assert.Single(await scope.ServiceProvider.GetRequiredService<IEntityDal<Page>>().ListAsync());
        Assert.Equal(true, page.GetValue("IsFrontPage"));
        Assert.Equal(2, page.GetValue("SortOrder"));
        Assert.Null(page.GetValue("Parent"));

        var tagDal = scope.ServiceProvider.GetRequiredService<IEntityDal<Tag>>();
        var tag = new Tag(scope.ServiceProvider.GetRequiredService<EntitySchemaOf<Tag>>().Schema);
        tag.Set("Name", "g1", "New idea");
        await tagDal.CreateAsync(tag);
        Assert.Contains("New idea", (await tagDal.ListAsync()).Select(t => t.GetValue("Name")));
    }

    [Fact]
    public async Task The_blog_is_the_configured_one_or_else_the_primary_blog_found_once()
    {
        var sql = new FakeSqlServer();
        sql.Answer("FROM be_Blogs", new Guid?[] { BlogId });
        var blog = new LegacyBlog(sql);

        Assert.Equal(BlogId, await blog.BlogIdAsync());
        Assert.Equal(BlogId, await blog.BlogIdAsync());
        Assert.Single(sql.Statements);

        var configured = new LegacyBlog(sql, CategoryId);
        Assert.Equal(CategoryId, await configured.BlogIdAsync());
        Assert.Single(sql.Statements);
    }
}
