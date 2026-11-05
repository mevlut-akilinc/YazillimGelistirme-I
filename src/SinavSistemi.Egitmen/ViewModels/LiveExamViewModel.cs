using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Egitmen.Services;

namespace SinavSistemi.Egitmen.ViewModels;

/// <summary>Sunucuyu başlatma, sınavı LAN'a dağıtma ve canlı takip.</summary>
public class LiveExamViewModel : ObservableObject, IPageLifecycle
{
    private readonly IExamServer _server = ServiceLocator.Server;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    private Exam? _selectedExam;
    private Exam? _activeExam;
    private string _remaining = "--:--";
    private int _port = ServiceLocator.DefaultPort;

    public LiveExamViewModel()
    {
        _server.StudentConnected += (_, s) => { Students.Add(s); RefreshCounts(); };
        _server.StudentChanged += (_, _) => RefreshCounts();
        _server.LogWritten += (_, l) =>
        {
            LiveLog.Insert(0, l);
            if (LiveLog.Count > 100) LiveLog.RemoveAt(LiveLog.Count - 1);
        };
        _clock.Tick += (_, _) => Tick();

        StartServerCommand = new AsyncRelayCommand(StartServerAsync, () => !_server.IsRunning);
        StopServerCommand = new AsyncRelayCommand(StopServerAsync, () => _server.IsRunning && !HasActiveExam);
        DistributeCommand = new AsyncRelayCommand(DistributeAsync,
            () => _server.IsRunning && SelectedExam is not null && !HasActiveExam && Students.Count > 0);
        EndExamCommand = new AsyncRelayCommand(EndExamAsync, () => HasActiveExam);

        _ = OnNavigatedToAsync();
    }

    public ObservableCollection<Exam> Exams { get; } = new();
    public ObservableCollection<ConnectedStudent> Students { get; } = new();
    public ObservableCollection<LogEntry> LiveLog { get; } = new();

    public Exam? SelectedExam { get => _selectedExam; set => SetProperty(ref _selectedExam, value); }

    public Exam? ActiveExam
    {
        get => _activeExam;
        private set { if (SetProperty(ref _activeExam, value)) OnPropertyChanged(nameof(HasActiveExam)); }
    }
    public bool HasActiveExam => ActiveExam is not null;

    public bool IsServerRunning => _server.IsRunning;
    public string ServerAddress => _server.IsRunning ? $"{_server.LocalAddress}:{_server.Port}" : "Sunucu kapalı";
    public int Port { get => _port; set => SetProperty(ref _port, value); }
    public string RemainingTime { get => _remaining; private set => SetProperty(ref _remaining, value); }

    public int ConnectedCount => Students.Count(s => s.State != ClientState.BaglantiKoptu);
    public int InExamCount => Students.Count(s => s.State == ClientState.SinavDa);
    public int SubmittedCount => Students.Count(s => s.State == ClientState.TeslimEtti);

    public ICommand StartServerCommand { get; }
    public ICommand StopServerCommand { get; }
    public ICommand DistributeCommand { get; }
    public ICommand EndExamCommand { get; }

    public void SelectExam(Exam exam)
    {
        if (!Exams.Contains(exam)) Exams.Insert(0, exam);
        SelectedExam = exam;
    }

    public async Task OnNavigatedToAsync()
    {
        var selectedId = SelectedExam?.Id;
        Exams.Clear();
        foreach (var e in await ServiceLocator.Exams.GetExamsAsync()) Exams.Add(e);
        SelectedExam = Exams.FirstOrDefault(e => e.Id == selectedId) ?? SelectedExam ?? Exams.FirstOrDefault();
    }

    private async Task StartServerAsync()
    {
        try
        {
            await _server.StartAsync(Port);
        }
        catch (Exception ex)
        {
            Dialogs.Error("Sunucu başlatılamadı: " + ex.Message + "\nPort başka bir uygulama tarafından kullanılıyor olabilir veya güvenlik duvarı engelliyor olabilir.");
        }
        OnPropertyChanged(nameof(IsServerRunning));
        OnPropertyChanged(nameof(ServerAddress));
    }

    private async Task StopServerAsync()
    {
        await _server.StopAsync();
        Students.Clear();
        RefreshCounts();
        OnPropertyChanged(nameof(IsServerRunning));
        OnPropertyChanged(nameof(ServerAddress));
    }

    private async Task DistributeAsync()
    {
        if (SelectedExam is null) return;
        if (!Dialogs.Confirm($"\"{SelectedExam.Title}\" {ConnectedCount} öğrenciye gönderilecek ve {SelectedExam.DurationMinutes} dakikalık süre başlayacak. Devam edilsin mi?"))
            return;

        SelectedExam.StartedAt = DateTime.Now;
        SelectedExam.Status = ExamStatus.Yayinda;
        await _server.BroadcastExamAsync(SelectedExam);
        ActiveExam = SelectedExam;
        _clock.Start();
        Tick();
        RefreshCounts();
    }

    private async Task EndExamAsync()
    {
        if (ActiveExam is null) return;
        if (!Dialogs.Confirm("Sınav şimdi sonlandırılsın mı? Bu andan sonra gönderim kabul edilmez.")) return;
        await FinishAsync();
    }

    private async Task FinishAsync()
    {
        if (ActiveExam is null) return;
        _clock.Stop();
        ActiveExam.Status = ExamStatus.Bitti;
        await _server.EndExamAsync(ActiveExam.Id);
        ActiveExam = null;
        RemainingTime = "Bitti";
        RefreshCounts();
    }

    private void Tick()
    {
        if (ActiveExam?.EndsAt is not DateTime end) return;
        var left = end - DateTime.Now;
        if (left <= TimeSpan.Zero)
        {
            _ = FinishAsync();   // süre doldu → sunucu yeni gönderimleri reddeder
            return;
        }
        RemainingTime = left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(ConnectedCount));
        OnPropertyChanged(nameof(InExamCount));
        OnPropertyChanged(nameof(SubmittedCount));
        CommandManager.InvalidateRequerySuggested(); // "Sınavı dağıt" butonu öğrenci gelince aktifleşsin
    }
}
