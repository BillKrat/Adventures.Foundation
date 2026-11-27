using System.Text.Json;

namespace Adventures.Browser.Client;

/// <summary>What a person's settings file holds: the browser API they use (optional) and the domains they configured.</summary>
public sealed record BrowserSettings(string? ApiBaseUrl, IReadOnlyList<string> Domains)
{
    public static BrowserSettings Empty { get; } = new(null, []);
}

/// <summary>
/// The settings file of a desktop installation. A missing file is an empty configuration; a file that cannot be read is reported and never overwritten
/// (it may be a hand edit with a typo, and saving over it would lose the person's list).
/// </summary>
public sealed class BrowserSettingsFile(string path) : ISiteListStore
{
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web) { WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public string Path { get; } = path;

    public BrowserSettings Read()
    {
        if (!File.Exists(Path))
        {
            return BrowserSettings.Empty;
        }

        try
        {
            var read = JsonSerializer.Deserialize<BrowserSettings>(File.ReadAllText(Path), Format);
            return new BrowserSettings(read?.ApiBaseUrl, read?.Domains ?? []);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"The settings file could not be read: {Path}. Correct it, or move it away to start with an empty list.", error);
        }
    }

    public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Read().Domains.Select(DomainName.Normalize).OfType<string>().Distinct().ToList());

    public Task SaveAsync(IReadOnlyList<string> domains, CancellationToken cancellationToken = default)
    {
        var settings = Read() with { Domains = domains };
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Format));
        File.Move(temporary, Path, overwrite: true); // all or nothing
        return Task.CompletedTask;
    }
}
