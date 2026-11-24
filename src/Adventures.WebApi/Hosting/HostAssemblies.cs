using System.Reflection;
using System.Runtime.Loader;

namespace Adventures.WebApi;

/// <summary>
/// Makes the assembly scan of <c>AddLifetimeServices()</c> independent of how a host's startup code is arranged (moved from the API host, R1 of the host reuse review). That scan only sees assemblies
/// that are already loaded, and .NET loads one the first time code that uses it is compiled; loading every referenced library first removes the accident. The name prefixes are parameters, so any host
/// (this application, a private one) names its own.
/// </summary>
public static class HostAssemblies
{
    /// <summary>The host's own libraries (default load context only, a plug-in DLL lives in its own) whose name starts with one of <paramref name="prefixes"/>. Call after <see cref="EnsureReferencedLoaded"/>.</summary>
    public static IReadOnlyList<Assembly> Libraries(params string[] prefixes) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && AssemblyLoadContext.GetLoadContext(a) == AssemblyLoadContext.Default
                && a.GetName().Name is { } name && prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

    /// <summary>Loads each assembly <paramref name="host"/> references (the .NET platform's own are skipped) and returns how many it loaded or found loaded.</summary>
    public static int EnsureReferencedLoaded(Assembly host)
    {
        var loaded = 0;
        foreach (var name in host.GetReferencedAssemblies())
        {
            if (name.Name is null || name.Name.StartsWith("System", StringComparison.Ordinal) || name.Name.StartsWith("Microsoft", StringComparison.Ordinal)
                || name.Name is "netstandard" or "mscorlib" or "WindowsBase")
            {
                continue;
            }

            Assembly.Load(name);
            loaded++;
        }

        return loaded;
    }
}
