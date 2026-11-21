using System.Globalization;
using System.Text.Json;
using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.MEF;

namespace Adventures.Blog;

/// <summary>One widget placement as the row is stored: where it is shown, which widget, an optional title, its settings, and its position in the zone. Schema in Sql/seed/seed-025-widget-placements.nq.</summary>
public sealed class WidgetPlacement(EntitySchema schema) : DynamicEntity(schema);

/// <summary>Where a placement lives in the store.</summary>
public static class WidgetConstants
{
    public const string SeedName = "025-widget-placements";
    public const string DefaultGraph = "https://global-webnet.com/graph/widgets";
    public const string SchemaIri = "https://schema.global-webnet.com/widgets/WidgetPlacement";
    public const string BaseIri = "https://global-webnet.com/id/widget-placement/";
    public const string TypeIri = "https://global-webnet.com/ontology/widgets#WidgetPlacement";

    /// <summary>The longest title an owner can give a placement.</summary>
    public const int MaxTitleLength = 100;
}

/// <summary>A placement for the Widgets screen. <see cref="Available"/> is false while the widget's plug-in is off or gone (the placement is kept).</summary>
public sealed record WidgetPlacementModel(string Id, string Zone, string WidgetKey, string? Title, IReadOnlyDictionary<string, string> Settings, int Position, bool Available);

/// <summary>A widget ready for a page: its placement id, widget key, the title to show, and its sanitised HTML.</summary>
public sealed record RenderedWidget(string Id, string WidgetKey, string Title, string Html);

/// <summary>Where each widget sits (per tenant) and what a zone shows.</summary>
public interface IWidgetPlacements
{
    Task<IReadOnlyList<WidgetPlacementModel>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Places an available widget at the end of a zone. A business-rule error for an unknown zone or widget, a widget of a plug-in that is off, or a setting the widget did not declare or that does not fit.</summary>
    Task<WidgetPlacementModel> AddAsync(string zone, string widgetKey, string? title, IReadOnlyDictionary<string, string>? settings, CancellationToken cancellationToken = default);

    /// <summary>Changes a placement's title and, when given, replaces its settings. Null when there is no such placement.</summary>
    Task<WidgetPlacementModel?> UpdateAsync(string id, string? title, IReadOnlyDictionary<string, string>? settings, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Swaps a placement with its neighbour in the zone: -1 up, +1 down. False when there is no such placement; nothing happens at the edge.</summary>
    Task<bool> MoveAsync(string id, int direction, CancellationToken cancellationToken = default);

    /// <summary>The widgets of a zone in order, rendered: placements whose widget is unavailable or that fail are left out.</summary>
    Task<IReadOnlyList<RenderedWidget>> RenderZoneAsync(string zone, CancellationToken cancellationToken = default);
}

public sealed class WidgetPlacements(IEntityBll<WidgetPlacement> store, EntitySchemaOf<WidgetPlacement> schema, IWidgetRenderer renderer) : IWidgetPlacements
{
    public async Task<IReadOnlyList<WidgetPlacementModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        var available = (await renderer.AvailableAsync(cancellationToken).ConfigureAwait(false)).Select(w => w.Definition.Key).ToHashSet(StringComparer.Ordinal);
        return (await StoredAsync(cancellationToken).ConfigureAwait(false))
            .Select(p => Model(p, available))
            .OrderBy(m => WidgetZones.All.ToList().IndexOf(m.Zone))
            .ThenBy(m => m.Position)
            .ToList();
    }

    public async Task<WidgetPlacementModel> AddAsync(string zone, string widgetKey, string? title, IReadOnlyDictionary<string, string>? settings, CancellationToken cancellationToken = default)
    {
        var normalised = WidgetZones.Normalise(zone) ?? throw new BusinessRuleException($"The zone must be one of: {string.Join(", ", WidgetZones.All)}.");
        var widgets = await renderer.AvailableAsync(cancellationToken).ConfigureAwait(false);
        var widget = widgets.FirstOrDefault(w => string.Equals(w.Definition.Key, widgetKey, StringComparison.Ordinal))
            ?? throw new BusinessRuleException($"There is no widget '{widgetKey}' that can be placed.");
        var clean = Check(widget.Definition, settings);
        var cleanTitle = CheckTitle(title);

        var inZone = (await StoredAsync(cancellationToken).ConfigureAwait(false)).Where(p => string.Equals(p.GetValue("Zone") as string, normalised, StringComparison.Ordinal)).ToList();
        var row = new WidgetPlacement(schema.Schema);
        var id = Guid.NewGuid();
        row.Set("Id", Guid.NewGuid().ToString(), id);
        row.Set("Zone", Guid.NewGuid().ToString(), normalised);
        row.Set("WidgetKey", Guid.NewGuid().ToString(), widgetKey);
        if (cleanTitle is not null)
        {
            row.Set("Title", Guid.NewGuid().ToString(), cleanTitle);
        }

        row.Set("Settings", Guid.NewGuid().ToString(), JsonSerializer.Serialize(clean));
        row.Set("Position", Guid.NewGuid().ToString(), (inZone.Count == 0 ? 0 : inZone.Max(PositionOf)) + 1);
        await store.CreateAsync(row, cancellationToken).ConfigureAwait(false);
        return Model(row, widgets.Select(w => w.Definition.Key).ToHashSet(StringComparer.Ordinal));
    }

    public async Task<WidgetPlacementModel?> UpdateAsync(string id, string? title, IReadOnlyDictionary<string, string>? settings, CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var widgets = await renderer.AvailableAsync(cancellationToken).ConfigureAwait(false);
        if (settings is not null)
        {
            var key = row.GetValue("WidgetKey") as string;
            var widget = widgets.FirstOrDefault(w => string.Equals(w.Definition.Key, key, StringComparison.Ordinal))
                ?? throw new BusinessRuleException("This widget is not available, so its settings cannot be changed now.");
            row.Set("Settings", Guid.NewGuid().ToString(), JsonSerializer.Serialize(Check(widget.Definition, settings)));
        }

        if (title is not null)
        {
            row.Set("Title", Guid.NewGuid().ToString(), CheckTitle(title) ?? string.Empty);
        }

        await store.UpdateAsync(row, cancellationToken).ConfigureAwait(false);
        return Model(row, widgets.Select(w => w.Definition.Key).ToHashSet(StringComparer.Ordinal));
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        await store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> MoveAsync(string id, int direction, CancellationToken cancellationToken = default)
    {
        var all = await StoredAsync(cancellationToken).ConfigureAwait(false);
        var row = all.FirstOrDefault(p => p.EntityId?.Value == id);
        if (row is null)
        {
            return false;
        }

        var zone = row.GetValue("Zone") as string;
        var inZone = all.Where(p => string.Equals(p.GetValue("Zone") as string, zone, StringComparison.Ordinal)).OrderBy(PositionOf).ToList();
        var at = inZone.IndexOf(row);
        var to = at + Math.Sign(direction);
        if (direction == 0 || to < 0 || to >= inZone.Count)
        {
            return true;
        }

        var neighbour = inZone[to];
        var (mine, theirs) = (PositionOf(row), PositionOf(neighbour));
        row.Set("Position", Guid.NewGuid().ToString(), theirs);
        neighbour.Set("Position", Guid.NewGuid().ToString(), mine);
        await store.UpdateAsync(row, cancellationToken).ConfigureAwait(false);
        await store.UpdateAsync(neighbour, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<RenderedWidget>> RenderZoneAsync(string zone, CancellationToken cancellationToken = default)
    {
        var normalised = WidgetZones.Normalise(zone);
        if (normalised is null)
        {
            return [];
        }

        var widgets = (await renderer.AvailableAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(w => w.Definition.Key, StringComparer.Ordinal);
        var rendered = new List<RenderedWidget>();
        foreach (var row in (await StoredAsync(cancellationToken).ConfigureAwait(false))
                     .Where(p => string.Equals(p.GetValue("Zone") as string, normalised, StringComparison.Ordinal))
                     .OrderBy(PositionOf))
        {
            var key = row.GetValue("WidgetKey") as string ?? string.Empty;
            if (!widgets.TryGetValue(key, out var widget))
            {
                continue;
            }

            var html = await renderer.RenderAsync(key, normalised, SettingsOf(row), cancellationToken).ConfigureAwait(false);
            if (html is null)
            {
                continue;
            }

            var title = row.GetValue("Title") as string;
            rendered.Add(new RenderedWidget(row.EntityId!.Value, key, string.IsNullOrWhiteSpace(title) ? widget.Definition.Title : title, html));
        }

        return rendered;
    }

    private async Task<List<WidgetPlacement>> StoredAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Where(p => p.EntityId is not null).ToList();

    private async Task<WidgetPlacement?> FindAsync(string id, CancellationToken cancellationToken) =>
        (await StoredAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(p => p.EntityId?.Value == id);

    private static int PositionOf(WidgetPlacement row) => row.GetValue("Position") is { } value ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : 0;

    private static Dictionary<string, string> SettingsOf(WidgetPlacement row)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(row.GetValue("Settings") as string ?? "{}") ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static WidgetPlacementModel Model(WidgetPlacement row, IReadOnlySet<string> available) => new(
        row.EntityId!.Value,
        row.GetValue("Zone") as string ?? string.Empty,
        row.GetValue("WidgetKey") as string ?? string.Empty,
        row.GetValue("Title") as string is { Length: > 0 } title ? title : null,
        SettingsOf(row),
        PositionOf(row),
        available.Contains(row.GetValue("WidgetKey") as string ?? string.Empty));

    /// <summary>The settings in stored form: only the ones the widget declared, each checked by its kind, length and rule.</summary>
    private static Dictionary<string, string> Check(WidgetDefinition definition, IReadOnlyDictionary<string, string>? given)
    {
        var declared = (definition.Settings ?? []).ToDictionary(s => s.Name, StringComparer.Ordinal);
        var clean = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in given ?? new Dictionary<string, string>())
        {
            if (!declared.TryGetValue(name, out var setting))
            {
                throw new BusinessRuleException($"The widget '{definition.Key}' has no setting '{name}'.");
            }

            clean[name] = PluginSettings.CheckValue(setting, value);
        }

        return clean;
    }

    private static string? CheckTitle(string? title)
    {
        var clean = title?.Trim();
        if (clean is { Length: > WidgetConstants.MaxTitleLength })
        {
            throw new BusinessRuleException($"The title can be at most {WidgetConstants.MaxTitleLength} characters.");
        }

        return string.IsNullOrEmpty(clean) ? null : clean;
    }
}
