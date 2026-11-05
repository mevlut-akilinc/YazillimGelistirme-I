using System.Net;
using System.Windows.Input;
using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Ogrenci.Services;

namespace SinavSistemi.Ogrenci.ViewModels;

/// <summary>Öğrenci girişi: bilgiler eğitmen bilgisayarındaki sunucuya gönderilir, orada doğrulanır.</summary>
public class LoginViewModel : ObservableObject
{
    private string _studentNumber = "";
    private string _serverIp = "";
    private string _errorMessage = "";
    private bool _isBusy;

    public LoginViewModel()
    {
        _serverIp = LastServerIp.Load() ?? "127.0.0.1";
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => !IsBusy && StudentNumber.Trim().Length > 0);
    }

    public string Password { private get; set; } = "";

    public string StudentNumber { get => _studentNumber; set => SetProperty(ref _studentNumber, value); }
    public string ServerIp { get => _serverIp; set => SetProperty(ref _serverIp, value); }
    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public ICommand LoginCommand { get; }
    public event Action<Session>? LoginSucceeded;

    private async Task LoginAsync()
    {
        ErrorMessage = "";
        if (string.IsNullOrEmpty(Password)) { ErrorMessage = "Şifre boş olamaz."; return; }
        if (!IPAddress.TryParse(ServerIp.Trim(), out _))
        {
            ErrorMessage = "Geçerli bir IP adresi girin (ör. 192.168.1.10). Aynı bilgisayarda deniyorsanız 127.0.0.1.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await ServiceLocator.Client.LoginAsync(ServerIp.Trim(), ServiceLocator.DefaultPort, StudentNumber.Trim(), Password);
            if (result.Session is null)
            {
                ErrorMessage = result.Error ?? "Giriş başarısız.";
                return;
            }
            LastServerIp.Save(ServerIp.Trim());
            LoginSucceeded?.Invoke(result.Session);
        }
        catch (Exception)
        {
            ErrorMessage = "Eğitmen bilgisayarına bağlanılamadı. Sunucunun başlatıldığından ve aynı ağda olduğunuzdan emin olun.";
        }
        finally { IsBusy = false; }
    }
}

/// <summary>Son girilen sunucu IP'sini hatırlar (her sınavda tekrar yazmamak için).</summary>
internal static class LastServerIp
{
    private static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SinavSistemi", "sunucu_ip.txt");

    public static string? Load()
    {
        try { return System.IO.File.Exists(FilePath) ? System.IO.File.ReadAllText(FilePath).Trim() : null; }
        catch { return null; }
    }

    public static void Save(string ip)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            System.IO.File.WriteAllText(FilePath, ip);
        }
        catch { /* önemli değil */ }
    }
}
