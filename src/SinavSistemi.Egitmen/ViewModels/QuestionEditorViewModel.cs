using System.Collections.ObjectModel;
using System.Windows.Input;
using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Egitmen.ViewModels;

public class OptionEditItem : ObservableObject
{
    private string _label = "", _text = "";
    private bool _isCorrect;
    public int Id { get; set; }
    public string Label { get => _label; set => SetProperty(ref _label, value); }
    public string Text { get => _text; set => SetProperty(ref _text, value); }
    public bool IsCorrect { get => _isCorrect; set => SetProperty(ref _isCorrect, value); }
}

public class TestCaseEditItem : ObservableObject
{
    private string _input = "", _expected = "";
    private bool _isHidden;
    public int Id { get; set; }
    public string Input { get => _input; set => SetProperty(ref _input, value); }
    public string ExpectedOutput { get => _expected; set => SetProperty(ref _expected, value); }
    public bool IsHidden { get => _isHidden; set => SetProperty(ref _isHidden, value); }
}

public record ChoiceItem<T>(T Value, string Text);

/// <summary>Soru ekle / düzenle penceresi.</summary>
public class QuestionEditorViewModel : ObservableObject
{
    // En fazla 5 şık: veritabanındaki question_options.label CHECK kısıtı ('A'..'E') ile aynı olmalı.
    // "Şık ekle" butonu Options.Count < Labels.Length olduğu sürece aktiftir.
    private static readonly string[] Labels = { "A", "B", "C", "D", "E" };

    private readonly int _id;
    private QuestionType _type = QuestionType.CoktanSecmeli;
    private string _category = "", _text = "", _expectedAnswer = "", _starterCode = "", _error = "";
    private int _points = 10;
    private Complexity _expectedTime = Complexity.ON, _expectedSpace = Complexity.O1;

    public QuestionEditorViewModel(Question? existing, IEnumerable<string> categories)
    {
        Categories = categories.ToList();
        IsEdit = existing is not null;

        if (existing is not null)
        {
            _id = existing.Id;
            _type = existing.Type;
            _category = existing.Category;
            _text = existing.Text;
            _points = existing.DefaultPoints;
            _expectedAnswer = existing.ExpectedAnswer ?? "";
            _starterCode = existing.StarterCode ?? "";
            _expectedTime = existing.ExpectedTimeComplexity ?? Complexity.ON;
            _expectedSpace = existing.ExpectedSpaceComplexity ?? Complexity.O1;
            foreach (var o in existing.Options)
                Options.Add(new OptionEditItem { Id = o.Id, Label = o.Label, Text = o.Text, IsCorrect = o.IsCorrect });
            foreach (var t in existing.TestCases)
                TestCases.Add(new TestCaseEditItem { Id = t.Id, Input = t.Input, ExpectedOutput = t.ExpectedOutput, IsHidden = t.IsHidden });
        }

        if (Options.Count == 0) for (int i = 0; i < 4; i++) Options.Add(new OptionEditItem { Label = Labels[i] });
        if (TestCases.Count == 0) TestCases.Add(new TestCaseEditItem());

        AddOptionCommand = new RelayCommand(AddOption, () => Options.Count < Labels.Length);
        RemoveOptionCommand = new RelayCommand(p => { if (p is OptionEditItem o && Options.Count > 2) { Options.Remove(o); Relabel(); } });
        AddTestCaseCommand = new RelayCommand(() => TestCases.Add(new TestCaseEditItem()));
        RemoveTestCaseCommand = new RelayCommand(p => { if (p is TestCaseEditItem t && TestCases.Count > 1) TestCases.Remove(t); });
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
    }

    public bool IsEdit { get; }
    public string WindowTitle => IsEdit ? "Soruyu Düzenle" : "Yeni Soru";

    public List<string> Categories { get; }

    public List<ChoiceItem<QuestionType>> TypeChoices { get; } = new()
    {
        new(QuestionType.CoktanSecmeli, "Çoktan Seçmeli"),
        new(QuestionType.Klasik, "Klasik"),
        new(QuestionType.Kodlama, "Kodlama (C) — final"),
    };

    public List<ChoiceItem<Complexity>> ComplexityChoices { get; } =
        Enum.GetValues<Complexity>().Select(c => new ChoiceItem<Complexity>(c, SinavSistemi.Shared.Converters.EnumDisplayConverter.ToDisplay(c))).ToList();

    public QuestionType Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value))
            {
                OnPropertyChanged(nameof(IsMultipleChoice));
                OnPropertyChanged(nameof(IsClassic));
                OnPropertyChanged(nameof(IsCoding));
            }
        }
    }

    public bool IsMultipleChoice => Type == QuestionType.CoktanSecmeli;
    public bool IsClassic => Type == QuestionType.Klasik;
    public bool IsCoding => Type == QuestionType.Kodlama;

    public string Category { get => _category; set => SetProperty(ref _category, value); }
    public string Text { get => _text; set => SetProperty(ref _text, value); }
    public int Points { get => _points; set => SetProperty(ref _points, value); }
    public string ExpectedAnswer { get => _expectedAnswer; set => SetProperty(ref _expectedAnswer, value); }
    public string StarterCode { get => _starterCode; set => SetProperty(ref _starterCode, value); }
    public Complexity ExpectedTime { get => _expectedTime; set => SetProperty(ref _expectedTime, value); }
    public Complexity ExpectedSpace { get => _expectedSpace; set => SetProperty(ref _expectedSpace, value); }
    public string Error { get => _error; set => SetProperty(ref _error, value); }

    public ObservableCollection<OptionEditItem> Options { get; } = new();
    public ObservableCollection<TestCaseEditItem> TestCases { get; } = new();

    public ICommand AddOptionCommand { get; }
    public ICommand RemoveOptionCommand { get; }
    public ICommand AddTestCaseCommand { get; }
    public ICommand RemoveTestCaseCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public Question? Result { get; private set; }
    public event Action<bool>? CloseRequested;

    private void AddOption()
    {
        Options.Add(new OptionEditItem());
        Relabel();
    }

    private void Relabel()
    {
        for (int i = 0; i < Options.Count; i++) Options[i].Label = Labels[i];
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Text)) return "Soru metni boş olamaz.";
        if (string.IsNullOrWhiteSpace(Category)) return "Bir kategori seçin veya yazın.";
        if (Points <= 0) return "Puan 0'dan büyük olmalı.";

        switch (Type)
        {
            case QuestionType.CoktanSecmeli:
                if (Options.Any(o => string.IsNullOrWhiteSpace(o.Text))) return "Tüm şıkları doldurun.";
                if (Options.Count(o => o.IsCorrect) != 1) return "Tam olarak bir doğru şık işaretleyin.";
                break;
            case QuestionType.Klasik:
                if (string.IsNullOrWhiteSpace(ExpectedAnswer)) return "Otomatik puanlama için beklenen cevabı girin.";
                break;
            case QuestionType.Kodlama:
                if (TestCases.Any(t => string.IsNullOrWhiteSpace(t.ExpectedOutput))) return "Her test senaryosunun beklenen çıktısı olmalı.";
                break;
        }
        return null;
    }

    private void Save()
    {
        var err = Validate();
        if (err is not null) { Error = err; return; }

        Result = new Question
        {
            Id = _id,
            Type = Type,
            Category = Category.Trim(),
            Text = Text.Trim(),
            DefaultPoints = Points,
            Options = IsMultipleChoice
                ? Options.Select(o => new QuestionOption { Id = o.Id, Label = o.Label, Text = o.Text.Trim(), IsCorrect = o.IsCorrect }).ToList()
                : new(),
            ExpectedAnswer = IsClassic ? ExpectedAnswer.Trim() : null,
            TestCases = IsCoding
                ? TestCases.Select(t => new TestCase { Id = t.Id, Input = t.Input, ExpectedOutput = t.ExpectedOutput, IsHidden = t.IsHidden }).ToList()
                : new(),
            ExpectedTimeComplexity = IsCoding ? ExpectedTime : null,
            ExpectedSpaceComplexity = IsCoding ? ExpectedSpace : null,
            StarterCode = IsCoding ? StarterCode : null
        };
        CloseRequested?.Invoke(true);
    }
}
