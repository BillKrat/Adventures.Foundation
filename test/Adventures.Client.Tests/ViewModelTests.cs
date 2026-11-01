using System.Net;
using Adventures.Entities;
using Adventures.Security;
using Xunit;

namespace Adventures.Client.Tests;

public class ViewModelTests
{
    private static readonly EntitySchemaModel Schema = new("urn:test",
    [
        new EntityFieldSchema("EntityId", "String", IsRequired: false, IsReadOnly: true, Order: 0),
        new EntityFieldSchema("UserName", "String", IsRequired: true, Label: "User name", Order: 1),
        new EntityFieldSchema("Email", "String", IsRequired: false, Order: 2),
        new EntityFieldSchema("Secret", "String", IsRequired: false, Order: 3, IsListVisible: false),
    ]);

    private static EntityDataModel Row(string id, string name, string email = "") =>
        new(id, new Dictionary<string, string?> { ["EntityId"] = id, ["UserName"] = name, ["Email"] = email });

    private sealed class FakePresenter : IEntityPresenter<DynamicRecord>
    {
        public List<EntityDataModel> Data { get; } = [Row("u1", "Ann", "ann@x"), Row("u2", "Bob")];
        public Exception? Throw { get; set; }
        public EntityGridQuery? LastQuery { get; private set; }
        public EntityDataModel? Saved { get; private set; }
        public EntityPermissions? Permissions { get; set; }
        public int PageSize { get; set; } = 25;

        private void Maybe()
        {
            if (Throw is { } error)
            {
                throw error;
            }
        }

        public Task<EntityGridModel> GetGridAsync(EntityGridQuery query, CancellationToken cancellationToken = default)
        {
            Maybe();
            LastQuery = query;
            var page = Data.Skip((query.Page - 1) * PageSize).Take(PageSize).ToList();
            return Task.FromResult(new EntityGridModel(Schema, page, new GridPaging(query.Page, PageSize, Data.Count, null, false), CanCreate: true));
        }

        public Task<EntityGridModel> GetGridAsync(CancellationToken cancellationToken = default) => GetGridAsync(new EntityGridQuery(), cancellationToken);

        public async Task<IReadOnlyList<EntityDataModel>> ListAsync(CancellationToken cancellationToken = default) => (await GetGridAsync(cancellationToken)).Rows;

        public Task<EntityFormModel?> GetFormAsync(string id, CancellationToken cancellationToken = default)
        {
            Maybe();
            var row = Data.FirstOrDefault(r => r.EntityId == id);
            return Task.FromResult(row is null ? null : new EntityFormModel(Schema, row, Permissions));
        }

        public Task<EntityFormModel?> GetNewFormAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<EntityFormModel?>(new EntityFormModel(Schema, new EntityDataModel(string.Empty, new Dictionary<string, string?>()), Permissions));

        public Task<EntityFormModel> CreateAsync(EntityDataModel data, CancellationToken cancellationToken = default)
        {
            Maybe();
            Saved = data;
            return Task.FromResult(new EntityFormModel(Schema, data with { EntityId = "new1" }));
        }

        public Task<EntityFormModel?> UpdateAsync(string id, EntityDataModel data, CancellationToken cancellationToken = default)
        {
            Maybe();
            Saved = data;
            return Task.FromResult<EntityFormModel?>(new EntityFormModel(Schema, data));
        }

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static async Task Idle(AsyncCommand command)
    {
        // commands run on a task; wait for the one just started
        while (command.IsRunning)
        {
            await Task.Delay(1);
        }
    }

    [Fact]
    public async Task List_shows_only_list_visible_columns_in_order_and_the_rows()
    {
        var list = new EntityListViewModel(new FakePresenter());

        await list.LoadAsync();

        Assert.Equal(["EntityId", "UserName", "Email"], list.Columns.Select(c => c.Name));
        Assert.Equal("User name", list.Columns[1].Label);
        Assert.Equal(["u1", "Ann", "ann@x"], list.Rows[0].Cells);
        Assert.Equal(2, list.Rows.Count);
        Assert.True(list.CanCreate);
    }

    [Fact]
    public async Task List_pages_forward_and_back_and_stops_at_the_ends()
    {
        var presenter = new FakePresenter { PageSize = 1 };
        var list = new EntityListViewModel(presenter);
        await list.LoadAsync();

        Assert.False(list.PreviousCommand.CanExecute(null));
        list.NextCommand.Execute(null);
        await Idle(list.NextCommand);
        Assert.Equal(2, presenter.LastQuery!.Page);
        Assert.Equal("Bob", list.Rows.Single().Cells[1]);
        Assert.False(list.NextCommand.CanExecute(null));
        Assert.True(list.PreviousCommand.CanExecute(null));
    }

    [Fact]
    public async Task List_reports_a_forbidden_call_as_a_message_and_a_lapsed_session_as_an_event()
    {
        var presenter = new FakePresenter { Throw = new ApiException(HttpStatusCode.Forbidden, "You may not list people.") };
        var list = new EntityListViewModel(presenter);
        var expired = 0;
        list.SessionExpired += (_, _) => expired++;

        await list.LoadAsync();
        Assert.Equal("You may not list people.", list.Error);
        Assert.Equal(0, expired);

        presenter.Throw = new ApiException(HttpStatusCode.Unauthorized, "x");
        await list.LoadAsync();
        Assert.Equal(1, expired);
    }

    [Fact]
    public async Task A_network_failure_is_a_plain_message_with_no_internals()
    {
        var list = new EntityListViewModel(new FakePresenter { Throw = new HttpRequestException("socket 10.0.0.5:443 refused") });

        await list.LoadAsync();

        Assert.DoesNotContain("10.0.0.5", list.Error);
        Assert.False(string.IsNullOrWhiteSpace(list.Error));
    }

    [Fact]
    public async Task Editing_loads_the_fields_and_saves_only_what_can_change()
    {
        var presenter = new FakePresenter();
        var edit = new EntityEditViewModel(presenter);
        await edit.LoadAsync("u1");

        Assert.Equal(["EntityId", "UserName", "Email", "Secret"], edit.Fields.Select(f => f.Name));
        Assert.True(edit.Fields[0].IsReadOnly);
        edit.Fields.Single(f => f.Name == "Email").Value = "new@x";
        var saved = false;
        edit.Saved += (_, _) => saved = true;

        await edit.SaveAsync();

        Assert.True(saved);
        Assert.Equal("new@x", presenter.Saved!.Values["Email"]);
        Assert.DoesNotContain("EntityId", presenter.Saved.Values.Keys);
    }

    [Fact]
    public async Task A_new_entry_is_created_and_a_missing_required_field_is_caught_before_the_call()
    {
        var presenter = new FakePresenter();
        var edit = new EntityEditViewModel(presenter);
        await edit.LoadAsync(null);

        await edit.SaveAsync();
        Assert.Null(presenter.Saved);
        Assert.Equal("User name is required.", edit.Fields.Single(f => f.Name == "UserName").Error);

        edit.Fields.Single(f => f.Name == "UserName").Value = "Cara";
        await edit.SaveAsync();
        Assert.Equal("Cara", presenter.Saved!.Values["UserName"]);
    }

    [Fact]
    public async Task The_servers_field_messages_land_on_the_fields()
    {
        var presenter = new FakePresenter();
        var edit = new EntityEditViewModel(presenter);
        await edit.LoadAsync("u1");
        presenter.Throw = new ApiException(HttpStatusCode.BadRequest, "Validation failed.", new Dictionary<string, string[]> { ["email"] = ["Email is not valid."] });

        await edit.SaveAsync();

        Assert.Equal("Email is not valid.", edit.Fields.Single(f => f.Name == "Email").Error);
        Assert.Equal("Validation failed.", edit.Error);
    }

    [Fact]
    public async Task A_read_only_form_cannot_be_saved()
    {
        var edit = new EntityEditViewModel(new FakePresenter { Permissions = EntityPermissions.ReadOnly });
        await edit.LoadAsync("u1");

        Assert.False(edit.SaveCommand.CanExecute(null));
        Assert.All(edit.Fields, f => Assert.True(f.IsReadOnly));
    }

    [Fact]
    public async Task SignIn_needs_both_fields_clears_the_password_and_announces_success()
    {
        var principal = new AmbientPrincipal();
        var handler = new StubHandler(_ => StubHandler.Json(new { accessToken = ApiSessionTestsToken(), expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), mustChangePassword = false }));
        var signIn = new SignInViewModel(new ApiSession(StubHandler.Client(handler), principal));
        var announced = false;
        signIn.SignedIn += (_, _) => announced = true;

        Assert.False(signIn.SignInCommand.CanExecute(null));
        signIn.UserName = "tester";
        signIn.Password = "pw";
        Assert.True(signIn.SignInCommand.CanExecute(null));
        await signIn.SignInAsync();

        Assert.True(announced);
        Assert.Equal(string.Empty, signIn.Password);
        Assert.Null(signIn.Error);
    }

    [Fact]
    public async Task SignIn_refused_says_so_without_naming_the_wrong_part()
    {
        var signIn = new SignInViewModel(new ApiSession(StubHandler.Client(new StubHandler(_ => StubHandler.Empty(HttpStatusCode.Unauthorized))), new AmbientPrincipal()))
        {
            UserName = "tester",
            Password = "bad",
        };

        await signIn.SignInAsync();

        Assert.Contains("refused", signIn.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password was", signIn.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, signIn.Password);
    }

    private static string ApiSessionTestsToken()
    {
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(new byte[32]);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken("i", "a", [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "tester")],
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
