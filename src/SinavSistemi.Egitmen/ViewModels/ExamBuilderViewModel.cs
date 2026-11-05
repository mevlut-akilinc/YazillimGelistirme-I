using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Egitmen.Services;

namespace SinavSistemi.Egitmen.ViewModels;

public class ExamQuestionItem : ObservableObject
{
    private int _order, _points;
    public ExamQuestionItem(Question q) { Question = q; _points = q.DefaultPoints; }
    public Question Question { get; }
    public int Order { get => _order; set => SetProperty(ref _order, value); }
    public int Points { get => _points; set => SetProperty(ref _points, value); }
}

/// <summary>Soru bankasından sınav/ödev oluşturma.</summary>
public class ExamBuilderViewModel : ObservableObject, IPageLifecycle
{
    private readonly Action<Exam> _onDistribute;
    private string _title = "", _className = "1-A", _search = "", _error = "";
    private int _duration = 40;
    private double _testCaseWeight = 70;

    public ExamBuilderViewModel(Action<Exam> onDistribute)
    {
        _onDistribute = onDistribute;
        BankView = CollectionViewSource.GetDefaultView(Bank);
        BankView.Filter = o => o is Question q
                               && Selected.All(s => s.Question.Id != q.Id)
                               && (string.IsNullOrWhiteSpace(Search) || q.Text.Contains(Search, StringComparison.CurrentCultureIgnoreCase)
                                   || q.Category.Contains(Search, StringComparison.CurrentCultureIgnoreCase));

        Selected.CollectionChanged += (_, _) => { Renumber(); OnPropertyChanged(nameof(TotalPoints)); OnPropertyChanged(nameof(QuestionCount)); };

        AddCommand = new RelayCommand(p => { if (p is Question q) { var it = new ExamQuestionItem(q); it.PropertyChanged += (_, _) => OnPropertyChanged(nameof(TotalPoints)); Selected.Add(it); BankView.Refresh(); } });
        RemoveCommand = new RelayCommand(p => { if (p is ExamQuestionItem it) { Selected.Remove(it); BankView.Refresh(); } });
        UpCommand = new RelayCommand(p => Move(p as ExamQuestionItem, -1));
        DownCommand = new RelayCommand(p => Move(p as ExamQuestionItem, +1));
        SaveCommand = new AsyncRelayCommand(() => SaveAsync(false));
        SaveAndDistributeCommand = new AsyncRelayCommand(() => SaveAsync(true));
        ClearCommand = new RelayCommand(Clear);

        _ = OnNavigatedToAsync();
    }

    public ObservableCollection<Question> Bank { get; } = new();
    public ICollectionView BankView { get; }
    public ObservableCollection<ExamQuestionItem> Selected { get; } = new();

    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string ClassName { get => _className; set => SetProperty(ref _className, value); }
    public int DurationMinutes { get => _duration; set => SetProperty(ref _duration, value); }
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) BankView.Refresh(); } }
    public string Error { get => _error; set => SetProperty(ref _error, value); }

    /// <summary>Kodlama puan ağırlığı: test case yüzdesi (kalanı karmaşıklık). Final isteri.</summary>
    public double TestCaseWeight
    {
        get => _testCaseWeight;
        set { if (SetProperty(ref _testCaseWeight, Math.Round(value))) OnPropertyChanged(nameof(ComplexityWeight)); }
    }
    public double ComplexityWeight => 100 - TestCaseWeight;
    public bool HasCodingQuestion => Selected.Any(s => s.Question.Type == QuestionType.Kodlama);

    public int TotalPoints => Selected.Sum(s => s.Points);
    public int QuestionCount => Selected.Count;

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAndDistributeCommand { get; }
    public ICommand ClearCommand { get; }

    public async Task OnNavigatedToAsync()
    {
        Bank.Clear();
        foreach (var q in await ServiceLocator.Questions.GetAllAsync()) Bank.Add(q);
        BankView.Refresh();
    }

    private void Renumber()
    {
        for (int i = 0; i < Selected.Count; i++) Selected[i].Order = i + 1;
        OnPropertyChanged(nameof(HasCodingQuestion));
    }

    private void Move(ExamQuestionItem? it, int delta)
    {
        if (it is null) return;
        int i = Selected.IndexOf(it), j = i + delta;
        if (j < 0 || j >= Selected.Count) return;
        Selected.Move(i, j);
    }

    private void Clear()
    {
        Selected.Clear();
        Title = "";
        Error = "";
        BankView.Refresh();
    }

    private async Task SaveAsync(bool distribute)
    {
        Error = "";
        if (string.IsNullOrWhiteSpace(Title)) { Error = "Sınav başlığı girin."; return; }
        if (DurationMinutes <= 0) { Error = "Süre 0'dan büyük olmalı."; return; }
        if (Selected.Count == 0) { Error = "En az bir soru ekleyin."; return; }

        var exam = new Exam
        {
            Title = Title.Trim(),
            ClassName = ClassName.Trim(),
            DurationMinutes = DurationMinutes,
            TestCaseWeight = TestCaseWeight / 100.0,
            ComplexityWeight = ComplexityWeight / 100.0,
            Questions = Selected.Select(s => new ExamQuestion { Question = s.Question, Order = s.Order, Points = s.Points }).ToList()
        };
        exam = await ServiceLocator.Exams.SaveAsync(exam);

        if (distribute) _onDistribute(exam);
        else Dialogs.Info($"\"{exam.Title}\" taslak olarak kaydedildi.");
        Clear();
    }
}
