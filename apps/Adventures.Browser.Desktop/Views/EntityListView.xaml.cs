using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Adventures.Client;

namespace Adventures.Browser.Desktop.Views;

public partial class EntityListView : UserControl
{
    public EntityListView()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is EntityListViewModel model)
            {
                model.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(EntityListViewModel.Columns))
                    {
                        BuildColumns(model);
                    }
                };
                BuildColumns(model);
            }
        };
    }

    // The columns come from the server's schema, so they are built here, not declared: Cells[i] is the text of column i.
    private void BuildColumns(EntityListViewModel model)
    {
        Grid.Columns.Clear();
        for (var i = 0; i < model.Columns.Count; i++)
        {
            Grid.Columns.Add(new DataGridTextColumn { Header = model.Columns[i].Label, Binding = new Binding($"Cells[{i}]"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is EntityListViewModel model && model.EditCommand.CanExecute(null))
        {
            model.EditCommand.Execute(null);
        }
    }
}
