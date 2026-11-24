using System.Reflection;
using Adventures.Blog;
using Adventures.Blog.WebApi;
using Adventures.Common.Exceptions;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace Adventures.Blog.WebApi.Tests;

public sealed class SearchControllerTests
{
    private sealed class FakeSearcher : IContentSearcher
    {
        public (string? Query, int Page, int Size)? Asked { get; private set; }

        public Task<SearchResult> SearchAsync(string? query, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            {
                throw new BusinessRuleException("Type at least two letters to search.");
            }

            Asked = (query, page, pageSize);
            return Task.FromResult(new SearchResult(query, 1, page, pageSize, [new SearchHit("post", "a", "A", "snippet", null)]));
        }
    }

    [Fact]
    public async Task ASearchAnswersTheResult_WithThePagingAsked()
    {
        var searcher = new FakeSearcher();

        var result = await new SearchController(searcher).Get("angular", 2, 5);

        var answer = Assert.IsType<SearchResult>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(("angular", 2, 5), searcher.Asked);
        Assert.Equal("a", Assert.Single(answer.Hits).Slug);
    }

    [Fact]
    public async Task ATooShortQuery_IsARefusalWithAReason_NotACrash()
    {
        var result = await new SearchController(new FakeSearcher()).Get("a");

        Assert.IsNotType<OkObjectResult>(result);
    }

    [Fact]
    public void TheRoute_IsAnonymous_ReadOnly_AndRateLimited()
    {
        var controller = typeof(SearchController);
        Assert.NotEmpty(controller.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Contains(controller.GetCustomAttributes<RouteAttribute>(), a => a.Template == "api/public/search");
        var get = controller.GetMethod("Get")!;
        Assert.Equal(RequestHardening.SearchPolicy, get.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.All(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => Assert.Empty(m.GetCustomAttributes<HttpPostAttribute>().Concat<Attribute>(m.GetCustomAttributes<HttpPutAttribute>()).Concat(m.GetCustomAttributes<HttpDeleteAttribute>())));
    }
}
