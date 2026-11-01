using System.Net;
using Adventures.Entities;
using Xunit;

namespace Adventures.Client.Tests;

public class HttpEntityPresenterTests
{
    private static readonly EntitySchemaModel Schema = new("urn:test", [new EntityFieldSchema("UserName", "String", IsRequired: true)]);

    private static EntityFormModel Form(string id, string name) => new(Schema, new EntityDataModel(id, new Dictionary<string, string?> { ["UserName"] = name }));

    private static (HttpEntityPresenter<DynamicEntity> Presenter, StubHandler Handler) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new HttpEntityPresenter<DynamicEntity>(StubHandler.Client(handler), "/api/users/"), handler);
    }

    [Fact]
    public async Task Grid_asks_for_the_page_sort_and_filter_and_reads_the_rows()
    {
        var (presenter, handler) = Build(_ => StubHandler.Json(new EntityGridModel(Schema, [Form("u1", "Ann").Entity], new GridPaging(2, 10, 11, "UserName", true))));

        var grid = await presenter.GetGridAsync(new EntityGridQuery(Page: 2, PageSize: 10, Sort: "UserName", Descending: true, Filter: "Role", FilterValue: "Admin & Co"));

        Assert.Equal("/api/users?page=2&pageSize=10&sort=UserName&descending=true&filter=Role&filterValue=Admin%20%26%20Co", handler.Requests.Single().Url);
        Assert.Equal("u1", Assert.Single(grid.Rows).EntityId);
        Assert.Equal(2, grid.Paging!.TotalPages);
    }

    [Fact]
    public async Task Form_is_null_when_the_server_has_no_such_entry()
    {
        var (presenter, handler) = Build(_ => StubHandler.Empty(HttpStatusCode.NotFound));

        Assert.Null(await presenter.GetFormAsync("a/b"));
        Assert.Equal("/api/users/a%2Fb", handler.Requests.Single().Url);
    }

    [Fact]
    public async Task A_forbidden_call_is_an_ApiException_that_says_so()
    {
        var (presenter, _) = Build(_ => StubHandler.Empty(HttpStatusCode.Forbidden));

        var error = await Assert.ThrowsAsync<ApiException>(() => presenter.GetGridAsync());

        Assert.True(error.IsForbidden);
    }

    [Fact]
    public async Task Create_posts_the_entry_and_returns_the_saved_form()
    {
        var (presenter, handler) = Build(_ => StubHandler.Json(Form("u9", "New"), HttpStatusCode.Created));

        var saved = await presenter.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = "New" }));

        Assert.Equal((HttpMethod.Post, "/api/users"), (handler.Requests.Single().Method, handler.Requests.Single().Url));
        Assert.Contains("\"UserName\":\"New\"", handler.Requests.Single().Body);
        Assert.Equal("u9", saved.Entity.EntityId);
    }

    [Fact]
    public async Task A_refused_save_carries_the_servers_field_messages()
    {
        var (presenter, _) = Build(_ => StubHandler.Json(new { title = "Validation failed.", errors = new Dictionary<string, string[]> { ["UserName"] = ["UserName is required."] } }, HttpStatusCode.BadRequest));

        var error = await Assert.ThrowsAsync<ApiException>(() => presenter.UpdateAsync("u1", new EntityDataModel("u1", new Dictionary<string, string?>())));

        Assert.Equal(["UserName is required."], error.Errors["UserName"]);
    }

    [Fact]
    public async Task Update_is_null_for_a_missing_entry_and_delete_is_false()
    {
        var (presenter, handler) = Build(_ => StubHandler.Empty(HttpStatusCode.NotFound));

        Assert.Null(await presenter.UpdateAsync("gone", new EntityDataModel("gone", new Dictionary<string, string?>())));
        Assert.False(await presenter.DeleteAsync("gone"));
        Assert.Equal([HttpMethod.Put, HttpMethod.Delete], handler.Requests.Select(r => r.Method));
    }

    [Fact]
    public async Task Delete_is_true_when_the_server_accepts_it()
    {
        var (presenter, _) = Build(_ => StubHandler.Empty(HttpStatusCode.NoContent));

        Assert.True(await presenter.DeleteAsync("u1"));
    }

    [Fact]
    public async Task A_server_fault_never_leaks_its_body_to_the_person()
    {
        var (presenter, _) = Build(_ => StubHandler.Text("System.Exception at Secret.Connection(Password=hunter2)", HttpStatusCode.InternalServerError));

        var error = await Assert.ThrowsAsync<ApiException>(() => presenter.GetGridAsync());

        Assert.DoesNotContain("hunter2", error.Message);
        Assert.Contains("500", error.Message);
    }
}
