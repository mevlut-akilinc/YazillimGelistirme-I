using System.ComponentModel;
using System.Windows;
using SinavSistemi.Shared.Core;
using SinavSistemi.Ogrenci.Services;
using SinavSistemi.Ogrenci.ViewModels;

namespace SinavSistemi.Ogrenci.Views;

public partial class StudentWindow : Window
{
    public StudentWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is StudentShellViewModel { IsInExam: true } &&
            !Dialogs.Confirm("Sınav devam ediyor ve cevaplarınız henüz gönderilmedi.\nKapatırsanız cevaplarınız kaybolur. Emin misiniz?", "Sınav devam ediyor"))
        {
            e.Cancel = true;
            return;
        }
        _ = ServiceLocator.Client.DisconnectAsync();
    }
}
