using Adventures.Diagnostics.WebApi;
// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Security;

using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Adventures.Diagnostics.WebApi.Tests;

public sealed class AdminExceptionsControllerTests
{
    private sealed class FakePresenter : Adventures.Entities.IExceptionLogPresenter
    {
        public (string Kind, bool Purged)? DrillDown { get; private set; }
        public bool PlainGridAsked { get; private set; }
        public bool HistoryAsked { get; private set; }

        private static readonly Adventures.Entities.EntityGridModel Empty = new(new Adventures.Entities.EntitySchemaModel("urn:test", []), []);

        /// <summary>What every grid call answers (empty unless a test wants rows).</summary>
        public Adventures.Entities.EntityGridModel Answer { get; init; } = Empty;

        public Task<Adventures.Entities.EntityGridModel> GetSignatureGridAsync(string signatureId, bool includePurged, CancellationToken cancellationToken = default)
        {
            DrillDown = (signatureId, includePurged);
            return Task.FromResult(Answer);
        }

        public Task<Adventures.Entities.EntityGridModel> GetGridAsync(CancellationToken cancellationToken = default)
        {
            PlainGridAsked = true;
            return Task.FromResult(Answer);
        }

        public Task<Adventures.Entities.EntityGridModel> GetHistoryGridAsync(CancellationToken cancellationToken = default)
        {
            HistoryAsked = true;
            return Task.FromResult(Answer);
        }

        public Task<Adventures.Entities.EntityGridModel> GetGridAsync(Adventures.Entities.EntityGridQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Adventures.Entities.EntityFormModel?> GetFormAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Adventures.Entities.EntityFormModel?> GetNewFormAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Adventures.Entities.EntityDataModel>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Adventures.Entities.EntityFormModel> CreateAsync(Adventures.Entities.EntityDataModel data, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Adventures.Entities.EntityFormModel?> UpdateAsync(string id, Adventures.Entities.EntityDataModel data, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> CountTotalAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> PurgeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task AKindOfFailureCanBeDrilledInto_WithOrWithoutThePurgedHistory()
    {
        var presenter = new FakePresenter();
        var controller = new AdminExceptionsController(presenter, null!);

        await controller.Grid(includePurged: true, signature: "kind-1");

        Assert.Equal(("kind-1", true), presenter.DrillDown);
        Assert.False(presenter.PlainGridAsked);
        Assert.False(presenter.HistoryAsked);
    }

    [Fact]
    public async Task WithNoKindTheGridIsAsBefore()
    {
        var presenter = new FakePresenter();
        var controller = new AdminExceptionsController(presenter, null!);

        await controller.Grid();
        await controller.Grid(includePurged: true);

        Assert.True(presenter.PlainGridAsked);
        Assert.True(presenter.HistoryAsked);
        Assert.Null(presenter.DrillDown);
    }

    private static Adventures.Entities.EntityGridModel Rows(int count) =>
        new(
            new Adventures.Entities.EntitySchemaModel("urn:test", []),
            Enumerable.Range(1, count).Select(n => new Adventures.Entities.EntityDataModel($"e{n}", new Dictionary<string, string?>())).ToArray());

    private static Adventures.Entities.EntityGridModel Served(Microsoft.AspNetCore.Mvc.ActionResult<Adventures.Entities.EntityGridModel> result) =>
        Assert.IsType<Adventures.Entities.EntityGridModel>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task TheGridIsPaged_TwentyFiveToAPageByDefault()
    {
        var controller = new AdminExceptionsController(new FakePresenter { Answer = Rows(159) }, null!);

        var grid = Served(await controller.Grid());

        Assert.Equal(25, grid.Rows.Count);
        Assert.Equal("e1", grid.Rows[0].EntityId);
        Assert.Equal(new Adventures.Entities.GridPaging(1, 25, 159, null, false), grid.Paging);
        Assert.Equal(7, grid.Paging!.TotalPages);
        Assert.Null(grid.Links!.Previous);
        Assert.NotNull(grid.Links.Next);
    }

    [Fact]
    public async Task ALaterPageHoldsTheNextRows_AndItsLinksKeepTheDrillDownAndHistory()
    {
        var controller = new AdminExceptionsController(new FakePresenter { Answer = Rows(159) }, null!);

        var grid = Served(await controller.Grid(includePurged: true, signature: "kind 1", page: 7));

        Assert.Equal(9, grid.Rows.Count);
        Assert.Equal("e151", grid.Rows[0].EntityId);
        Assert.Null(grid.Links!.Next);
        Assert.Equal("/api/admin/exceptions?page=6&pageSize=25&includePurged=true&signature=kind%201", grid.Links.Previous);
    }

    [Fact]
    public async Task APageBeyondTheEnd_ShowsTheLastPage_AndPageSizeIsCapped()
    {
        var controller = new AdminExceptionsController(new FakePresenter { Answer = Rows(30) }, null!);

        Assert.Equal(2, Served(await controller.Grid(page: 99)).Paging!.Page);
        Assert.Equal(Adventures.Entities.EntityGridQuery.MaxPageSize, Served(await controller.Grid(pageSize: 100000)).Paging!.PageSize);
        Assert.Equal(1, Served(await controller.Grid(page: 0)).Paging!.Page);
    }

    [Fact]
    public void TheCountOfAKind_IsHowManyOccurrencesAreInTheLog_PurgedOnesIncluded()
    {
        var kinds = new[]
        {
            new Adventures.Common.Diagnostics.ExceptionSignatureSummary("k1", "Npgsql.NpgsqlException", "ssl", "frame", null, null, 2, "New"),
            new Adventures.Common.Diagnostics.ExceptionSignatureSummary("k2", "Other", "x", "frame", null, null, 5, "New"),
        };
        var rows = new[] { "k1", "k1", "k1", "k1" }.Select((s, i) => new Adventures.Entities.EntityDataModel($"e{i}", new Dictionary<string, string?> { ["SignatureId"] = s })).ToArray();

        var counted = AdminExceptionsController.WithLoggedCounts(kinds, rows);

        Assert.Equal(4, counted.Single(k => k.Id == "k1").OccurrenceCount);
        Assert.Equal(5, counted.Single(k => k.Id == "k2").OccurrenceCount); // nothing logged for it: the stored count stands
    }

    [Fact]
    public void Controller_NeedsTheDiagnosticsRight()
    {
        var guard = Assert.Single(
            typeof(AdminExceptionsController).GetCustomAttributes(typeof(Adventures.WebApi.RequireRightAttribute), inherit: true).Cast<Adventures.WebApi.RequireRightAttribute>());

        Assert.Equal(Adventures.Entities.Rights.DiagnosticsRead, guard.Right);
    }

    [Fact]
    public void NoActionIsAnonymous()
    {
        var anonymous = typeof(AdminExceptionsController).GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Length > 0);

        Assert.Empty(anonymous);
    }
}
