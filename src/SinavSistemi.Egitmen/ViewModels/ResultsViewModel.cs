using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using SinavSistemi.Shared.Core;
using SinavSistemi.Shared.Models;
using SinavSistemi.Egitmen.Services;

namespace SinavSistemi.Egitmen.ViewModels;

/// <summary>Öğrenci bazında puanlar + sınıf istatistikleri.</summary>
public class ResultsViewModel : ObservableObject, IPageLifecycle
{
    private Exam? _selectedExam;
    private ResultRow? _selectedRow;

    public ResultsViewModel()
    {
        ExportCommand = new RelayCommand(Export, () => Rows.Count > 0);
        _ = OnNavigatedToAsync();
    }

    public ObservableCollection<Exam> Exams { get; } = new();
    public ObservableCollection<ResultRow> Rows { get; } = new();

    public Exam? SelectedExam
    {
        get => _selectedExam;
        set { if (SetProperty(ref _selectedExam, value)) _ = LoadAsync(); }
    }

    public ResultRow? SelectedRow { get => _selectedRow; set => SetProperty(ref _selectedRow, value); }

    public int Participants => Rows.Count;
    public string Average => Rows.Count == 0 ? "—" : Rows.Average(r => r.Score).ToString("0.0");
    public string Highest => Rows.Count == 0 ? "—" : Rows.Max(r => r.Score).ToString("0.0");
    public string Lowest => Rows.Count == 0 ? "—" : Rows.Min(r => r.Score).ToString("0.0");
    public string AvgCorrect => Rows.Count == 0 ? "—" : $"{Rows.Average(r => r.CorrectCount):0.0} / {Rows[0].TotalQuestions}";

    public ICommand ExportCommand { get; }

    public async Task OnNavigatedToAsync()
    {
        var id = SelectedExam?.Id;
        Exams.Clear();
        foreach (var e in await ServiceLocator.Exams.GetExamsAsync()) Exams.Add(e);
        SelectedExam = Exams.FirstOrDefault(e => e.Id == id) ?? Exams.FirstOrDefault();
    }

    private async Task LoadAsync()
    {
        Rows.Clear();
        if (SelectedExam is not null)
            foreach (var r in (await ServiceLocator.Exams.GetResultsAsync(SelectedExam.Id)).OrderByDescending(r => r.Score))
                Rows.Add(r);

        OnPropertyChanged(nameof(Participants));
        OnPropertyChanged(nameof(Average));
        OnPropertyChanged(nameof(Highest));
        OnPropertyChanged(nameof(Lowest));
        OnPropertyChanged(nameof(AvgCorrect));
    }

    private void Export()
    {
        var path = Dialogs.SaveFile("CSV dosyası (*.csv)|*.csv", $"sonuclar_{SelectedExam?.Id}.csv");
        if (path is null) return;

        var sb = new StringBuilder();
        sb.AppendLine("Öğrenci No;Ad Soyad;Sınıf;Doğru;Toplam Soru;Puan;Teslim Zamanı;Zaman K.;Yer K.");
        foreach (var r in Rows)
            sb.AppendLine($"{r.StudentNumber};{r.FullName};{r.ClassName};{r.CorrectCount};{r.TotalQuestions};{r.Score:0.0};{r.SubmittedAt:dd.MM.yyyy HH:mm:ss};{r.TimeComplexity};{r.SpaceComplexity}");
        sb.AppendLine();
        sb.AppendLine($"Sınıf ortalaması;{Average}");

        // Excel'in Türkçe karakterleri doğru açması için UTF-8 BOM
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        Dialogs.Info("Rapor kaydedildi:\n" + path);
    }
}
