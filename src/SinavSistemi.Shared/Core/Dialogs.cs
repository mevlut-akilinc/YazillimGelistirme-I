using System.Windows;
using Microsoft.Win32;

namespace SinavSistemi.Shared.Core;

/// <summary>ViewModel'lerin mesaj kutusu / dosya diyaloğu açması için ortak yardımcı.</summary>
public static class Dialogs
{
    public static Window? Owner => Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                   ?? Application.Current.MainWindow;

    private static MessageBoxResult Show(string message, string title, MessageBoxButton b, MessageBoxImage i)
        => Owner is { } o ? MessageBox.Show(o, message, title, b, i) : MessageBox.Show(message, title, b, i);

    public static bool Confirm(string message, string title = "Onay")
        => Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Info(string message, string title = "Bilgi")
        => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(string message, string title = "Hata")
        => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public static string? SaveFile(string filter, string defaultName)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = defaultName };
        var ok = Owner is { } o ? dlg.ShowDialog(o) : dlg.ShowDialog();
        return ok == true ? dlg.FileName : null;
    }
}
