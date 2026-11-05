using System.Windows.Input;
using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Egitmen.Services;

namespace SinavSistemi.Egitmen.ViewModels;

/// <summary>Eğitmen girişi (yerel veritabanı, ağ gerekmez).</summary>
public class LoginViewModel : ObservableObject
{
    private string _username = "";
    private string _errorMessage = "";
    private bool _isBusy;

    public LoginViewModel()
    {
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy && Username.Trim().Length > 0);
    }

    /// <summary>PasswordBox bağlanamadığı için code-behind'dan set edilir.</summary>
    public string Password { private get; set; } = "";

    public string Username { get => _username; set => SetProperty(ref _username, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public ICommand LoginCommand { get; }
    public event Action<Session>? LoginSucceeded;

    private async Task LoginAsync()
    {
        ErrorMessage = "";
        if (string.IsNullOrEmpty(Password)) { ErrorMessage = "Şifre boş olamaz."; return; }

        IsBusy = true;
        try
        {
            var session = await ServiceLocator.Auth.LoginAsync(Username.Trim(), Password);
            if (session is null) { ErrorMessage = "Kullanıcı adı veya şifre hatalı."; return; }
            LoginSucceeded?.Invoke(session);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Veritabanına ulaşılamadı: " + ex.Message;
        }
        finally { IsBusy = false; }
    }
}
