using Microsoft.AspNetCore.Mvc;

namespace Adventures.Blog.WebApi;

public static class BlogWebApiExtensions
{
    /// <summary>
    /// Makes MVC find the blog controllers in this library (a controller in a referenced library is not discovered by default).
    /// Call it on the builder returned by <c>AddControllers()</c>, together with <c>AddBlog()</c> for the services behind them.
    /// </summary>
    public static IMvcBuilder AddBlogControllers(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddApplicationPart(typeof(BlogWebApiExtensions).Assembly);
    }
}
