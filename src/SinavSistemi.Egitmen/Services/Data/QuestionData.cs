using Dapper;
using Npgsql;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Egitmen.Services.Data;

/// <summary>
/// Soruları şıkları ve test case'leriyle birlikte okur. N+1 yok: tek komutta üç SELECT
/// (QueryMultipleAsync, tek gidiş-dönüş) ve sonuçlar bellekte question_id'ye göre birleştirilir.
/// PgQuestionService (soru bankası) ve PgExamService (sınavın soruları) birlikte kullanır.
/// </summary>
internal static class QuestionData
{
    // Enum kolonları önce string olarak okunur, sonra Enum.Parse ile çevrilir (Dapper'ın gizli dönüşümüne güvenmeyiz).
    // Adı modelden farklı kolonlar AS ile eşlenir: body → Text, categories.name → Category.

    private const string ActiveSql = """
        SELECT q.id, q.type, c.name AS category, q.body AS text, q.default_points, q.expected_answer,
               q.starter_code, q.expected_time_complexity, q.expected_space_complexity, q.created_at
        FROM questions q
        JOIN categories c ON c.id = q.category_id
        WHERE NOT q.is_deleted
        ORDER BY q.id;

        SELECT o.id, o.question_id, o.label, o.body AS text, o.is_correct
        FROM question_options o
        JOIN questions q ON q.id = o.question_id
        WHERE NOT q.is_deleted
        ORDER BY o.question_id, o.label;

        SELECT t.id, t.question_id, t.input, t.expected_output, t.is_hidden
        FROM test_cases t
        JOIN questions q ON q.id = t.question_id
        WHERE NOT q.is_deleted
        ORDER BY t.question_id, t.id;
        """;

    /// <summary>Verilen id'ler — silinmiş (is_deleted) sorular DAHİL (geçmiş sınavların parçası).</summary>
    private const string ByIdsSql = """
        SELECT q.id, q.type, c.name AS category, q.body AS text, q.default_points, q.expected_answer,
               q.starter_code, q.expected_time_complexity, q.expected_space_complexity, q.created_at
        FROM questions q
        JOIN categories c ON c.id = q.category_id
        WHERE q.id = ANY(@ids)
        ORDER BY q.id;

        SELECT id, question_id, label, body AS text, is_correct
        FROM question_options
        WHERE question_id = ANY(@ids)
        ORDER BY question_id, label;

        SELECT id, question_id, input, expected_output, is_hidden
        FROM test_cases
        WHERE question_id = ANY(@ids)
        ORDER BY question_id, id;
        """;

    /// <summary>Soru bankası: silinmemiş tüm sorular.</summary>
    public static async Task<List<Question>> GetActiveAsync(NpgsqlConnection conn)
    {
        await using var grid = await conn.QueryMultipleAsync(ActiveSql);
        return await ReadAsync(grid);
    }

    /// <summary>Id'si verilen sorular (silinmişler dahil). Anahtar: soru id.</summary>
    public static async Task<Dictionary<int, Question>> GetByIdsAsync(NpgsqlConnection conn, int[] ids)
    {
        if (ids.Length == 0) return new();
        // Npgsql + Dapper: int[] tek bir PostgreSQL dizi parametresi olarak gider (= ANY(@ids))
        await using var grid = await conn.QueryMultipleAsync(ByIdsSql, new { ids });
        return (await ReadAsync(grid)).ToDictionary(q => q.Id);
    }

    private static async Task<List<Question>> ReadAsync(SqlMapper.GridReader grid)
    {
        var questions = (await grid.ReadAsync<QuestionRow>()).Select(r => r.ToQuestion()).ToList();
        var options = (await grid.ReadAsync<OptionRow>()).ToLookup(o => o.QuestionId);
        var tests = (await grid.ReadAsync<TestCaseRow>()).ToLookup(t => t.QuestionId);

        foreach (var q in questions)
        {
            q.Options = options[q.Id].Select(o => o.ToOption()).ToList();
            q.TestCases = tests[q.Id].Select(t => t.ToTestCase()).ToList();
        }
        return questions;
    }

    // ------------------------------------------------------------------ satır sınıfları

    private sealed class QuestionRow
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public string Category { get; set; } = "";
        public string Text { get; set; } = "";
        public int DefaultPoints { get; set; }
        public string? ExpectedAnswer { get; set; }
        public string? StarterCode { get; set; }
        public string? ExpectedTimeComplexity { get; set; }
        public string? ExpectedSpaceComplexity { get; set; }
        public DateTime CreatedAt { get; set; }

        public Question ToQuestion() => new()
        {
            Id = Id,
            Type = Enum.Parse<QuestionType>(Type),
            Category = Category,
            Text = Text,
            DefaultPoints = DefaultPoints,
            ExpectedAnswer = ExpectedAnswer,
            StarterCode = StarterCode,
            ExpectedTimeComplexity = ParseComplexity(ExpectedTimeComplexity),
            ExpectedSpaceComplexity = ParseComplexity(ExpectedSpaceComplexity),
            CreatedAt = CreatedAt
        };
    }

    private sealed class OptionRow
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public string Label { get; set; } = "";
        public string Text { get; set; } = "";
        public bool IsCorrect { get; set; }

        public QuestionOption ToOption() => new() { Id = Id, Label = Label, Text = Text, IsCorrect = IsCorrect };
    }

    private sealed class TestCaseRow
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public string Input { get; set; } = "";
        public string ExpectedOutput { get; set; } = "";
        public bool IsHidden { get; set; }

        public TestCase ToTestCase() => new() { Id = Id, Input = Input, ExpectedOutput = ExpectedOutput, IsHidden = IsHidden };
    }

    /// <summary>NULL → null, 'ONLogN' → Complexity.ONLogN.</summary>
    public static Complexity? ParseComplexity(string? value)
        => value is null ? null : Enum.Parse<Complexity>(value);
}
