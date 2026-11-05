using System.Windows;
using SinavSistemi.Shared.Models;
using SinavSistemi.Egitmen.ViewModels;

namespace SinavSistemi.Egitmen.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Vm.LoginSucceeded += OnLoginSucceeded;
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private LoginViewModel Vm => (LoginViewModel)DataContext;

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => Vm.Password = PasswordBox.Password;

    private void OnLoginSucceeded(Session session)
    {
        var vm = new InstructorShellViewModel(session);
        var shell = new InstructorWindow { DataContext = vm };
        vm.LoggedOut += () =>
        {
            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            shell.CloseWithoutPrompt();
        };

        Application.Current.MainWindow = shell;
        shell.Show();
        Close();
    }
}
