using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Adventures.Browser.Client;

namespace Adventures.Browser.Desktop.Views;

public partial class SignInView : UserControl
{
    public SignInView()
    {
        InitializeComponent();
        Loaded += (_, _) => UserNameBox.Focus();
        DataContextChanged += (_, args) =>
        {
            // one view is reused as the person changes domains: what was typed for one never shows for another
            if (args.OldValue is SiteSignInViewModel old)
            {
                old.PropertyChanged -= OnModelChanged;
            }

            PasswordBox.Clear();
            if (args.NewValue is SiteSignInViewModel model)
            {
                model.PropertyChanged += OnModelChanged;
            }
        };
    }

    // the view model clears the password after every attempt; empty the box with it
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is SiteSignInViewModel { Password.Length: 0 } && e.PropertyName == nameof(SiteSignInViewModel.Password))
        {
            PasswordBox.Clear();
        }
    }

    // A PasswordBox cannot bind (by design), so the view hands the typed password to the view model.
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SiteSignInViewModel model && model.Password != PasswordBox.Password)
        {
            model.Password = PasswordBox.Password;
        }
    }
}
