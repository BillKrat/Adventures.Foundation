using System.Reflection;
using Adventures.Browser.WebApi;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Browser.WebApi.Tests;

public class SchemaBrowserControllerTests
{
    private const string Thing = "https://schema.example/test/Thing";

    private sealed class Source : ISchemaCatalogSource
    {
        public Task<SchemaCatalog> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SchemaCatalog([EntitySchema.Create(Thing, [new EntitySchemaField(Thing, Thing + "/Name", "Name", "string", "urn:p:name")])]));
    }

    private sealed class Tenant : IRequestTenant
    {
        public CurrentTenant Current { get; } = new("id-1", "One", "one.example", "one.example", TenantMatch.Domain);
    }

    private static T Value<T>(ActionResult<T> result) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task It_lists_the_schemas_and_shows_one()
    {
        var controller = new SchemaBrowserController();

        Assert.Equal("Thing", Assert.Single(Value(await controller.Schemas(new Source(), default)).Schemas).Name);
        Assert.Equal("Name", Assert.Single(Value(await controller.Schema(Thing, new Source(), default)).Fields).Name);
        Assert.IsType<NotFoundResult>((await controller.Schema("urn:none", new Source(), default)).Result);
    }

    [Fact]
    public void It_says_which_site_answered() =>
        Assert.Equal(new SchemaBrowserContext("One", "one.example"), Value(new SchemaBrowserController().Context(new Tenant())));

    // The beta browser is read only: a route that could change anything would have to change this test on purpose.
    [Fact]
    public void Every_route_only_reads_and_needs_the_schema_right()
    {
        var type = typeof(SchemaBrowserController);
        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal(Rights.SchemaRead, type.GetCustomAttribute<RequireRightAttribute>()!.Right);
        Assert.NotNull(type.GetCustomAttribute<SchemaBrowserSwitchedOnAttribute>());
        Assert.NotEmpty(actions);
        Assert.All(actions, a =>
        {
            Assert.Equal(["GET"], a.GetCustomAttributes<HttpMethodAttribute>().SelectMany(m => m.HttpMethods));
            Assert.Null(a.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>());
            Assert.Null(a.GetCustomAttribute<RequireRightAttribute>()); // nothing weakens the controller's right
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_routes_answer_not_found_unless_the_host_switched_them_on(bool switchedOn)
    {
        var services = new ServiceCollection();
        if (switchedOn)
        {
            services.AddSchemaBrowser();
        }

        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);

        new SchemaBrowserSwitchedOnAttribute().OnAuthorization(context);

        Assert.Equal(switchedOn, context.Result is null);
    }
}
