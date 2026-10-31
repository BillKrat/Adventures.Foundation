namespace Adventures.Entities;

/// <summary>
/// Moves a default role from its first shipped rights to an improved one, without a seed: a seed cannot supersede a stored value, and a
/// second seed quad would sit beside the first and lose. It only changes a role whose rights are still exactly the old default, so a role an
/// administrator edited is never overwritten, and running it again changes nothing. The host runs it at startup.
/// </summary>
public static class RoleRightsUpgrade
{
    /// <summary>The Reviewer as seed 018 shipped it: it could approve and request changes but not write a note with the decision.</summary>
    private const string ReviewerBefore = "admin.access,posts.read,pages.read,collaboration.read,collaboration.review,decisions.read";

    private const string ReviewerAfter = "admin.access,posts.read,pages.read,collaboration.read,collaboration.write,collaboration.review,decisions.read";

    /// <summary>Applies every default-rights upgrade; returns how many roles changed.</summary>
    public static async Task<int> ApplyAsync(IEntityDal<Role> roles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var changed = 0;
        foreach (var role in await roles.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (role.GetValue("Name") is "Reviewer" && string.Equals(role.GetValue("Rights") as string, ReviewerBefore, StringComparison.Ordinal))
            {
                role.Set("Rights", Guid.NewGuid().ToString(), ReviewerAfter);
                await roles.UpdateAsync(role, cancellationToken).ConfigureAwait(false);
                changed++;
            }
        }

        return changed;
    }
}
