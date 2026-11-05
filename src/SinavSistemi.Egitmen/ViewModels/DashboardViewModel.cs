using System.Collections.ObjectModel;
using System.Windows.Input;
using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Egitmen.Services;

namespace SinavSistemi.Egitmen.ViewModels;

public class DashboardViewModel : ObservableObject, IPageLifecycle
{
    private int _questionCount, _mcCount, _classicCount, _codeCount, _examCount;
    private string _serverStatus = "Kapalı";

    public DashboardViewModel(Action<string> navigate)
    {
        GoCommand = new RelayCommand(p => navigate(p as string ?? "dashboard"));
        _ = OnNavigatedToAsync();
    }

    public int QuestionCount { get => _questionCount; set => SetProperty(ref _questionCount, value); }
    public int McCount { get => _mcCount; set => SetProperty(ref _mcCount, value); }
    public int ClassicCount { get => _classicCount; set => SetProperty(ref _classicCount, value); }
    public int CodeCount { get => _codeCount; set => SetProperty(ref _codeCount, value); }
    public int ExamCount { get => _examCount; set => SetProperty(ref _examCount, value); }
    public string ServerStatus { get => _serverStatus; set => SetProperty(ref _serverStatus, value); }

    public ObservableCollection<Exam> RecentExams { get; } = new();

    public ICommand GoCommand { get; }

    public async Task OnNavigatedToAsync()
    {
        var questions = await ServiceLocator.Questions.GetAllAsync();
        QuestionCount = questions.Count;
        McCount = questions.Count(q => q.Type == QuestionType.CoktanSecmeli);
        ClassicCount = questions.Count(q => q.Type == QuestionType.Klasik);
        CodeCount = questions.Count(q => q.Type == QuestionType.Kodlama);

        var exams = await ServiceLocator.Exams.GetExamsAsync();
        ExamCount = exams.Count;
        RecentExams.Clear();
        foreach (var e in exams.OrderByDescending(e => e.Id).Take(5)) RecentExams.Add(e);

        var srv = ServiceLocator.Server;
        ServerStatus = srv.IsRunning ? $"Açık · {srv.LocalAddress}:{srv.Port}" : "Kapalı";
    }
}
