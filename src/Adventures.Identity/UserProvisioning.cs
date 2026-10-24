using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Adventures.Data;
using Adventures.Security;

namespace Adventures.Identity;

/// <summary>What <see cref="IUserProvisioning"/> did: the account that can now sign in and the one-time password to hand to the person (never stored in the clear, never logged).</summary>
public sealed record ProvisionedLogin(string Username, string Password, bool AccountCreated);

/// <summary>
/// Gives a person the ability to sign in: creates their account in the identity store when it has none (no roles: access comes from role
/// assignments, never from this call) and sets a generated first password that must be changed at the first sign-in. Used by the Users
/// screen (an administrator sets the first password) and by the test-user script. The host decides who may call it.
/// </summary>
public interface IUserProvisioning
{
    /// <summary>Creates the account when missing, then sets a new generated password (also clearing a lockout). The password is returned once.</summary>
    Task<ProvisionedLogin> SetInitialPasswordAsync(string tenant, string username, string? email, string? displayName, CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="IUserProvisioning"/> over the identity entity store, the credential store and the password hasher.</summary>
public sealed class UserProvisioning(IEntityRepository entities, IUserCredentialStore credentials, IPasswordHasher hasher) : IUserProvisioning
{
    public const string PasswordAlgorithm = "pbkdf2-sha256";
    public const int GeneratedLength = 20;

    // no look-alike characters (0/O, 1/l/I) so a password read from the screen is typed correctly
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    public async Task<ProvisionedLogin> SetInitialPasswordAsync(string tenant, string username, string? email, string? displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var created = false;
        var entity = await entities.FindByStandardFieldAsync(tenant, "user", "username", username, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            var fields = new JsonObject
            {
                ["username"] = username,
                ["email"] = email ?? string.Empty,
                ["display_name"] = string.IsNullOrWhiteSpace(displayName) ? username : displayName,
                ["status"] = "must_change_password",
                ["roles"] = new JsonArray(),
            };
            var now = DateTimeOffset.UtcNow;
            entity = await entities.CreateAsync(new Entity(Guid.NewGuid(), tenant, "default", "user", fields.ToJsonString(), now, now, 1), cancellationToken).ConfigureAwait(false);
            created = true;
        }

        var password = Generate();
        await credentials.CreateOrResetAsync(entity.Id, hasher.Hash(password), PasswordAlgorithm, mustChangePassword: true, cancellationToken).ConfigureAwait(false);
        return new ProvisionedLogin(username, password, created);
    }

    private static string Generate()
    {
        var chars = new char[GeneratedLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }
}
