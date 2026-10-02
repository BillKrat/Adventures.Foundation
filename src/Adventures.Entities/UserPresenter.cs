namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IUserPresenter"/> - see that interface for the contract and rationale.</summary>
public sealed class UserPresenter(IEntityRepository<User> users, IUserBll userBll, EntitySchema userSchema)
    : EntityPresenter<User>(users, userSchema, schema => new User(schema)), IUserPresenter
{
    private readonly IUserBll _userBll = userBll ?? throw new ArgumentNullException(nameof(userBll));

    public async Task<EntityFormModel?> GetFormByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        var user = await _userBll.FindByUserNameAsync(userName, cancellationToken).ConfigureAwait(false);
        return user is null ? null : EntityFormModel.From(Schema, user);
    }

    public Task<bool> DeleteAsync(string id, string currentUserId, CancellationToken cancellationToken = default) =>
        _userBll.DeleteAsync(id, currentUserId, cancellationToken);
}
