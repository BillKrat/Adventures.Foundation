using Adventures.Identity;

namespace Adventures.Hosting;

/// <summary>
/// Stands in for <see cref="IUserProvisioning"/> in Data:Mode=Legacy, where there is no identity store (sign-in is the developer-testing account).
/// Lets the controllers that take the service be built, so Admin > Users lists people; creating a sign-in is refused with a clear message.
/// </summary>
public sealed class LegacyUserProvisioning : IUserProvisioning
{
    public const string Message = "Legacy mode has no sign-in accounts to create: it signs in with the developer-testing account only.";

    public Task<ProvisionedLogin> SetInitialPasswordAsync(string tenant, string username, string? email, string? displayName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(Message);
}
