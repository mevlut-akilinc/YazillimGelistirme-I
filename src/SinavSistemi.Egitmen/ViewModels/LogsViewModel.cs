using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Egitmen.Services;

namespace SinavSistemi.Egitmen.ViewModels;

/// <summary>Gönderim tarih/saat logları (vize isteri 6, genel hedef 12).</summary>
public class LogsViewModel : ObservableObject, IPageLifecycle
{
    public const string AllLevels = "Tümü";
    private string _search = "", _level = AllLevels;

    public LogsViewModel()
    {
        View = CollectionViewSource.GetDefaultView(Logs);
        View.SortDescriptions.Add(new SortDescription(nameof(LogEntry.Time), ListSortDirection.Descending));
        View.Filter = o => o is LogEntry l
                           && (Level == AllLevels || SinavSistemi.Shared.Converters.EnumDisplayConverter.ToDisplay(l.Level) == Level)
                           && (string.IsNullOrWhiteSpace(Search)
                               || l.Message.Contains(Search, StringComparison.CurrentCultureIgnoreCase)
                               || l.Source.Contains(Search, StringComparison.CurrentCultureIgnoreCase));

        // Canlı loglar da bu listeye düşsün
        ServiceLocator.Server.LogWritten += (_, l) => Logs.Add(l);

        RefreshCommand = new AsyncRelayCommand(OnNavigatedToAsync);
        ExportCommand = new RelayCommand(Export);
        _ = OnNavigatedToAsync();
    }

    public ObservableCollection<LogEntry> Logs { get; } = new();
    public ICollectionView View { get; }
    public string[] Levels { get; } = { AllLevels, "BİLGİ", "UYARI", "HATA" };

    public string Search { get => _search; set { if (SetProperty(ref _search, value)) View.Refresh(); } }
    public string Level { get => _level; set { if (SetProperty(ref _level, value)) View.Refresh(); } }

    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }

    public async Task OnNavigatedToAsync()
    {
        var stored = await ServiceLocator.Exams.GetLogsAsync();
        Logs.Clear();
        foreach (var l in stored) Logs.Add(l);
    }

    private void Export()
    {
        var path = Dialogs.SaveFile("Log dosyası (*.log)|*.log|Metin (*.txt)|*.txt", $"sinav_{DateTime.Now:yyyyMMdd_HHmm}.log");
        if (path is null) return;
        var lines = View.Cast<LogEntry>().Select(l => $"{l.Time:yyyy-MM-dd HH:mm:ss}\t{SinavSistemi.Shared.Converters.EnumDisplayConverter.ToDisplay(l.Level)}\t{l.Source}\t{l.Message}");
        File.WriteAllLines(path, lines);
        Dialogs.Info("Loglar kaydedildi:\n" + path);
    }
}
