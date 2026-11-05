using System.Windows.Input;
using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Ogrenci.Services;

namespace SinavSistemi.Ogrenci.ViewModels;

/// <summary>Öğrenci penceresi: Bekleme → Sınav → Teslim edildi akışı.</summary>
public class StudentShellViewModel : ObservableObject
{
    private ObservableObject _currentPage;

    public StudentShellViewModel(Session session)
    {
        Session = session;
        _currentPage = new StudentLobbyViewModel(session);

        ServiceLocator.Client.ExamReceived += OnExamReceived;
        ServiceLocator.Client.ExamEnded += OnExamEnded;
        ServiceLocator.Client.ConnectionLost += OnConnectionLost;
        LogoutCommand = new AsyncRelayCommand(LogoutAsync, () => CurrentPage is not ExamSessionViewModel);
    }

    public Session Session { get; }
    public string StudentLine => $"{Session.User.StudentNumber} · {Session.User.FullName}";

    public ObservableObject CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }

    public ICommand LogoutCommand { get; }
    public event Action? LoggedOut;

    private void OnExamReceived(object? sender, Exam exam)
    {
        var vm = new ExamSessionViewModel(exam);
        vm.Submitted += result => CurrentPage = new SubmittedViewModel(exam, result);
        CurrentPage = vm;
    }

    private void OnExamEnded(object? sender, EventArgs e)
    {
        if (CurrentPage is ExamSessionViewModel s) _ = s.ForceSubmitAsync();
    }

    private void OnConnectionLost(object? sender, EventArgs e)
    {
        if (CurrentPage is ExamSessionViewModel)
            Dialogs.Error("Eğitmen bilgisayarıyla bağlantı koptu. Cevaplarınız bu bilgisayarda duruyor; bağlantı gelince tekrar \"Gönder\"e basın.");
    }

    public bool IsInExam => CurrentPage is ExamSessionViewModel { IsLocked: false };

    private async Task LogoutAsync()
    {
        ServiceLocator.Client.ExamReceived -= OnExamReceived;
        ServiceLocator.Client.ExamEnded -= OnExamEnded;
        ServiceLocator.Client.ConnectionLost -= OnConnectionLost;
        await ServiceLocator.Client.DisconnectAsync();
        LoggedOut?.Invoke();
    }
}

public class StudentLobbyViewModel : ObservableObject
{
    public StudentLobbyViewModel(Session session) => Session = session;
    public Session Session { get; }
    public string Greeting => $"Merhaba {Session.User.FullName}";
}

public record SubmitSummary(DateTime SubmittedAt, int Answered, int Total, bool AutoSubmitted);

public class SubmittedViewModel : ObservableObject
{
    public SubmittedViewModel(Exam exam, SubmitSummary summary)
    {
        ExamTitle = exam.Title;
        Summary = summary;
    }

    public string ExamTitle { get; }
    public SubmitSummary Summary { get; }
    public string Detail => $"{Summary.Total} sorudan {Summary.Answered} tanesini cevapladınız.";
    public string When => "Teslim zamanı: " + Summary.SubmittedAt.ToString("dd.MM.yyyy HH:mm:ss");
    public string Reason => Summary.AutoSubmitted ? "Süre dolduğu için cevaplarınız otomatik gönderildi." : "Cevaplarınız eğitmen bilgisayarına iletildi.";
}
