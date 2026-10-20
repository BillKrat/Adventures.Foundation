using System.Security.Claims;
using System.Text.Json;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.WebApi.Tests;

public sealed class UnhandledExceptionHandlerTests
{
    internal sealed class RecordingExceptionLog(string? idToReturn) : IExceptionLogBll
    {
        public List<(Exception Exception, string? Request, string? CorrelationId, TimeSpan Window)> Throttled { get; } = [];

        public Task<string?> RecordThrottledAsync(
            Exception exception, string? request, string? userName, string? correlationId, TimeSpan window, CancellationToken cancellationToken = default)
        {
            Throttled.Add((exception, request, correlationId, window));
            return Task.FromResult(idToReturn);
        }

        public List<(Exception Exception, string? Request, string? UserName)> Recorded { get; } = [];

        public string? LastCorrelationId { get; private set; }

        public Task<string?> RecordAsync(Exception exception, string? request, string? userName, string? correlationId = null, CancellationToken cancellationToken = default)
        {
            LastCorrelationId = correlationId;
            Recorded.Add((exception, request, userName));
            return Task.FromResult(idToReturn);
        }

        public Task<IReadOnlyList<ExceptionLogEntry>> ListIncludingPurgedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<int> CountIncludingPurgedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<int> PurgeAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<ExceptionLogEntry> CreateAsync(ExceptionLogEntry entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ExceptionLogEntry?> GetAsync(string entityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExceptionLogEntry>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateAsync(ExceptionLogEntry entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeHandlerFeature(Exception error) : IExceptionHandlerFeature
    {
        public Exception Error { get; } = error;

        public string Path { get; } = "/api/profile/me";

        public Endpoint? Endpoint => null;

        public Microsoft.AspNetCore.Routing.RouteValueDictionary? RouteValues => null;
    }

    private static (DefaultHttpContext Context, MemoryStream Body) NewContext(Exception? error, IExceptionLogBll? log, string? userName = "Admin")
    {
        var services = new ServiceCollection();
        if (log is not null)
        {
            services.AddSingleton(log);
        }

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Method = "PUT";
        context.Request.Path = "/api/profile/me";
        if (userName is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, userName)], "Bearer"));
        }

        if (error is not null)
        {
            context.Features.Set<IExceptionHandlerFeature>(new FakeHandlerFeature(error));
        }

        var body = new MemoryStream();
        context.Response.Body = body;
        return (context, body);
    }

    private static JsonElement ReadBody(MemoryStream body)
    {
        body.Position = 0;
        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task HandleAsync_RecordsTheExceptionWithRequestAndUser_AndReturnsItsReference()
    {
        var log = new RecordingExceptionLog("abc-123");
        var error = new InvalidOperationException("secret internal detail");
        var (context, body) = NewContext(error, log);

        await UnhandledExceptionHandler.HandleAsync(context);

        var recorded = Assert.Single(log.Recorded);
        Assert.Same(error, recorded.Exception);
        Assert.Equal("PUT /api/profile/me", recorded.Request);
        Assert.Equal("Admin", recorded.UserName);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("abc-123", ReadBody(body).GetProperty("errorId").GetString());
    }

    [Fact]
    public async Task HandleAsync_PassesTheCorrelationIdToTheLog_AndReturnsItToTheClient()
    {
        var log = new RecordingExceptionLog("abc-123");
        var (context, body) = NewContext(new InvalidOperationException("boom"), log);
        context.Items[Adventures.WebApi.CorrelationIdMiddleware.ItemsKey] = "corr-77";

        await UnhandledExceptionHandler.HandleAsync(context);

        Assert.Equal("corr-77", log.LastCorrelationId);
        Assert.Equal("corr-77", ReadBody(body).GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task HandleAsync_NeverEchoesTheMessageOrStackToTheClient()
    {
        var (context, body) = NewContext(new InvalidOperationException("secret internal detail"), new RecordingExceptionLog("x"));

        await UnhandledExceptionHandler.HandleAsync(context);

        body.Position = 0;
        var text = new StreamReader(body).ReadToEnd();
        Assert.DoesNotContain("secret internal detail", text);
        Assert.DoesNotContain("InvalidOperationException", text);
        Assert.Contains(UnhandledExceptionHandler.FriendlyTitle, text);
    }

    [Fact]
    public async Task HandleAsync_WhenTheLogCannotRecord_StillAnswers500WithoutAReference()
    {
        var (context, body) = NewContext(new InvalidOperationException("x"), new RecordingExceptionLog(null));

        await UnhandledExceptionHandler.HandleAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal(JsonValueKind.Null, ReadBody(body).GetProperty("errorId").ValueKind);
    }

    [Fact]
    public async Task HandleAsync_WithNoLogRegistered_StillAnswers500()
    {
        var (context, _) = NewContext(new InvalidOperationException("x"), log: null);

        await UnhandledExceptionHandler.HandleAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_WithTheRealBllAndStore_TheExceptionAppearsInTheAdminGridAndCanBePurged()
    {
        var store = new Adventures.Data.NQuad.InMemoryNQuadStore();
        var seedDirectory = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
        await Adventures.Data.NQuad.NQuadStoreExtensions.SeedOnceAsync(store, Path.Combine(seedDirectory, "seed.nq"), "baseline");
        await Adventures.Data.NQuad.NQuadStoreExtensions.SeedOnceAsync(
            store, Path.Combine(seedDirectory, "seed-002-exception-log.nq"), EntityConstants.ExceptionLog.SeedName);
        var schema = Adventures.Data.NQuad.SchemaDal.Load(await store.QueryAsync(), EntityConstants.ExceptionLog.SchemaIri);

        var services = new ServiceCollection();
        services.AddSingleton<Adventures.Data.NQuad.INQuadStore>(store);
        Adventures.Data.NQuad.NQuadEntityServiceCollectionExtensions.AddNQuadEntityRepository(
            services, schema, EntityConstants.ExceptionLog.BaseIri, EntityConstants.ExceptionLog.TypeIri,
            EntityConstants.ExceptionLog.DefaultGraph, s => new ExceptionLogEntry(s));
        services.AddScoped<IExceptionLogBll, ExceptionLogBll>();
        services.AddScoped<IExceptionLogPresenter, ExceptionLogPresenter>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = "GET";
        context.Request.Path = "/api/dev/throw";
        context.Response.Body = new MemoryStream();
        context.Features.Set<IExceptionHandlerFeature>(new FakeHandlerFeature(new InvalidOperationException("Deliberate test exception")));

        await UnhandledExceptionHandler.HandleAsync(context);

        var presenter = scope.ServiceProvider.GetRequiredService<IExceptionLogPresenter>();
        var grid = await presenter.GetGridAsync();
        var row = Assert.Single(grid.Rows);
        Assert.Equal("Deliberate test exception", row.Values["Message"]);
        Assert.Equal("GET /api/dev/throw", row.Values["Request"]);
        Assert.Equal(1, await presenter.CountActiveAsync());

        Assert.Equal(1, await presenter.PurgeAsync());
        Assert.Empty((await presenter.GetGridAsync()).Rows);
    }

    [Fact]
    public async Task HandleAsync_ACancelledRequestIsNotAnError()
    {
        var log = new RecordingExceptionLog("x");
        var (context, _) = NewContext(new OperationCanceledException(), log);

        await UnhandledExceptionHandler.HandleAsync(context);

        Assert.Empty(log.Recorded);
    }
}
