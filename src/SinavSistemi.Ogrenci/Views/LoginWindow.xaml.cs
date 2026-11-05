using System.Windows;
using SinavSistemi.Shared.Models;
using SinavSistemi.Ogrenci.ViewModels;

namespace SinavSistemi.Ogrenci.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Vm.LoginSucceeded += OnLoginSucceeded;
        Loaded += (_, _) => NumberBox.Focus();
    }

    private LoginViewModel Vm => (LoginViewModel)DataContext;

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => Vm.Password = PasswordBox.Password;

    private void OnLoginSucceeded(Session session)
    {
        var vm = new StudentShellViewModel(session);
        var shell = new StudentWindow { DataContext = vm };
        vm.LoggedOut += () =>
        {
            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            shell.Close();
        };

        Application.Current.MainWindow = shell;
        shell.Show();
        Close();
    }
}
