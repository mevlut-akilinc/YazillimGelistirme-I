using SinavSistemi.Shared.Core;

namespace SinavSistemi.Shared.Models;

public enum ClientState
{
    Bagli,
    SinavDa,
    TeslimEtti,
    BaglantiKoptu
}

/// <summary>Eğitmen ekranında canlı listelenen öğrenci bağlantısı.</summary>
public class ConnectedStudent : ObservableObject
{
    private ClientState _state;
    private int _answeredCount;

    public string StudentNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public DateTime ConnectedAt { get; set; }

    public ClientState State { get => _state; set => SetProperty(ref _state, value); }
    public int AnsweredCount { get => _answeredCount; set => SetProperty(ref _answeredCount, value); }
}

public enum LogLevel
{
    Bilgi,
    Uyari,
    Hata
}

public class LogEntry
{
    public DateTime Time { get; set; }
    public LogLevel Level { get; set; }
    public string Source { get; set; } = "";     // ör. 192.168.1.23 / 210201045
    public string Message { get; set; } = "";
}

/// <summary>Sonuç tablosundaki bir satır.</summary>
public class ResultRow
{
    public string StudentNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int CorrectCount { get; set; }
    public int TotalQuestions { get; set; }
    public double Score { get; set; }
    public DateTime SubmittedAt { get; set; }
    // Final: kodlama sonuçları
    public string? TimeComplexity { get; set; }
    public string? SpaceComplexity { get; set; }
}

/// <summary>Kod derleme/test sonucu (final).</summary>
public class CodeRunResult
{
    public bool Compiled { get; set; }
    public string CompilerOutput { get; set; } = "";
    public int PassedTests { get; set; }
    public int TotalTests { get; set; }
    public Complexity? MeasuredTime { get; set; }
    public Complexity? MeasuredSpace { get; set; }
}
