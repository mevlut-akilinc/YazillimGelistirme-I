using System.Windows;
using System.Windows.Threading;

namespace SinavSistemi.Ogrenci;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Login penceresi kapanıp kabuk açılırken uygulama kapanmasın
        ShutdownMode = ShutdownMode.OnLastWindowClose;

        DispatcherUnhandledException += OnUnhandled;
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Beklenmeyen bir hata oluştu:\n" + e.Exception.Message, "Hata",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
