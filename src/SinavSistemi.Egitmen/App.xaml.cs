using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using SinavSistemi.Egitmen.Services;
using SinavSistemi.Egitmen.Services.Data;

namespace SinavSistemi.Egitmen;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Login penceresi kapanıp kabuk açılırken uygulama kapanmasın
        ShutdownMode = ShutdownMode.OnLastWindowClose;

        DispatcherUnhandledException += OnUnhandled;

        if (!InitializeServices())
            Shutdown(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Db.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Ayarları okur, UseDatabase=true ise PostgreSQL bağlantısını test eder ve servisleri oluşturur.
    /// Bağlantı kurulamazsa sessizce mock'a düşmez: kullanıcıya açıklama gösterip false döner.
    /// </summary>
    private static bool InitializeServices()
    {
        IConfiguration config;
        try
        {
            config = Db.LoadConfiguration();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Db.SettingsFile} okunamadı:\n{ex.Message}", "Ayar hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        if (Db.UseDatabase(config))
        {
            try
            {
                // OnStartup senkron; pencere açılmadan önce testin bitmesi gerekiyor.
                // Async çağrı thread pool'da çalıştırılıp beklenir (UI thread'inde kilitlenme olmaz).
                Task.Run(() => Db.ConnectAsync(config)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "PostgreSQL servisine bağlanılamadı. services.msc'de postgresql-x64-18 çalışıyor mu? " +
                    $"Bağlantı ayarı: {Db.LocalSettingsFile}\n\nAyrıntı: {ex.Message}",
                    "Veritabanı hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        ServiceLocator.Initialize(config);
        return true;
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Beklenmeyen bir hata oluştu:\n" + e.Exception.Message, "Hata",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
