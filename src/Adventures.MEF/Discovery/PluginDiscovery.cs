using System.Composition.Hosting;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

namespace Adventures.MEF;

/// <summary>
/// Where a host looks for plug-ins besides its own assemblies. A DLL is code that runs with the host's rights, so by default
/// (<see cref="RequireApproval"/>) the directory loads only files named in <see cref="ApprovedFileName"/> with a matching SHA-256: the file
/// someone reviewed is the file that loads. A plug-in's private dependencies must be on the list too.
/// </summary>
public sealed record PluginDirectoryOptions(string? Path, bool RequireApproval = true)
{
    /// <summary>The approved list inside the plug-in directory: a JSON object of file name to hex SHA-256.</summary>
    public const string ApprovedFileName = "approved-plugins.json";
}

/// <summary>A plug-in that loaded. <see cref="Source"/> says where it came from: <c>assembly:Name</c> or <c>directory:File.dll</c>.</summary>
public sealed record DiscoveredPlugin(PluginDefinition Definition, IPluginModule Module, string Source);

/// <summary>A plug-in or file that did not load, and why. Failures are reported to the administrator, never skipped silently.</summary>
public sealed record PluginLoadFailure(string Source, string Reason);

public sealed record PluginDiscoveryResult(IReadOnlyList<DiscoveredPlugin> Plugins, IReadOnlyList<PluginLoadFailure> Failures);

/// <summary>
/// Finds <see cref="IPluginModule"/> exports (System.Composition, the lightweight MEF) in the host's own assemblies and in a plug-in directory.
/// One bad assembly or file never stops the others: each is composed on its own and its failure is recorded.
/// </summary>
public static class PluginDiscovery
{
    public static PluginDiscoveryResult Discover(IEnumerable<Assembly> hostAssemblies, PluginDirectoryOptions? directory)
    {
        ArgumentNullException.ThrowIfNull(hostAssemblies);
        var plugins = new List<DiscoveredPlugin>();
        var failures = new List<PluginLoadFailure>();
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in hostAssemblies.DistinctBy(a => a.FullName))
        {
            Collect(assembly, "assembly:" + assembly.GetName().Name, plugins, failures, keys);
        }

        if (!string.IsNullOrWhiteSpace(directory?.Path))
        {
            ScanDirectory(directory!, plugins, failures, keys);
        }

        return new PluginDiscoveryResult(plugins, failures);
    }

    private static void ScanDirectory(PluginDirectoryOptions options, List<DiscoveredPlugin> plugins, List<PluginLoadFailure> failures, Dictionary<string, string> keys)
    {
        var path = options.Path!;
        if (!Directory.Exists(path))
        {
            failures.Add(new PluginLoadFailure("directory", $"The plug-in directory was not found: {path}"));
            return;
        }

        Dictionary<string, string>? approved = null;
        if (options.RequireApproval)
        {
            var listFile = Path.Combine(path, PluginDirectoryOptions.ApprovedFileName);
            if (!File.Exists(listFile))
            {
                failures.Add(new PluginLoadFailure("directory", $"There is no approved list ({PluginDirectoryOptions.ApprovedFileName}) in the plug-in directory, so nothing was loaded."));
                return;
            }

            try
            {
                approved = new Dictionary<string, string>(
                    JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(listFile)) ?? throw new JsonException("empty"),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                failures.Add(new PluginLoadFailure("directory", $"The approved list could not be read ({ex.Message}), so nothing was loaded."));
                return;
            }
        }

        foreach (var file in Directory.EnumerateFiles(path, "*.dll", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            var source = "directory:" + name;
            try
            {
                // Read the bytes once: the hash that is checked is the file that is loaded (no gap to swap it in).
                var bytes = File.ReadAllBytes(file);
                if (approved is not null)
                {
                    var problem = Verify(name, bytes, approved);
                    if (problem is not null)
                    {
                        failures.Add(new PluginLoadFailure(source, problem));
                        continue;
                    }
                }

                var context = new PluginLoadContext(file, approved);
                var assembly = context.LoadFromStream(new MemoryStream(bytes));
                Collect(assembly, source, plugins, failures, keys);
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failures.Add(new PluginLoadFailure(source, $"The file could not be loaded ({ex.GetType().Name}: {ex.Message})."));
            }
        }
    }

    /// <summary>Null when the file is on the approved list with the same SHA-256, otherwise the reason it is refused.</summary>
    internal static string? Verify(string fileName, byte[] bytes, IReadOnlyDictionary<string, string> approved)
    {
        if (!approved.TryGetValue(fileName, out var expected))
        {
            return $"{fileName} is not on the approved list, so it was not loaded.";
        }

        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{fileName} does not match the approved SHA-256 (it changed since it was reviewed), so it was not loaded.";
    }

    private static void Collect(Assembly assembly, string source, List<DiscoveredPlugin> plugins, List<PluginLoadFailure> failures, Dictionary<string, string> keys)
    {
        try
        {
            using var container = new ContainerConfiguration().WithAssembly(assembly).CreateContainer();
            foreach (var module in container.GetExports<IPluginModule>())
            {
                var definition = module.Definition;
                if (string.IsNullOrWhiteSpace(definition.Key))
                {
                    failures.Add(new PluginLoadFailure(source, "A plug-in has no key."));
                }
                else if (keys.TryGetValue(definition.Key, out var first))
                {
                    failures.Add(new PluginLoadFailure(source, $"The plug-in key '{definition.Key}' is already registered by {first}."));
                }
                else
                {
                    keys[definition.Key] = source;
                    plugins.Add(new DiscoveredPlugin(definition, module, source));
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failures.Add(new PluginLoadFailure(source, $"The plug-in could not be composed ({ex.GetType().Name}: {ex.Message})."));
        }
    }

    /// <summary>
    /// Loads one plug-in apart from the others. Anything the host already has (the contracts, System.Composition, the framework) comes from the
    /// host, so a plug-in cannot replace a host assembly; its private dependencies come from its own folder and, when approval is required, must
    /// be on the approved list with a matching hash.
    /// </summary>
    private sealed class PluginLoadContext(string mainPath, IReadOnlyDictionary<string, string>? approved) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
            {
                // not a host assembly: a private dependency of the plug-in
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path is null)
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            if (approved is not null && Verify(Path.GetFileName(path), bytes, approved) is { } problem)
            {
                throw new FileLoadException(problem);
            }

            return LoadFromStream(new MemoryStream(bytes));
        }
    }
}
