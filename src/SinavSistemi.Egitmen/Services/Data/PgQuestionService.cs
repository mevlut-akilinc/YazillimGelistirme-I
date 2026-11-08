using System.Data;
using Dapper;
using Npgsql;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Egitmen.Services.Data;

/// <summary>
/// Soru bankası (PostgreSQL). Silme fiziksel değil: questions.is_deleted = true (soft delete),
/// böylece geçmiş sınavlar ve cevaplar soruya bağlı kalır.
/// </summary>
public sealed class PgQuestionService(NpgsqlDataSource dataSource, IAuthService auth) : IQuestionService
{
    public async Task<IReadOnlyList<Question>> GetAllAsync()
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return await QuestionData.GetActiveAsync(conn);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync()
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        return (await conn.QueryAsync<string>("SELECT name FROM categories ORDER BY name")).ToList();
    }

    /// <summary>Tek transaction: kategori (yoksa oluştur) → soru → şıklar → test case'ler. Üretilen id'ler nesnelere yazılır.</summary>
    public async Task<Question> AddAsync(Question question)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        int categoryId = await GetOrCreateCategoryAsync(conn, tx, question.Category);

        var created = await conn.QuerySingleAsync<(int Id, DateTime CreatedAt)>(
            """
            INSERT INTO questions (category_id, type, body, default_points, expected_answer, starter_code,
                                   expected_time_complexity, expected_space_complexity, created_by)
            VALUES (@categoryId, @type, @text, @defaultPoints, @expectedAnswer, @starterCode,
                    @timeComplexity, @spaceComplexity, @createdBy)
            RETURNING id, created_at
            """,
            new
            {
                categoryId,
                type = question.Type.ToString(),
                text = question.Text,
                defaultPoints = question.DefaultPoints,
                expectedAnswer = question.ExpectedAnswer,
                starterCode = question.StarterCode,
                timeComplexity = question.ExpectedTimeComplexity?.ToString(),
                spaceComplexity = question.ExpectedSpaceComplexity?.ToString(),
                createdBy = auth.CurrentSession?.User.Id
            }, tx);

        question.Id = created.Id;
        question.CreatedAt = created.CreatedAt;

        foreach (var o in question.Options) o.Id = await InsertOptionAsync(conn, tx, question.Id, o);
        foreach (var t in question.TestCases) t.Id = await InsertTestCaseAsync(conn, tx, question.Id, t);

        await tx.CommitAsync();
        return question;
    }

    /// <summary>
    /// Tek transaction. Şıklar ve test case'ler id bazlı güncellenir:
    /// Id'si olan → UPDATE, Id == 0 → INSERT, listeden çıkarılan → DELETE.
    /// "Hepsini sil, yeniden ekle" yapılmaz: answers.selected_option_id şıklara bağlı, geçmiş cevaplar kopardı.
    /// </summary>
    public async Task UpdateAsync(Question question)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        int categoryId = await GetOrCreateCategoryAsync(conn, tx, question.Category);

        int affected = await conn.ExecuteAsync(
            """
            UPDATE questions
            SET category_id = @categoryId, type = @type, body = @text, default_points = @defaultPoints,
                expected_answer = @expectedAnswer, starter_code = @starterCode,
                expected_time_complexity = @timeComplexity, expected_space_complexity = @spaceComplexity
            WHERE id = @id AND NOT is_deleted
            """,
            new
            {
                id = question.Id,
                categoryId,
                type = question.Type.ToString(),
                text = question.Text,
                defaultPoints = question.DefaultPoints,
                expectedAnswer = question.ExpectedAnswer,
                starterCode = question.StarterCode,
                timeComplexity = question.ExpectedTimeComplexity?.ToString(),
                spaceComplexity = question.ExpectedSpaceComplexity?.ToString()
            }, tx);
        if (affected == 0)
            throw new InvalidOperationException($"Soru #{question.Id} bulunamadı veya silinmiş.");

        // ---- şıklar
        // 1) Listede artık olmayanları sil. (Etiketler kayabilir: C → B. UNIQUE(question_id, label)
        //    DEFERRABLE olduğu için kontrol COMMIT'te yapılır, ara adımlarda çakışma olmaz.)
        await conn.ExecuteAsync(
            "DELETE FROM question_options WHERE question_id = @questionId AND NOT (id = ANY(@keepIds))",
            new { questionId = question.Id, keepIds = question.Options.Where(o => o.Id != 0).Select(o => o.Id).ToArray() }, tx);

        // 2) Var olanları güncelle, yenileri ekle
        foreach (var o in question.Options)
        {
            if (o.Id == 0)
            {
                o.Id = await InsertOptionAsync(conn, tx, question.Id, o);
                continue;
            }
            int n = await conn.ExecuteAsync(
                "UPDATE question_options SET label = @label, body = @text, is_correct = @isCorrect WHERE id = @id AND question_id = @questionId",
                new { id = o.Id, questionId = question.Id, label = o.Label, text = o.Text, isCorrect = o.IsCorrect }, tx);
            if (n == 0) throw new InvalidOperationException($"Şık #{o.Id} bu soruya ait değil.");
        }

        // ---- test case'ler (aynı mantık)
        await conn.ExecuteAsync(
            "DELETE FROM test_cases WHERE question_id = @questionId AND NOT (id = ANY(@keepIds))",
            new { questionId = question.Id, keepIds = question.TestCases.Where(t => t.Id != 0).Select(t => t.Id).ToArray() }, tx);

        foreach (var t in question.TestCases)
        {
            if (t.Id == 0)
            {
                t.Id = await InsertTestCaseAsync(conn, tx, question.Id, t);
                continue;
            }
            int n = await conn.ExecuteAsync(
                "UPDATE test_cases SET input = @input, expected_output = @expectedOutput, is_hidden = @isHidden WHERE id = @id AND question_id = @questionId",
                new { id = t.Id, questionId = question.Id, input = t.Input, expectedOutput = t.ExpectedOutput, isHidden = t.IsHidden }, tx);
            if (n == 0) throw new InvalidOperationException($"Test case #{t.Id} bu soruya ait değil.");
        }

        await tx.CommitAsync();
    }

    /// <summary>Soft delete: satır tabloda kalır, listelerde görünmez.</summary>
    public async Task DeleteAsync(int questionId)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("UPDATE questions SET is_deleted = true WHERE id = @questionId", new { questionId });
    }

    // ------------------------------------------------------------------ yardımcılar

    /// <summary>Kategori yoksa oluşturur, id'sini döndürür. Aynı anda iki ekleme olsa da ON CONFLICT çakışmayı önler.</summary>
    private static async Task<int> GetOrCreateCategoryAsync(NpgsqlConnection conn, IDbTransaction tx, string name)
    {
        await conn.ExecuteAsync(
            "INSERT INTO categories (name) VALUES (@name) ON CONFLICT (name) DO NOTHING", new { name }, tx);
        return await conn.QuerySingleAsync<int>("SELECT id FROM categories WHERE name = @name", new { name }, tx);
    }

    private static Task<int> InsertOptionAsync(NpgsqlConnection conn, IDbTransaction tx, int questionId, QuestionOption o)
        => conn.QuerySingleAsync<int>(
            "INSERT INTO question_options (question_id, label, body, is_correct) VALUES (@questionId, @label, @text, @isCorrect) RETURNING id",
            new { questionId, label = o.Label, text = o.Text, isCorrect = o.IsCorrect }, tx);

    private static Task<int> InsertTestCaseAsync(NpgsqlConnection conn, IDbTransaction tx, int questionId, TestCase t)
        => conn.QuerySingleAsync<int>(
            "INSERT INTO test_cases (question_id, input, expected_output, is_hidden) VALUES (@questionId, @input, @expectedOutput, @isHidden) RETURNING id",
            new { questionId, input = t.Input, expectedOutput = t.ExpectedOutput, isHidden = t.IsHidden }, tx);
}
