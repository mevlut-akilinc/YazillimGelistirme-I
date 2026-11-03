namespace SinavSistemi.Shared.Models;

public enum QuestionType
{
    CoktanSecmeli,
    Klasik,
    Kodlama
}

public enum Complexity
{
    O1,
    OLogN,
    ON,
    ONLogN,
    ON2,
    ON3,
    O2N
}

public class Question
{
    public int Id { get; set; }
    public QuestionType Type { get; set; }
    public string Category { get; set; } = "";
    public string Text { get; set; } = "";
    public int DefaultPoints { get; set; } = 10;

    // Çoktan seçmeli
    public List<QuestionOption> Options { get; set; } = new();

    // Klasik: eğitmenin beklediği anahtar cevap (otomatik puanlamada karşılaştırılır)
    public string? ExpectedAnswer { get; set; }

    // Kodlama (final)
    public List<TestCase> TestCases { get; set; } = new();
    public Complexity? ExpectedTimeComplexity { get; set; }
    public Complexity? ExpectedSpaceComplexity { get; set; }
    public string? StarterCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class QuestionOption
{
    public int Id { get; set; }
    public string Label { get; set; } = "";   // A, B, C, D, E
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}

public class TestCase
{
    public int Id { get; set; }
    public string Input { get; set; } = "";
    public string ExpectedOutput { get; set; } = "";
    public bool IsHidden { get; set; }
}
