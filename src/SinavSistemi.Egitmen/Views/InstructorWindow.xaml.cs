using System.ComponentModel;
using System.Windows;
using SinavSistemi.Shared.Core;
using SinavSistemi.Egitmen.Services;
using SinavSistemi.Egitmen.ViewModels;

namespace SinavSistemi.Egitmen.Views;

public partial class InstructorWindow : Window
{
    private bool _skipPrompt;

    public InstructorWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    /// <summary>Çıkış butonu zaten onay aldıysa tekrar sorma.</summary>
    public void CloseWithoutPrompt()
    {
        _skipPrompt = true;
        Close();
    }

    // Sunucu bu exe'nin içinde: pencere kapanırsa öğrencilerin bağlantısı kopar.
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_skipPrompt) return;

        if (DataContext is InstructorShellViewModel { LiveExam.HasActiveExam: true } &&
            !Dialogs.Confirm("Devam eden bir sınav var. Uygulama kapanırsa sunucu da kapanır ve öğrencilerin bağlantısı kopar.\n\nYine de kapatılsın mı?", "Sınav devam ediyor"))
        {
            e.Cancel = true;
            return;
        }

        if (ServiceLocator.Server.IsRunning) await ServiceLocator.Server.StopAsync();
    }
}
