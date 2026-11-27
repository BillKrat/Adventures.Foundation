using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace Adventures.Browser.Web.Components;

/// <summary>A view of one view model: draws again when the model, or a collection it names in <see cref="Collections"/>, says it changed.</summary>
public abstract class ModelView<TModel> : ComponentBase, IDisposable
    where TModel : class, INotifyPropertyChanged
{
    private readonly List<INotifyCollectionChanged> _watched = [];
    private TModel? _model;

    [Parameter, EditorRequired]
    public TModel Model { get; set; } = default!;

    protected virtual IEnumerable<INotifyCollectionChanged> Collections(TModel model) => [];

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_model, Model))
        {
            return;
        }

        Forget();
        _model = Model;
        _model.PropertyChanged += OnChanged;
        foreach (var collection in Collections(_model))
        {
            collection.CollectionChanged += OnCollectionChanged;
            _watched.Add(collection);
        }
    }

    public void Dispose()
    {
        Forget();
        GC.SuppressFinalize(this);
    }

    private void Forget()
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnChanged;
        }

        _watched.ForEach(c => c.CollectionChanged -= OnCollectionChanged);
        _watched.Clear();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => InvokeAsync(StateHasChanged);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvokeAsync(StateHasChanged);
}
