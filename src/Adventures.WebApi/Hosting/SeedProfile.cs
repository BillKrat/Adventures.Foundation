using Microsoft.Extensions.Configuration;
namespace Adventures.WebApi;

/// <summary>
/// Chooses the baseline seed a NEW store gets. `seed.nq` carries our development tenants, people and assignments next to the schemas and roles; `seed-install.nq` is the same
/// file without that data (a customer's install starts empty: the first host becomes the first tenant, see the onboarding decision). A store that already has the baseline is not
/// touched either way (a seed applies once, by name). `Seed:Profile` is `dev` or `install`; unset, Development uses `dev` (today's local experience) and anything deployed uses `install`.
/// </summary>
public static class SeedProfile
{
    public const string SettingName = "Seed:Profile";

    public static string BaselineFile(IConfiguration configuration, bool development)
    {
        var setting = configuration[SettingName]?.Trim();
        if (string.Equals(setting, "dev", StringComparison.OrdinalIgnoreCase))
        {
            return "seed.nq";
        }

        if (string.Equals(setting, "install", StringComparison.OrdinalIgnoreCase))
        {
            return "seed-install.nq";
        }

        // unset or not understood: only a Development run gets our data; a deployed site never does by accident
        return development ? "seed.nq" : "seed-install.nq";
    }
}
