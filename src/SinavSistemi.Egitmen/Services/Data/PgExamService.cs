using Dapper;
using Npgsql;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Egitmen.Services.Data;

/// <summary>Sınavlar, teslimler, sonuçlar ve loglar (PostgreSQL + log dosyası).</summary>
public sealed class PgExamService(NpgsqlDataSource dataSource, IAuthService auth, LogFileWriter logFile) : IExamService
{
    /// <summary>logs.source VARCHAR(50): daha uzun kaynak adı INSERT'i patlatmasın.</summary>
    private const int MaxSourceLength = 50;

    // ------------------------------------------------------------------ sınavlar

    /// <summary>
    /// Sınavlar + soruları (order_no sırasıyla), her soru şık ve testleriyle.
    /// Silinmiş (is_deleted) sorular da yüklenir: geçmiş sınavın parçasıdır.
    /// N+1 yok: 1) sınavlar + exam_questions, 2) tüm soruların tam hali (= ANY(@ids)).
    /// </summary>
    public async Task<IReadOnlyList<Exam>> GetExamsAsync()
    {
        await using var conn = await dataSource.OpenConnectionAsync();

        List<ExamRow> examRows;
        List<ExamQuestionRow> links;
        await using (var grid = await conn.QueryMultipleAsync(
            """
            SELECT id, title, COALESCE(class_name, '') AS class_name, duration_minutes, status,
                   test_case_weight, complexity_weight, started_at
            FROM exams
            ORDER BY created_at DESC, id DESC;

            SELECT exam_id, question_id, order_no AS "order", points
            FROM exam_questions
            ORDER BY exam_id, order_no;
            """))
        {
            examRows = (await grid.ReadAsync<ExamRow>()).ToList();
            links = (await grid.ReadAsync<ExamQuestionRow>()).ToList();
        }

        var questions = await QuestionData.GetByIdsAsync(conn, links.Select(l => l.QuestionId).Distinct().ToArray());
        var linksByExam = links.ToLookup(l => l.ExamId);

        return examRows.Select(r =>
        {
            var exam = r.ToExam();
            exam.Questions = linksByExam[r.Id]
                .Select(l => new ExamQuestion { Question = questions[l.QuestionId], Order = l.Order, Points = l.Points })
                .ToList();
            return exam;
        }).ToList();
    }

    /// <summary>
    /// Tek transaction. Id == 0 → INSERT, değilse UPDATE.
    /// exam_questions satırları silinip yeniden eklenir (bu tabloya dışarıdan referans yok).
    /// </summary>
    public async Task<Exam> SaveAsync(Exam exam)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var p = new
        {
            id = exam.Id,
            title = exam.Title,
            className = exam.ClassName,
            durationMinutes = exam.DurationMinutes,
            status = exam.Status.ToString(),
            testCaseWeight = exam.TestCaseWeight,
            complexityWeight = exam.ComplexityWeight,
            startedAt = exam.StartedAt,
            createdBy = auth.CurrentSession?.User.Id
        };

        if (exam.Id == 0)
        {
            // Boş sınıf adı NULL saklanır (NULLIF), okurken COALESCE ile "" olur
            exam.Id = await conn.QuerySingleAsync<int>(
                """
                INSERT INTO exams (title, class_name, duration_minutes, status, test_case_weight,
                                   complexity_weight, started_at, created_by)
                VALUES (@title, NULLIF(@className, ''), @durationMinutes, @status, @testCaseWeight,
                        @complexityWeight, @startedAt, @createdBy)
                RETURNING id
                """, p, tx);
        }
        else
        {
            int affected = await conn.ExecuteAsync(
                """
                UPDATE exams
                SET title = @title, class_name = NULLIF(@className, ''), duration_minutes = @durationMinutes,
                    status = @status, test_case_weight = @testCaseWeight, complexity_weight = @complexityWeight,
                    started_at = @startedAt
                WHERE id = @id
                """, p, tx);
            if (affected == 0) throw new InvalidOperationException($"Sınav #{exam.Id} bulunamadı.");
        }

        await conn.ExecuteAsync("DELETE FROM exam_questions WHERE exam_id = @examId", new { examId = exam.Id }, tx);
        // Dapper, liste verilince komutu her eleman için çalıştırır
        await conn.ExecuteAsync(
            "INSERT INTO exam_questions (exam_id, question_id, order_no, points) VALUES (@examId, @questionId, @order, @points)",
            exam.Questions.Select(q => new { examId = exam.Id, questionId = q.Question.Id, order = q.Order, points = q.Points }),
            tx);

        await tx.CommitAsync();
        return exam;
    }

    public async Task UpdateStatusAsync(int examId, ExamStatus status, DateTime? startedAt)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        // COALESCE: startedAt null gelirse (ör. sınav bitirilirken) mevcut başlama zamanı korunur
        await conn.ExecuteAsync(
            "UPDATE exams SET status = @status, started_at = COALESCE(@startedAt, started_at) WHERE id = @examId",
            new { examId, status = status.ToString(), startedAt });
    }

    // ------------------------------------------------------------------ teslimler ve sonuçlar

    /// <summary>
    /// Tek transaction: submissions (+ RETURNING id) ve her cevap için answers.
    /// Çift teslimde uq_submissions_exam_student ihlali → PostgresException (SqlState 23505) fırlar; çağıran yakalar.
    /// </summary>
    public async Task SaveSubmissionAsync(Submission submission)
    {
        if (submission.Student.Id == 0)
            throw new InvalidOperationException(
                $"Teslim kaydedilemedi: öğrencinin veritabanı id'si yok ({submission.Student.StudentNumber}). " +
                "Öğrenci PgStudentAuthenticator ile doğrulanmış olmalı.");

        await using var conn = await dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        submission.Id = await conn.QuerySingleAsync<int>(
            """
            INSERT INTO submissions (exam_id, student_id, submitted_at, client_ip, earned_points, max_points,
                                     correct_count, graded_count, score)
            VALUES (@examId, @studentId, @submittedAt, @ipAddress, @earnedPoints, @maxPoints,
                    @correctCount, @gradedCount, @score)
            RETURNING id
            """,
            new
            {
                examId = submission.ExamId,
                studentId = submission.Student.Id,
                submittedAt = submission.SubmittedAt,
                ipAddress = submission.IpAddress,
                earnedPoints = submission.EarnedPoints,
                maxPoints = submission.MaxPoints,
                correctCount = submission.CorrectCount,
                gradedCount = submission.GradedCount,
                score = submission.Score
            }, tx);

        await conn.ExecuteAsync(
            """
            INSERT INTO answers (submission_id, question_id, selected_option_id, answer_text, code,
                                 is_correct, earned_points, is_pending)
            VALUES (@submissionId, @questionId, @selectedOptionId, @text, @code, @isCorrect, @earnedPoints, @isPending)
            """,
            submission.Answers.Select(a => new
            {
                submissionId = submission.Id,
                questionId = a.QuestionId,
                selectedOptionId = a.SelectedOptionId,
                text = a.Text,
                code = a.Code,
                isCorrect = a.IsCorrect,
                earnedPoints = a.EarnedPoints,
                isPending = a.IsPending
            }), tx);

        await tx.CommitAsync();
    }

    public async Task<IReadOnlyList<ResultRow>> GetResultsAsync(int examId)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        // Time/SpaceComplexity şimdilik NULL (final: code_results)
        var rows = await conn.QueryAsync<ResultRow>(
            """
            SELECT COALESCE(u.student_number, u.username) AS student_number,
                   u.full_name,
                   COALESCE(u.class_name, '')     AS class_name,
                   COALESCE(s.correct_count, 0)   AS correct_count,
                   COALESCE(s.graded_count, 0)    AS total_questions,
                   COALESCE(s.score, 0)           AS score,
                   s.submitted_at,
                   NULL::varchar                  AS time_complexity,
                   NULL::varchar                  AS space_complexity
            FROM submissions s
            JOIN users u ON u.id = s.student_id
            WHERE s.exam_id = @examId
            ORDER BY s.score DESC NULLS LAST, s.submitted_at
            """, new { examId });
        return rows.ToList();
    }

    public async Task<IReadOnlyCollection<string>> GetSubmittedStudentNumbersAsync(int examId)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        var numbers = await conn.QueryAsync<string>(
            """
            SELECT COALESCE(u.student_number, u.username)
            FROM submissions s
            JOIN users u ON u.id = s.student_id
            WHERE s.exam_id = @examId
            """, new { examId });
        return numbers.ToList();
    }

    // ------------------------------------------------------------------ loglar

    /// <summary>En yeni üstte. examId verilirse sadece o sınavın logları.</summary>
    public async Task<IReadOnlyList<LogEntry>> GetLogsAsync(int? examId = null)
    {
        const string allSql = """
            SELECT created_at AS time, level, source, message
            FROM logs
            ORDER BY created_at DESC, id DESC
            """;
        const string byExamSql = """
            SELECT created_at AS time, level, source, message
            FROM logs
            WHERE exam_id = @examId
            ORDER BY created_at DESC, id DESC
            """;

        await using var conn = await dataSource.OpenConnectionAsync();
        var rows = examId is null
            ? await conn.QueryAsync<LogRow>(allSql)
            : await conn.QueryAsync<LogRow>(byExamSql, new { examId });
        return rows.Select(r => r.ToEntry()).ToList();
    }

    /// <summary>
    /// Önce dosyaya, sonra veritabanına yazar. Biri başarısız olsa da diğeri denenir;
    /// hata varsa sonunda fırlatılır (TcpExamServer yakalar, sunucu durmaz).
    /// </summary>
    public async Task AddLogAsync(LogEntry entry, int? examId)
    {
        Exception? fileError = null;
        try { await logFile.AppendAsync(entry); }
        catch (Exception ex) { fileError = ex; }

        var source = entry.Source.Length > MaxSourceLength ? entry.Source[..MaxSourceLength] : entry.Source;
        await using (var conn = await dataSource.OpenConnectionAsync())
        {
            await conn.ExecuteAsync(
                "INSERT INTO logs (exam_id, created_at, level, source, message) VALUES (@examId, @time, @level, @source, @message)",
                new { examId, time = entry.Time, level = entry.Level.ToString(), source, message = entry.Message });
        }

        if (fileError is not null) throw fileError;
    }

    // ------------------------------------------------------------------ satır sınıfları

    private sealed class ExamRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string ClassName { get; set; } = "";
        public int DurationMinutes { get; set; }
        public string Status { get; set; } = "";
        public double TestCaseWeight { get; set; }
        public double ComplexityWeight { get; set; }
        public DateTime? StartedAt { get; set; }

        public Exam ToExam() => new()
        {
            Id = Id,
            Title = Title,
            ClassName = ClassName,
            DurationMinutes = DurationMinutes,
            Status = Enum.Parse<ExamStatus>(Status),
            TestCaseWeight = TestCaseWeight,
            ComplexityWeight = ComplexityWeight,
            StartedAt = StartedAt
        };
    }

    private sealed class ExamQuestionRow
    {
        public int ExamId { get; set; }
        public int QuestionId { get; set; }
        public int Order { get; set; }
        public int Points { get; set; }
    }

    private sealed class LogRow
    {
        public DateTime Time { get; set; }
        public string Level { get; set; } = "";
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";

        public LogEntry ToEntry() => new()
        {
            Time = Time,
            Level = Enum.Parse<LogLevel>(Level),
            Source = Source,
            Message = Message
        };
    }
}
