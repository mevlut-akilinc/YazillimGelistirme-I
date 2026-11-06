using System.Text.RegularExpressions;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Egitmen.Grading;

/// <summary>Tek bir sorunun puanlama sonucu.</summary>
public record QuestionGrade(
    int QuestionId,
    QuestionType Type,
    int MaxPoints,
    int EarnedPoints,
    bool IsCorrect,
    bool IsPending);

/// <summary>Bir öğrencinin sınav sonucu.</summary>
public record GradeResult(
    int EarnedPoints,
    int MaxPoints,
    int CorrectCount,
    int GradedCount,
    IReadOnlyList<QuestionGrade> Details)
{
    /// <summary>100 üzerinden puan.</summary>
    public double Percentage => MaxPoints == 0 ? 0 : Math.Round(EarnedPoints * 100.0 / MaxPoints, 1);
}

/// <summary>
/// Çoktan seçmeli ve klasik soruları cevap anahtarına göre puanlar (vize isteri 7).
/// Kodlama soruları finalde değerlendirileceği için "beklemede" işaretlenir ve toplama katılmaz.
/// </summary>
public static class Grader
{
    /// <summary>Klasik soruda birden fazla kabul edilen cevabı ayırır. Ör: "free;free()"</summary>
    public const char AnswerSeparator = ';';

    public static GradeResult Grade(Exam exam, IReadOnlyList<StudentAnswer> answers)
    {
        // Cevaplar ağdan geldiği için aynı soruya birden fazla kayıt olabilir; sonuncusu geçerli sayılır.
        // (ToDictionary bu durumda ArgumentException fırlatırdı.)
        var byQuestion = answers
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.Last());
        var details = new List<QuestionGrade>();

        foreach (var eq in exam.Questions.OrderBy(q => q.Order))
        {
            var q = eq.Question;
            byQuestion.TryGetValue(q.Id, out var answer);

            if (q.Type == QuestionType.Kodlama)
            {
                details.Add(new QuestionGrade(q.Id, q.Type, eq.Points, 0, false, IsPending: true));
                continue;
            }

            bool correct = q.Type switch
            {
                QuestionType.CoktanSecmeli => IsMultipleChoiceCorrect(q, answer),
                QuestionType.Klasik => IsClassicCorrect(q, answer),
                _ => false
            };

            details.Add(new QuestionGrade(q.Id, q.Type, eq.Points, correct ? eq.Points : 0, correct, IsPending: false));
        }

        var graded = details.Where(d => !d.IsPending).ToList();
        return new GradeResult(
            EarnedPoints: graded.Sum(d => d.EarnedPoints),
            MaxPoints: graded.Sum(d => d.MaxPoints),
            CorrectCount: graded.Count(d => d.IsCorrect),
            GradedCount: graded.Count,
            Details: details);
    }

    /// <summary>Seçilen şık, cevap anahtarındaki doğru şık mı?</summary>
    private static bool IsMultipleChoiceCorrect(Question q, StudentAnswer? a)
    {
        if (a?.SelectedOptionId is not int selected) return false;
        var correctOption = q.Options.FirstOrDefault(o => o.IsCorrect);
        return correctOption is not null && correctOption.Id == selected;
    }

    /// <summary>Öğrencinin cevabı, anahtardaki kabul edilen cevaplardan biriyle eşleşiyor mu?</summary>
    private static bool IsClassicCorrect(Question q, StudentAnswer? a)
    {
        if (string.IsNullOrWhiteSpace(a?.Text) || string.IsNullOrWhiteSpace(q.ExpectedAnswer)) return false;

        var given = Normalize(a.Text);
        return q.ExpectedAnswer
            .Split(AnswerSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(key => Normalize(key) == given);
    }

    /// <summary>Baştaki/sondaki boşluk, çoklu boşluk ve büyük/küçük harf farkını yok sayar.</summary>
    public static string Normalize(string s)
        => Regex.Replace(s.Trim(), @"\s+", " ").ToLowerInvariant();
}