namespace SinavSistemi.Shared.Models;

public enum ExamStatus
{
    Taslak,
    Yayinda,
    Bitti
}

public class Exam
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int DurationMinutes { get; set; } = 40;
    public ExamStatus Status { get; set; } = ExamStatus.Taslak;
    public DateTime? StartedAt { get; set; }
    public List<ExamQuestion> Questions { get; set; } = new();

    /// <summary>Kodlama puanının ağırlıkları (final). Toplamı 1 olmalı.</summary>
    public double TestCaseWeight { get; set; } = 0.7;
    public double ComplexityWeight { get; set; } = 0.3;

    public DateTime? EndsAt => StartedAt?.AddMinutes(DurationMinutes);
    public int TotalPoints => Questions.Sum(q => q.Points);
}

public class ExamQuestion
{
    public Question Question { get; set; } = new();
    public int Order { get; set; }
    public int Points { get; set; }
}

public class StudentAnswer
{
    public int QuestionId { get; set; }
    public int? SelectedOptionId { get; set; }  // çoktan seçmeli
    public string? Text { get; set; }           // klasik
    public string? Code { get; set; }           // kodlama
}

public class Submission
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public User Student { get; set; } = new();
    public List<StudentAnswer> Answers { get; set; } = new();
    public DateTime SubmittedAt { get; set; }
    public double Score { get; set; }
    public int CorrectCount { get; set; }
}
