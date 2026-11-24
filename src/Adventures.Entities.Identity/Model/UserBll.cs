// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IUserBll"/> - see that interface for the contract and rationale.</summary>
public sealed class UserBll(IEntityDal<User> users, IUserMembership? membership = null, IHomeTenant? home = null) : EntityBll<User>(users), IUserBll
{
    /// <summary>The longest "About me" text.</summary>
    public const int MaxAboutLength = 1000;

    /// <summary>The longest website or picture address.</summary>
    public const int MaxAddressLength = 300;

    /// <summary>Required fields (the base rule), then the profile text rules: website and picture addresses are plain http(s) (a picture may also be a path on this site), lengths are limited.</summary>
    protected override void Validate(User entity)
    {
        base.Validate(entity);
        var errors = new Dictionary<string, string[]>();
        var about = entity.GetValue("AboutMe")?.ToString();
        if (about is { Length: > MaxAboutLength })
        {
            errors["AboutMe"] = [$"About me can be at most {MaxAboutLength} characters."];
        }

        var website = entity.GetValue("Website")?.ToString()?.Trim();
        if (!string.IsNullOrEmpty(website) && !IsWebAddress(website))
        {
            errors["Website"] = ["The website must be an address starting with http:// or https://, with no spaces."];
        }

        var picture = entity.GetValue("Picture")?.ToString()?.Trim();
        if (!string.IsNullOrEmpty(picture) && !IsWebAddress(picture) && !IsSitePath(picture))
        {
            errors["Picture"] = ["The picture must be an http or https address, or a path on this site such as /media/me.png."];
        }

        if (errors.Count > 0)
        {
            throw new InputValidationException(errors);
        }
    }

    /// <summary>One http or https address: no spaces, quotes or angle brackets, no sign-in part, at most <see cref="MaxAddressLength"/> characters.</summary>
    private static bool IsWebAddress(string text) =>
        text.Length <= MaxAddressLength
        && !text.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '<' or '>' or '\\' or '`')
        && Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.UserInfo.Length == 0;

    /// <summary>A path on this site: one leading slash (never two, which would mean another host), no spaces, quotes or angle brackets.</summary>
    private static bool IsSitePath(string text) =>
        text.Length <= MaxAddressLength
        && text.StartsWith('/') && !text.StartsWith("//", StringComparison.Ordinal)
        && !text.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '<' or '>' or '\\' or '`');

    /// <summary>
    /// Finds the person by user name (ignoring case). Scans the whole list: fine for the in-memory store, to be replaced by a Dal query before real data volumes. One person of the name:
    /// that person. Several (one per tenant): the one who belongs to the tenant the request came on; nobody when that tenant has none or there is no tenant, because guessing would hand one
    /// tenant's rights and profile to another's person.
    /// </summary>
    public async Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        var matches = (await ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(user => string.Equals(user.GetValue("UserName")?.ToString(), userName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count <= 1)
        {
            return matches.FirstOrDefault();
        }

        if (membership is null || home?.HomeTenantId is not { Length: > 0 } tenantId)
        {
            return null;
        }

        var members = await membership.MemberIdsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return matches.FirstOrDefault(user => user.EntityId is { } id && members.Contains(id.Value));
    }

    /// <summary>The user name is the sign-in name and the lookup key: letters, digits, dot, dash and underscore (3 to 64). Created this way the name must be free system-wide (the old rule).</summary>
    public override Task<User> CreateAsync(User entity, CancellationToken cancellationToken = default) => CreateCoreAsync(entity, null, cancellationToken);

    public Task<User> CreateInTenantAsync(User entity, IReadOnlySet<string> tenantPeopleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantPeopleIds);
        return CreateCoreAsync(entity, tenantPeopleIds, cancellationToken);
    }

    private async Task<User> CreateCoreAsync(User entity, IReadOnlySet<string>? among, CancellationToken cancellationToken)
    {
        var name = entity.GetValue("UserName")?.ToString()?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new BusinessRuleException("A user name is required.");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9._-]{3,64}$"))
        {
            throw new BusinessRuleException("The user name may use letters, digits, dot, dash and underscore only, 3 to 64 characters, no spaces.");
        }

        var taken = (await ListAsync(cancellationToken).ConfigureAwait(false)).Any(user =>
            string.Equals(user.GetValue("UserName")?.ToString(), name, StringComparison.OrdinalIgnoreCase)
            && (among is null || (user.EntityId is { } id && among.Contains(id.Value))));
        if (taken)
        {
            throw new BusinessRuleException(among is null ? "That user name is already taken." : "That user name is already taken in this tenant.");
        }

        entity.Set("UserName", Guid.NewGuid().ToString(), name);
        return await base.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> DeleteAsync(string userId, string currentUserId, CancellationToken cancellationToken = default)
    {
        if (string.Equals(userId, currentUserId, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("You cannot delete your own account.");
        }

        return Dal.DeleteAsync(userId, cancellationToken);
    }

    public override Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default) =>
        throw new BusinessRuleException("Deleting users requires the id of the user performing the deletion.");

    /// <summary>A user is only ever deleted by someone else; without an acting user the rule cannot be checked.</summary>
    public override Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) =>
        throw new BusinessRuleException("Deleting a user requires the id of the user performing the deletion.");
}
