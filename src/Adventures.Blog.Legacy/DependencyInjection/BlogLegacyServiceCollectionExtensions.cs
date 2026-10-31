using Adventures.Data.Sql;
using Adventures.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Blog.Legacy;

public static class BlogLegacyServiceCollectionExtensions
{
    /// <summary>
    /// Serves the blog entities (posts, pages, categories, tags) from a BlogEngine.NET SQL Server database. This is the whole Legacy switch:
    /// call it before the entity repositories are registered (their registration only adds a Dal when none is registered yet), then call
    /// <c>AddBlog()</c> as usual; no Bll, presenter or controller changes. Collaboration has no legacy tables and stays off.
    /// </summary>
    public static IServiceCollection AddBlogLegacy(this IServiceCollection services, string connectionString, Guid? blogId = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSqlServer(connectionString);
        services.AddSingleton<ILegacyBlog>(p => new LegacyBlog(p.GetRequiredService<ISqlServerExecutor>(), blogId));
        services.AddSingleton<LegacyTagRegistry>();
        services.AddScoped<LegacyTagDal>();
        services.AddScoped<IEntityDal<Tag>>(p => p.GetRequiredService<LegacyTagDal>());
        services.AddScoped<LegacyCategoryDal>();
        services.AddScoped<IEntityDal<Category>>(p => p.GetRequiredService<LegacyCategoryDal>());
        services.AddScoped<IEntityDal<Page>, LegacyPageDal>();
        services.AddScoped<IEntityDal<Post>, LegacyPostDal>();
        // so the pointer fields of a post (its categories and tags) show their names and offer their choices from SQL, not from the N-Quad store
        services.AddScoped<IRelationNameSource>(p => p.GetRequiredService<LegacyCategoryDal>());
        services.AddScoped<IRelationNameSource>(p => p.GetRequiredService<LegacyTagDal>());
        return services;
    }
}
