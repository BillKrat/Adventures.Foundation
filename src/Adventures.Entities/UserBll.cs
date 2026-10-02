namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IUserBll"/> - see that interface for the contract and rationale.</summary>
public sealed class UserBll(IEntityRepository<User> users) : IUserBll
{
    private readonly IEntityRepository<User> _users = users ?? throw new ArgumentNullException(nameof(users));

    /// <summary>
    /// UserName is meant to be a system-wide unique lookup key, but nothing enforces that yet (no
    /// validation at this stage - see <c>docs/artifacts</c>) - this returns the first match, same
    /// as the old Postgres-backed system's own documented-but-unenforced convention.
    /// </summary>
    public async Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        var all = await _users.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(user =>
            string.Equals(user.GetValue("UserName")?.ToString(), userName, StringComparison.OrdinalIgnoreCase));
    }

    public Task<User> CreateAsync(User user, CancellationToken cancellationToken = default) =>
        _users.CreateAsync(user, cancellationToken);

    public Task<User?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
        _users.GetAsync(userId, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default) =>
        _users.ListAsync(cancellationToken: cancellationToken);

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default) =>
        _users.UpdateAsync(user, cancellationToken);

    public Task<bool> DeleteAsync(string userId, string currentUserId, CancellationToken cancellationToken = default)
    {
        if (string.Equals(userId, currentUserId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("You cannot delete your own account.");
        }

        return _users.DeleteAsync(userId, cancellationToken);
    }
}
