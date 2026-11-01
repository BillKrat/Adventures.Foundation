using System.Collections.ObjectModel;
using Adventures.Entities;

namespace Adventures.Client;

public sealed class FieldViewModel(string name, string label, string value, bool isReadOnly, bool isRequired, IReadOnlyList<string>? choices) : ObservableObject
{
    private string _value = value;
    private string? _error;

    public string Name { get; } = name;

    public string Label { get; } = label;

    public bool IsReadOnly { get; } = isReadOnly;

    public bool IsRequired { get; } = isRequired;

    /// <summary>The allowed values when the schema fixes them; null for free text.</summary>
    public IReadOnlyList<string>? Choices { get; } = choices;

    public bool HasChoices => Choices is { Count: > 0 };

    public bool IsEditable => !IsReadOnly;

    public string Value
    {
        get => _value;
        set
        {
            if (Set(ref _value, value))
            {
                Error = null;
            }
        }
    }

    public string? Error
    {
        get => _error;
        set => Set(ref _error, value);
    }
}

/// <summary>
/// A schema-driven edit form for one entry (or a new one). Checking required fields here only saves a round trip; the server validates again and its
/// per-field messages are placed on the matching fields. What the person may do comes from the server's permissions, which are for rendering only.
/// </summary>
public sealed class EntityEditViewModel : ScreenViewModel
{
    private readonly IEntityPresenter<DynamicRecord> _presenter;
    private string? _entityId;
    private bool _canUpdate = true;

    public EntityEditViewModel(IEntityPresenter<DynamicRecord> presenter)
    {
        _presenter = presenter;
        SaveCommand = new AsyncCommand(SaveAsync, () => _canUpdate, Report);
        CancelCommand = new AsyncCommand(() => { Closed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; });
    }

    public ObservableCollection<FieldViewModel> Fields { get; } = [];

    public AsyncCommand SaveCommand { get; }

    public AsyncCommand CancelCommand { get; }

    public bool IsNew => _entityId is null;

    public event EventHandler? Saved;

    public event EventHandler? Closed;

    /// <summary>Loads the entry with this id, or a blank form when <paramref name="id"/> is null.</summary>
    public async Task LoadAsync(string? id)
    {
        Error = null;
        IsBusy = true;
        try
        {
            var form = id is null ? await _presenter.GetNewFormAsync().ConfigureAwait(true) : await _presenter.GetFormAsync(id).ConfigureAwait(true);
            if (form is null)
            {
                Error = "That entry no longer exists.";
                return;
            }

            Show(form, id);
        }
        catch (Exception error)
        {
            Report(error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveAsync()
    {
        Error = null;
        foreach (var field in Fields)
        {
            field.Error = null;
        }

        var missing = Fields.Where(f => !f.IsReadOnly && f.IsRequired && string.IsNullOrWhiteSpace(f.Value)).ToList();
        foreach (var field in missing)
        {
            field.Error = $"{field.Label} is required.";
        }

        if (missing.Count > 0)
        {
            Error = "Fill in the highlighted fields.";
            return;
        }

        var values = Fields.Where(f => !f.IsReadOnly).ToDictionary(f => f.Name, f => (string?)f.Value, StringComparer.Ordinal);
        IsBusy = true;
        try
        {
            var data = new EntityDataModel(_entityId ?? string.Empty, values);
            var saved = _entityId is null ? await _presenter.CreateAsync(data).ConfigureAwait(true) : await _presenter.UpdateAsync(_entityId, data).ConfigureAwait(true);
            if (saved is null)
            {
                Error = "That entry no longer exists.";
                return;
            }

            Show(saved, saved.Entity.EntityId);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ApiException refusal) when (refusal.Errors.Count > 0)
        {
            foreach (var field in Fields.Where(f => refusal.Errors.ContainsKey(f.Name)))
            {
                field.Error = string.Join(' ', refusal.Errors[field.Name]);
            }

            Error = refusal.Message;
        }
        catch (Exception error)
        {
            Report(error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(EntityFormModel form, string? id)
    {
        _entityId = string.IsNullOrEmpty(id) ? null : id;
        _canUpdate = form.Permissions?.CanUpdate ?? true;
        Fields.Clear();
        foreach (var field in form.Schema.Fields.OrderBy(f => f.Order))
        {
            form.Entity.Values.TryGetValue(field.Name, out var value);
            Fields.Add(new FieldViewModel(field.Name, field.Label ?? field.Name, value ?? string.Empty, field.IsReadOnly || !_canUpdate, field.IsRequired, field.Choices));
        }

        SaveCommand.RaiseCanExecuteChanged();
    }
}
