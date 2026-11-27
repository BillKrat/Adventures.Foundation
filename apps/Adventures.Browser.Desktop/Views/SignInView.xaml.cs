using System.Windows;
using System.Windows.Controls;
using Adventures.Client;

namespace Adventures.Browser.Desktop.Views;

public partial class SignInView : UserControl
{
    public SignInView()
    {
        InitializeComponent();
        Loaded += (_, _) => UserNameBox.Focus();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is SignInViewModel model)
            {
                // the view model clears the password after every attempt; empty the box with it
                model.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SignInViewModel.Password) && model.Password.Length == 0)
                    {
                        PasswordBox.Clear();
                    }
                };
            }
        };
    }

    // A PasswordBox cannot bind (by design), so the view hands the typed password to the view model.
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SignInViewModel model && model.Password != PasswordBox.Password)
        {
            model.Password = PasswordBox.Password;
        }
    }
}
