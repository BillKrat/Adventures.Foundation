using Adventures.Entities;

namespace Adventures.Identity.WebApi;

/// <summary><see cref="IResetDirectory"/> over the tenant's people (<see cref="IUserBll"/>, already scoped to the request tenant): by sign-in name, or by saved address when the text has an at sign.</summary>
public sealed class UserBllResetDirectory(IUserBll users) : IResetDirectory
{
    public async Task<ResetPerson?> FindAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (!identifier.Contains('@'))
        {
            return Of(await users.FindByUserNameAsync(identifier, cancellationToken).ConfigureAwait(false));
        }

        var matches = (await users.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(user => string.Equals(Text(user, "Email"), identifier, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();

        // an address two people share proves nothing about either: ask for the sign-in name instead
        return matches.Count == 1 ? Of(matches[0]) : null;
    }

    public async Task<ResetPerson?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
        Of(await users.GetAsync(userId, cancellationToken).ConfigureAwait(false));

    private static ResetPerson? Of(User? user)
    {
        var id = user?.EntityId?.Value;
        var name = user is null ? null : Text(user, "UserName");
        return string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) ? null : new ResetPerson(id, name, Text(user!, "Email"));
    }

    private static string Text(User user, string field) => user.GetValue(field)?.ToString()?.Trim() ?? string.Empty;
}
