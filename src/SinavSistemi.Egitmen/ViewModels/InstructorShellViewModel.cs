using System.Windows.Input;
using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Egitmen.Services;

namespace SinavSistemi.Egitmen.ViewModels;

/// <summary>Eğitmen penceresinin kabuğu: sol menü + aktif sayfa.</summary>
public class InstructorShellViewModel : ObservableObject
{
    private ObservableObject _currentPage;
    private string _currentKey = "dashboard";

    public InstructorShellViewModel(Session session)
    {
        Session = session;

        Dashboard = new DashboardViewModel(Navigate);
        QuestionBank = new QuestionBankViewModel();
        ExamBuilder = new ExamBuilderViewModel(OnExamReadyToDistribute);
        LiveExam = new LiveExamViewModel();
        Results = new ResultsViewModel();
        Logs = new LogsViewModel();

        _currentPage = Dashboard;
        NavigateCommand = new RelayCommand(p => Navigate(p as string ?? "dashboard"));
        LogoutCommand = new AsyncRelayCommand(LogoutAsync);
    }

    public Session Session { get; }
    public string UserName => Session.User.FullName;

    public DashboardViewModel Dashboard { get; }
    public QuestionBankViewModel QuestionBank { get; }
    public ExamBuilderViewModel ExamBuilder { get; }
    public LiveExamViewModel LiveExam { get; }
    public ResultsViewModel Results { get; }
    public LogsViewModel Logs { get; }

    public ObservableObject CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }

    /// <summary>Sol menüdeki RadioButton'ların IsChecked durumu için.</summary>
    public string CurrentKey { get => _currentKey; private set => SetProperty(ref _currentKey, value); }

    public ICommand NavigateCommand { get; }
    public ICommand LogoutCommand { get; }

    public event Action? LoggedOut;

    public void Navigate(string key)
    {
        CurrentPage = key switch
        {
            "questions" => QuestionBank,
            "builder" => ExamBuilder,
            "live" => LiveExam,
            "results" => Results,
            "logs" => Logs,
            _ => Dashboard
        };
        CurrentKey = key;
        if (CurrentPage is IPageLifecycle page) _ = page.OnNavigatedToAsync();
    }

    private void OnExamReadyToDistribute(Exam exam)
    {
        LiveExam.SelectExam(exam);
        Navigate("live");
    }

    private async Task LogoutAsync()
    {
        if (LiveExam.HasActiveExam && !Dialogs.Confirm("Devam eden bir sınav var. Çıkış yapılırsa sunucu kapanır. Emin misiniz?"))
            return;
        if (ServiceLocator.Server.IsRunning) await ServiceLocator.Server.StopAsync();
        await ServiceLocator.Auth.LogoutAsync();
        LoggedOut?.Invoke();
    }
}

/// <summary>Sayfa her açıldığında veriyi yenilemek için.</summary>
public interface IPageLifecycle
{
    Task OnNavigatedToAsync();
}
