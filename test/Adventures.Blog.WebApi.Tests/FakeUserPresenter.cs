// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;

namespace Adventures.Blog.WebApi.Tests;

/// <summary>Shared test double for <see cref="IUserPresenter"/>, used by both ProfileControllerTests and UsersControllerTests.</summary>
internal sealed class FakeUserPresenter : ITenantUserPresenter
{
    public EntityFormModel? FormByUserName { get; set; }

    public EntityFormModel? FormById { get; set; }

    public IReadOnlyList<EntityDataModel> Summaries { get; set; } = [];

    public EntityFormModel? CreateResult { get; set; }

    public EntityFormModel? UpdateResult { get; set; }

    public bool ThrowOnDelete { get; set; }

    public bool DeleteResult { get; set; } = true;

    public EntityDataModel? LastCreateRequest { get; private set; }

    public EntityDataModel? LastUpdateRequest { get; private set; }

    public (string UserId, string CurrentUserId)? LastDeleteCall { get; private set; }

    public Task<EntityFormModel?> GetFormAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(FormById);

    public Task<EntityFormModel?> GetFormByUserNameAsync(string userName, CancellationToken cancellationToken = default) =>
        Task.FromResult(FormByUserName);

    public Task<EntityFormModel?> GetNewFormAsync(CancellationToken cancellationToken = default) => Task.FromResult<EntityFormModel?>(null);

    public Task<IReadOnlyList<EntityDataModel>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Summaries);

    public EntityGridQuery? LastQuery { get; private set; }

    public Task<EntityGridModel> GetGridAsync(EntityGridQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        return Task.FromResult(new EntityGridModel(
            new EntitySchemaModel("urn:test", []), Summaries, new GridPaging(query.Page, query.PageSize, Summaries.Count, query.Sort, query.Descending), CanCreate: true));
    }

    public Task<EntityGridModel> GetGridAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new EntityGridModel(new EntitySchemaModel("urn:test", []), Summaries));

    public Task<EntityFormModel> CreateAsync(EntityDataModel data, CancellationToken cancellationToken = default)
    {
        LastCreateRequest = data;
        return Task.FromResult(CreateResult ?? throw new InvalidOperationException("CreateResult was not set."));
    }

    public Task<EntityFormModel?> UpdateAsync(string id, EntityDataModel data, CancellationToken cancellationToken = default)
    {
        LastUpdateRequest = data;
        return Task.FromResult(UpdateResult);
    }

    /// <summary>The ungated base DeleteAsync - not exercised by these tests, which always go through the guarded overload below.</summary>
    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Tests exercise the guarded DeleteAsync(id, currentUserId) overload only.");

    public Task<bool> DeleteAsync(string id, string currentUserId, CancellationToken cancellationToken = default)
    {
        LastDeleteCall = (id, currentUserId);
        if (ThrowOnDelete)
        {
            throw new Adventures.Common.Exceptions.BusinessRuleException("You cannot delete your own account.");
        }

        return Task.FromResult(DeleteResult);
    }
}
