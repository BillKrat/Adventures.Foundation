// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// <see cref="UserPresenter"/>'s two additions beyond the generic base: the UserName lookup and
/// the guarded delete. Ordinary CRUDL passthrough is already covered by
/// <see cref="EntityPresenterTests"/> and <c>NQuadEntityRepositoryTests</c>.
/// </summary>
public sealed class UserPresenterTests : IAsyncLifetime
{
    private const string Graph = "https://global-webnet.com/graph/test";

    private IUserPresenter _presenter = null!;
    private InMemoryNQuadStore _store = null!;

    public async Task InitializeAsync()
    {
        var store = _store = new InMemoryNQuadStore();
        var seedPath = Path.Combine(AppContext.BaseDirectory, "Sql", "seed", "seed.nq");
        await store.SeedFromFileAsync(seedPath);
        var allQuads = await store.QueryAsync();
        var schema = SchemaDal.Load(allQuads, EntityConstants.Schema.UserIri);

        var repository = new NQuadEntityRepository<User>(
            store, schema, EntityConstants.User.BaseIri, EntityConstants.User.TypeIri, Graph, s => new User(s));
        var userBll = new UserBll(repository);

        _presenter = new UserPresenter(userBll, new EntitySchemaOf<User>(schema));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetFormByUserNameAsync_ExistingUser_ReturnsForm()
    {
        var form = await _presenter.GetFormByUserNameAsync("Admin");

        Assert.NotNull(form);
        Assert.Equal("Admin", form!.Entity.Values["UserName"]);
    }

    [Fact]
    public async Task OwnProfile_CanBeUpdatedButNotDeleted_AndTheLoginNameIsReadOnly()
    {
        var form = await _presenter.GetFormByUserNameAsync("Admin");

        Assert.Equal(new EntityPermissions(CanUpdate: true, CanDelete: false), form!.Permissions);
        Assert.Equal(EntityContract.Version, form.ContractVersion);
        Assert.True(form.Schema.Fields.Single(f => f.Name == "UserName").IsReadOnly);
        Assert.True(form.Schema.Fields.Single(f => f.Name == "Id").IsReadOnly);
        Assert.False(form.Schema.Fields.Single(f => f.Name == "First").IsReadOnly);
        Assert.Equal("Display name", form.Schema.Fields.Single(f => f.Name == "DisplayName").Label);
    }

    [Fact]
    public async Task UpdateAsync_RefusesAChangeToAReadOnlyField_ButAcceptsItSentBackUnchanged()
    {
        var form = await _presenter.GetFormByUserNameAsync("Admin");
        var id = form!.Entity.EntityId;
        var values = form.Entity.Values.ToDictionary(p => p.Key, p => p.Value);

        // the whole form posted back as the browser does: nothing changed, so it is accepted
        Assert.NotNull(await _presenter.UpdateAsync(id, new EntityDataModel(id, values)));

        values["UserName"] = "SomeoneElse";
        var refused = await Assert.ThrowsAsync<Adventures.Common.Exceptions.InputValidationException>(
            () => _presenter.UpdateAsync(id, new EntityDataModel(id, values)));
        Assert.Equal("User name cannot be changed.", refused.Errors["UserName"].Single());
        Assert.Equal("Admin", (await _presenter.GetFormByUserNameAsync("Admin"))!.Entity.Values["UserName"]);
    }

    [Fact]
    public async Task UpdateAsync_StillChangesAnEditableField()
    {
        var form = await _presenter.GetFormByUserNameAsync("Admin");
        var id = form!.Entity.EntityId;

        var updated = await _presenter.UpdateAsync(id, new EntityDataModel(id, new Dictionary<string, string?> { ["First"] = "Changed" }));

        Assert.Equal("Changed", updated!.Entity.Values["First"]);
    }

    [Fact]
    public async Task GetFormByUserNameAsync_NoMatch_ReturnsNull()
    {
        Assert.Null(await _presenter.GetFormByUserNameAsync("NoSuchUser"));
    }

    [Fact]
    public async Task DeleteAsync_OwnAccount_Throws()
    {
        var form = await _presenter.GetFormByUserNameAsync("Admin");
        var id = form!.Entity.EntityId;

        await Assert.ThrowsAsync<Adventures.Common.Exceptions.BusinessRuleException>(() => _presenter.DeleteAsync(id, id));
    }

    [Fact]
    public async Task TheBlankFormLetsAnAdministratorTypeTheUserName_ButAnExistingPersonKeepsIt()
    {
        var blank = await _presenter.GetNewFormAsync();
        var existing = await _presenter.GetFormByUserNameAsync("Admin");

        Assert.False(blank!.Schema.Fields.Single(f => f.Name == "UserName").IsReadOnly);
        Assert.True(existing!.Schema.Fields.Single(f => f.Name == "UserName").IsReadOnly);
    }

    [Fact]
    public async Task ANewUserNameMustBeWellFormedAndUnique_IgnoringCase()
    {
        var created = await _presenter.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = "  test-editor " }));
        Assert.Equal("test-editor", created.Entity.Values["UserName"]);

        var taken = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _presenter.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = "TEST-EDITOR" })));
        Assert.Contains("already taken", taken.Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _presenter.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = "has space" })));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _presenter.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = "ab" })));
    }
    [Fact]
    public async Task DeleteAsync_OtherAccount_Succeeds()
    {
        var admin = await _presenter.GetFormByUserNameAsync("Admin");
        var adminId = admin!.Entity.EntityId;

        var created = await _presenter.CreateAsync(new EntityDataModel(
            string.Empty, new Dictionary<string, string?> { ["UserName"] = "Temp" }));

        var deleted = await _presenter.DeleteAsync(created.Entity.EntityId, adminId);

        Assert.True(deleted);
    }

    /// <summary>
    /// Reproduces a real failure found manually: Admin has no DOB (never set in seed.nq), so an
    /// Angular form that round-trips every field posts back "" (not null) for the untouched DOB
    /// input - the browser has no way to distinguish "the field was never set" from "the user
    /// cleared it", and a form's bound value for an empty date input is "". Previously this threw
    /// a FormatException trying to DateOnly.ParseExact an empty string; it should instead be
    /// skipped, same as a null value already is, since "" is simply the DateOnly field's own
    /// formless Angular way of sending "unchanged/absent."
    /// </summary>
    [Fact]
    public async Task UpdateAsync_EmptyStringForDateField_SkipsRatherThanThrowing()
    {
        var admin = await _presenter.GetFormByUserNameAsync("Admin");
        var id = admin!.Entity.EntityId;

        var updated = await _presenter.UpdateAsync(id, new EntityDataModel(id, new Dictionary<string, string?>
        {
            ["Phone"] = "555-0100",
            ["Email"] = "admin@global-webnet.com",
            ["DisplayName"] = "Admin User",
            ["DOB"] = string.Empty,
        }));

        Assert.NotNull(updated);
        Assert.Equal("555-0100", updated!.Entity.Values["Phone"]);
        Assert.Null(updated.Entity.Values["DOB"]);
    }

    private async Task<EntityFormModel> CreateTempUserAsync(params (string Name, string Value)[] extra)
    {
        var values = new Dictionary<string, string?> { ["UserName"] = "Temp-" + Guid.NewGuid().ToString("N")[..8] };
        foreach (var (name, value) in extra)
        {
            values[name] = value;
        }

        return await _presenter.CreateAsync(new EntityDataModel(string.Empty, values));
    }

    [Fact]
    public async Task UpdateAsync_EmptyValueForAFieldThatHasOne_ClearsIt()
    {
        var created = await CreateTempUserAsync(("Phone", "555-0100"));
        var id = created.Entity.EntityId;

        var updated = await _presenter.UpdateAsync(id, new EntityDataModel(id, new Dictionary<string, string?> { ["Phone"] = string.Empty }));

        Assert.Null(updated!.Entity.Values["Phone"]);
        Assert.Null((await _presenter.GetFormAsync(id))!.Entity.Values["Phone"]);
    }

    [Fact]
    public async Task UpdateAsync_UsDateIsAccepted_ShownAsIso_AndSavingThatFormBackWritesNothing()
    {
        var created = await CreateTempUserAsync();
        var id = created.Entity.EntityId;

        var updated = await _presenter.UpdateAsync(id, new EntityDataModel(id, new Dictionary<string, string?> { ["DOB"] = "11/19/1961" }));

        Assert.Equal("1961-11-19", updated!.Entity.Values["DOB"]);
        var countBefore = await _store.CountAsync();
        await _presenter.UpdateAsync(id, updated.Entity);
        Assert.Equal(countBefore, await _store.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_NullValue_LeavesTheFieldAlone()
    {
        var created = await CreateTempUserAsync(("Phone", "555-0100"));
        var id = created.Entity.EntityId;

        await _presenter.UpdateAsync(id, new EntityDataModel(id, new Dictionary<string, string?> { ["Phone"] = null }));

        Assert.Equal("555-0100", (await _presenter.GetFormAsync(id))!.Entity.Values["Phone"]);
    }

    [Fact]
    public async Task UpdateAsync_EmptyValueForAFieldThatHasNone_IsANoOpNotAParseError()
    {
        var created = await CreateTempUserAsync();
        var id = created.Entity.EntityId;

        var updated = await _presenter.UpdateAsync(id, new EntityDataModel(id, new Dictionary<string, string?> { ["DOB"] = string.Empty }));

        Assert.Null(updated!.Entity.Values["DOB"]);
    }

    [Fact]
    public async Task UpdateAsync_SavingTheFormUnchanged_WritesNothing()
    {
        var created = await CreateTempUserAsync(("Phone", "555-0100"));
        var id = created.Entity.EntityId;
        var form = (await _presenter.GetFormAsync(id))!;
        var countBefore = await _store.CountAsync();

        await _presenter.UpdateAsync(id, form.Entity);

        Assert.Equal(countBefore, await _store.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_ChangingOneField_OnlyThatFieldIsRewritten()
    {
        var created = await CreateTempUserAsync(("Phone", "555-0100"), ("First", "Ada"));
        var id = created.Entity.EntityId;
        var form = (await _presenter.GetFormAsync(id))!;
        var values = form.Entity.Values.ToDictionary(v => v.Key, v => v.Value);
        values["Phone"] = "555-0199";
        var countBefore = await _store.CountAsync();

        await _presenter.UpdateAsync(id, new EntityDataModel(id, values));

        // one superseded marker + one new value quad - far fewer than a full rewrite
        var added = await _store.CountAsync() - countBefore;
        Assert.InRange(added, 2, 12);
        Assert.Equal("555-0199", (await _presenter.GetFormAsync(id))!.Entity.Values["Phone"]);
        Assert.Equal("Ada", (await _presenter.GetFormAsync(id))!.Entity.Values["First"]);
    }

    [Fact]
    public async Task CreateAsync_WithMalformedDate_ThrowsInputValidationExceptionNotFormatException()
    {
        var data = new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = "Temp", ["DOB"] = "not-a-date" });

        var ex = await Assert.ThrowsAsync<Adventures.Common.Exceptions.InputValidationException>(() => _presenter.CreateAsync(data));

        Assert.Contains("DOB", ex.Errors.Keys);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownField_ThrowsInputValidationExceptionAndStoresNothing()
    {
        var data = new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = "Temp2", ["IsAdmin"] = "true" });

        await Assert.ThrowsAsync<Adventures.Common.Exceptions.InputValidationException>(() => _presenter.CreateAsync(data));

        Assert.Null(await _presenter.GetFormByUserNameAsync("Temp2"));
    }

    [Fact]
    public async Task UpdateAsync_WithMalformedDate_ThrowsInputValidationException()
    {
        var admin = await _presenter.GetFormByUserNameAsync("Admin");
        var data = new EntityDataModel(admin!.Entity.EntityId, new Dictionary<string, string?> { ["DOB"] = "31/31/2020" });

        await Assert.ThrowsAsync<Adventures.Common.Exceptions.InputValidationException>(
            () => _presenter.UpdateAsync(admin.Entity.EntityId, data));
    }

    private async Task AddUsersAsync(params string[] names)
    {
        foreach (var name in names)
        {
            await _presenter.CreateAsync(new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = name }));
        }
    }

    [Fact]
    public async Task GetGridAsync_SortsPagesAndReportsWhereThePageSits()
    {
        await AddUsersAsync("Echo", "Delta", "Charlie", "Bravo", "Alpha");

        var first = await _presenter.GetGridAsync(new EntityGridQuery(Page: 1, PageSize: 3, Sort: "UserName"));
        var last = await _presenter.GetGridAsync(new EntityGridQuery(Page: 3, PageSize: 3, Sort: "username"));

        Assert.Equal(["Admin", "Alpha", "Bravo"], first.Rows.Select(r => r.Values["UserName"]));
        Assert.Equal(new GridPaging(1, 3, 7, "UserName", false), first.Paging);
        Assert.Equal(3, first.Paging!.TotalPages);
        Assert.Equal(["Echo"], last.Rows.Select(r => r.Values["UserName"]));
        Assert.True(first.CanCreate); // an administrator adds a person, then sets a first password for them
        Assert.Equal(EntityPermissions.Full, first.RowPermissions);
    }

    [Fact]
    public async Task GetGridAsync_DescendingNormalisesTheQueryAndIgnoresAnUnknownSortField()
    {
        await AddUsersAsync("Alpha", "Bravo");

        var descending = await _presenter.GetGridAsync(new EntityGridQuery(Page: 1, PageSize: 2, Sort: "UserName", Descending: true));
        var clamped = await _presenter.GetGridAsync(new EntityGridQuery(Page: 99, PageSize: 100_000, Sort: "NoSuchField"));

        Assert.Equal(["Claude", "Bravo"], descending.Rows.Select(r => r.Values["UserName"]));
        Assert.Equal(EntityGridQuery.MaxPageSize, clamped.Paging!.PageSize);
        Assert.Equal(1, clamped.Paging.Page);
        Assert.Null(clamped.Paging.Sort);
    }

    [Fact]
    public async Task GetGridAsync_HidesTheIdColumnByDefault_ButKeepsItInTheSchemaAndRows()
    {
        var grid = await _presenter.GetGridAsync(new EntityGridQuery());

        Assert.False(grid.Schema.Fields.Single(f => f.Name == "Id").IsListVisible);
        Assert.True(grid.Schema.Fields.Single(f => f.Name == "UserName").IsListVisible);
        Assert.All(grid.Rows, r => Assert.False(string.IsNullOrEmpty(r.Values["Id"])));
    }
}
