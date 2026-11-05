using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Shared.Protocol;
using SinavSistemi.Ogrenci.Services;

namespace SinavSistemi.Ogrenci.ViewModels;

/// <summary>Öğrencinin sınav çözdüğü ekran. Soru gezgini + sayaç + gönderim.</summary>
public class ExamSessionViewModel : ObservableObject
{
    private readonly Exam _exam;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AnswerViewModel _current;
    private string _remaining = "";
    private bool _isCritical, _isSubmitting, _isLocked;

    public ExamSessionViewModel(Exam exam)
    {
        _exam = exam;
        foreach (var eq in exam.Questions.OrderBy(q => q.Order))
        {
            var a = new AnswerViewModel(eq, exam.Id);
            a.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AnswerViewModel.IsAnswered)) OnAnsweredChanged();
            };
            Answers.Add(a);
        }
        _current = Answers[0];
        _current.IsCurrent = true;

        GoToCommand = new RelayCommand(p => { if (p is AnswerViewModel a) Current = a; });
        NextCommand = new RelayCommand(() => Step(+1), () => Index < Answers.Count - 1);
        PrevCommand = new RelayCommand(() => Step(-1), () => Index > 0);
        SubmitCommand = new AsyncRelayCommand(() => SubmitAsync(auto: false), () => !_isLocked && !_isSubmitting);

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    public string ExamTitle => _exam.Title;
    public int TotalPoints => _exam.TotalPoints;
    public ObservableCollection<AnswerViewModel> Answers { get; } = new();

    public AnswerViewModel Current
    {
        get => _current;
        set
        {
            if (value is null || ReferenceEquals(_current, value)) return;
            _current.IsCurrent = false;
            _current = value;
            _current.IsCurrent = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Index));
            OnPropertyChanged(nameof(PositionText));
        }
    }

    public int Index => Answers.IndexOf(Current);
    public string PositionText => $"Soru {Index + 1} / {Answers.Count}";
    public int AnsweredCount => Answers.Count(a => a.IsAnswered);
    public string ProgressText => $"{AnsweredCount}/{Answers.Count} cevaplandı";

    public string RemainingTime { get => _remaining; private set => SetProperty(ref _remaining, value); }
    public bool IsTimeCritical { get => _isCritical; private set => SetProperty(ref _isCritical, value); }
    public bool IsLocked { get => _isLocked; private set => SetProperty(ref _isLocked, value); }

    public ICommand GoToCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PrevCommand { get; }
    public ICommand SubmitCommand { get; }

    public event Action<SubmitSummary>? Submitted;

    /// <summary>Eğitmen sınavı bitirdiğinde (ExamEnded mesajı) mevcut cevaplar otomatik gönderilir.</summary>
    public Task ForceSubmitAsync() => IsLocked ? Task.CompletedTask : SubmitAsync(auto: true);

    private void Step(int d)
    {
        int i = Index + d;
        if (i >= 0 && i < Answers.Count) Current = Answers[i];
    }

    private void OnAnsweredChanged()
    {
        OnPropertyChanged(nameof(AnsweredCount));
        OnPropertyChanged(nameof(ProgressText));
        _ = ServiceLocator.Client.SendProgressAsync(_exam.Id, AnsweredCount);
    }

    private void Tick()
    {
        if (_exam.EndsAt is not DateTime end) { RemainingTime = "Süresiz"; return; }
        var left = end - DateTime.Now;
        if (left <= TimeSpan.Zero)
        {
            RemainingTime = "00:00";
            _timer.Stop();
            if (!_isLocked) _ = SubmitAsync(auto: true);
            return;
        }
        RemainingTime = left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        IsTimeCritical = left.TotalSeconds <= 60;
    }

    private async Task SubmitAsync(bool auto)
    {
        if (!auto)
        {
            int empty = Answers.Count - AnsweredCount;
            var msg = empty > 0
                ? $"{empty} soru boş. Yine de göndermek istiyor musunuz?\nGönderdikten sonra değişiklik yapamazsınız."
                : "Cevaplarınız gönderilecek. Gönderdikten sonra değişiklik yapamazsınız.";
            if (!Dialogs.Confirm(msg, "Sınavı Bitir")) return;
        }

        _isSubmitting = true;
        IsLocked = true;
        _timer.Stop();
        try
        {
            SubmitResultPayload result = await ServiceLocator.Client.SubmitAsync(_exam.Id, Answers.Select(a => a.ToModel()).ToList());
            if (!result.Accepted)
            {
                // Sunucu süre dolduğu için reddetti; cevaplar kilitli kalır
                Dialogs.Error("Gönderim kabul edilmedi: " + (result.Reason ?? "bilinmeyen sebep"));
                return;
            }
            Submitted?.Invoke(new SubmitSummary(result.ServerTime, AnsweredCount, Answers.Count, auto));
        }
        catch (Exception ex)
        {
            IsLocked = false;
            if (!auto) _timer.Start();
            Dialogs.Error("Gönderim başarısız: " + ex.Message + "\nBağlantınızı kontrol edip tekrar deneyin.");
        }
        finally
        {
            _isSubmitting = false;
        }
    }
}

public class AnswerViewModel : ObservableObject
{
    private readonly int _examId;
    private QuestionOption? _selectedOption;
    private string _text = "", _code;
    private bool _isCurrent, _isRunning;
    private CodeRunResult? _runResult;

    public AnswerViewModel(ExamQuestion eq, int examId)
    {
        _examId = examId;
        Number = eq.Order;
        Points = eq.Points;
        Question = eq.Question;
        _code = eq.Question.StarterCode ?? "#include <stdio.h>\n\nint main(void) {\n    \n    return 0;\n}\n";
        RunCodeCommand = new AsyncRelayCommand(RunCodeAsync, () => !_isRunning);
    }

    public int Number { get; }
    public int Points { get; }
    public Question Question { get; }

    public bool IsMultipleChoice => Question.Type == QuestionType.CoktanSecmeli;
    public bool IsClassic => Question.Type == QuestionType.Klasik;
    public bool IsCoding => Question.Type == QuestionType.Kodlama;

    public QuestionOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            // Soru değiştirirken ListBox geçici olarak null yazabiliyor; şık kaldırma diye bir eylem yok, yok sayıyoruz.
            if (value is null) return;
            if (SetProperty(ref _selectedOption, value)) OnPropertyChanged(nameof(IsAnswered));
        }
    }

    public string Text
    {
        get => _text;
        set { if (SetProperty(ref _text, value)) OnPropertyChanged(nameof(IsAnswered)); }
    }

    public string Code
    {
        get => _code;
        set { if (SetProperty(ref _code, value)) OnPropertyChanged(nameof(IsAnswered)); }
    }

    public bool IsAnswered => Question.Type switch
    {
        QuestionType.CoktanSecmeli => SelectedOption is not null,
        QuestionType.Klasik => !string.IsNullOrWhiteSpace(Text),
        QuestionType.Kodlama => RunResult is not null || Code != (Question.StarterCode ?? Code),
        _ => false
    };

    public bool IsCurrent { get => _isCurrent; set => SetProperty(ref _isCurrent, value); }

    // ---- Kodlama (final) ----
    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }
    public CodeRunResult? RunResult
    {
        get => _runResult;
        private set
        {
            if (SetProperty(ref _runResult, value))
            {
                OnPropertyChanged(nameof(TestSummary));
                OnPropertyChanged(nameof(ComplexitySummary));
                OnPropertyChanged(nameof(IsAnswered));
            }
        }
    }
    public string TestSummary => RunResult is null ? "" :
        RunResult.Compiled ? $"{RunResult.PassedTests}/{RunResult.TotalTests} test geçti" : "Derleme hatası";
    // Hibrit model: burada sadece görünür testler çalışır. Gizli testler ve zaman/yer karmaşıklığı
    // teslimden sonra eğitmen bilgisayarında ölçülür; öğrenci bu değerleri göremez/değiştiremez.
    public string ComplexitySummary => RunResult is { Compiled: true }
        ? "Gizli testler ve karmaşıklık analizi teslimden sonra sunucuda yapılır."
        : "";

    public ICommand RunCodeCommand { get; }

    private async Task RunCodeAsync()
    {
        IsRunning = true;
        try { RunResult = await ServiceLocator.CodeRunner.CompileAndTestAsync(Code, Question.TestCases); }
        finally { IsRunning = false; }
    }

    public StudentAnswer ToModel() => new()
    {
        QuestionId = Question.Id,
        SelectedOptionId = SelectedOption?.Id,
        Text = IsClassic ? Text : null,
        Code = IsCoding ? Code : null
    };
}
