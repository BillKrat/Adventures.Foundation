namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IUserPresenter"/> - see that interface for the contract and rationale.</summary>
public sealed class UserPresenter(IUserBll userBll, EntitySchemaOf<User> userSchema, IEntityRelationProvider? relations = null)
    : EntityPresenter<User>(userBll, userSchema.Schema, schema => new User(schema), relations), IUserPresenter
{
    private readonly IUserBll _userBll = userBll ?? throw new ArgumentNullException(nameof(userBll));

    /// <summary>The login name is the identity the token and the profile lookup rely on, so it is shown but never changed through the form.</summary>
    protected override IReadOnlySet<string> ReadOnlyFields { get; } = new HashSet<string>(["UserName"], StringComparer.OrdinalIgnoreCase);

    public async Task<EntityFormModel?> GetFormByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        var user = await _userBll.FindByUserNameAsync(userName, cancellationToken).ConfigureAwait(false);
        // The caller is looking at their own profile: they may edit it, but the delete-own-account guard means they may never delete it.
        return user is null ? null : await WithRelationsAsync(EntityFormModel.From(Schema, user, new EntityPermissions(CanUpdate: true, CanDelete: false), ReadOnlyFields), editable: false, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> DeleteAsync(string id, string currentUserId, CancellationToken cancellationToken = default) =>
        _userBll.DeleteAsync(id, currentUserId, cancellationToken);
}
