using System.Windows;
using System.Windows.Threading;
using SinavSistemi.Shared.Mock;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Egitmen.Services.Mock;

internal class MockAuthService : IAuthService
{
    public Session? CurrentSession { get; private set; }

    public async Task<Session?> LoginAsync(string username, string password)
    {
        await Task.Delay(400); // ağ gecikmesi taklidi
        // Demo hesap: egitmen / 1234
        if (username != "egitmen" || password != "1234") return null;

        CurrentSession = new Session
        {
            Token = Guid.NewGuid().ToString("N"),
            User = new User
            {
                Id = 1,
                Username = username,
                Role = UserRole.Egitmen,
                FullName = "Arş. Gör. Demo Eğitmen"
            }
        };
        return CurrentSession;
    }

    public Task LogoutAsync()
    {
        CurrentSession = null;
        return Task.CompletedTask;
    }
}

internal class MockQuestionService : IQuestionService
{
    private int _nextId = 100;

    public Task<IReadOnlyList<Question>> GetAllAsync()
        => Task.FromResult<IReadOnlyList<Question>>(MockData.Questions.ToList());

    public Task<IReadOnlyList<string>> GetCategoriesAsync()
        => Task.FromResult<IReadOnlyList<string>>(MockData.Categories.ToList());

    public Task<Question> AddAsync(Question question)
    {
        question.Id = _nextId++;
        MockData.Questions.Add(question);
        if (!MockData.Categories.Contains(question.Category)) MockData.Categories.Add(question.Category);
        return Task.FromResult(question);
    }

    public Task UpdateAsync(Question question)
    {
        var i = MockData.Questions.FindIndex(q => q.Id == question.Id);
        if (i >= 0) MockData.Questions[i] = question;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int questionId)
    {
        MockData.Questions.RemoveAll(q => q.Id == questionId);
        return Task.CompletedTask;
    }
}

internal class MockExamService : IExamService
{
    private readonly List<Exam> _exams = new() { MockData.SampleExam() };

    public Task<IReadOnlyList<Exam>> GetExamsAsync() => Task.FromResult<IReadOnlyList<Exam>>(_exams.ToList());

    public Task<Exam> SaveAsync(Exam exam)
    {
        if (exam.Id == 0) { exam.Id = _exams.Count + 1; _exams.Add(exam); }
        return Task.FromResult(exam);
    }

    public Task<IReadOnlyList<ResultRow>> GetResultsAsync(int examId)
    {
        var rnd = new Random(examId);
        var rows = MockData.Students.Select(s =>
        {
            int correct = rnd.Next(2, 7);
            return new ResultRow
            {
                StudentNumber = s.No, FullName = s.Name, ClassName = "1-A",
                CorrectCount = correct, TotalQuestions = 6,
                Score = Math.Round(correct / 6.0 * 100, 1),
                SubmittedAt = DateTime.Now.AddMinutes(-rnd.Next(1, 15)),
                TimeComplexity = rnd.Next(2) == 0 ? "O(n log n)" : "O(n²)",
                SpaceComplexity = "O(n)"
            };
        }).ToList();
        return Task.FromResult<IReadOnlyList<ResultRow>>(rows);
    }

    public Task<IReadOnlyList<LogEntry>> GetLogsAsync(int? examId = null)
    {
        var now = DateTime.Now;
        var logs = new List<LogEntry>
        {
            new() { Time = now.AddMinutes(-30), Level = LogLevel.Bilgi, Source = "Sunucu", Message = "Sunucu 0.0.0.0:5050 üzerinde başlatıldı" },
            new() { Time = now.AddMinutes(-28), Level = LogLevel.Bilgi, Source = "192.168.1.21", Message = "230201001 bağlandı" },
            new() { Time = now.AddMinutes(-27), Level = LogLevel.Bilgi, Source = "Sunucu", Message = "Sınav #1 6 öğrenciye gönderildi" },
            new() { Time = now.AddMinutes(-12), Level = LogLevel.Uyari, Source = "192.168.1.24", Message = "230201033 bağlantısı koptu, yeniden bağlandı" },
            new() { Time = now.AddMinutes(-8), Level = LogLevel.Bilgi, Source = "192.168.1.21", Message = "230201001 sınavı teslim etti" },
            new() { Time = now.AddMinutes(-1), Level = LogLevel.Hata, Source = "192.168.1.26", Message = "230201052 süre bittikten sonra gönderim denedi — reddedildi" },
        };
        return Task.FromResult<IReadOnlyList<LogEntry>>(logs);
    }
}

/// <summary>Öğrenci bağlanmasını ve ilerlemesini zamanlayıcıyla taklit eder.</summary>
internal class MockExamServer : IExamServer
{
    private readonly List<ConnectedStudent> _students = new();
    private DispatcherTimer? _timer;
    private readonly Random _rnd = new();

    public bool IsRunning { get; private set; }
    public string LocalAddress => "192.168.1.10";
    public int Port { get; private set; }

    public event EventHandler<ConnectedStudent>? StudentConnected;
    public event EventHandler<ConnectedStudent>? StudentChanged;
    public event EventHandler<LogEntry>? LogWritten;

    public Task StartAsync(int port)
    {
        Port = port;
        IsRunning = true;
        Log(LogLevel.Bilgi, "Sunucu", $"Sunucu {LocalAddress}:{port} üzerinde dinliyor");

        int i = 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += (_, _) =>
        {
            if (i < MockData.Students.Count)
            {
                var s = MockData.Students[i++];
                var cs = new ConnectedStudent
                {
                    StudentNumber = s.No, FullName = s.Name, IpAddress = s.Ip,
                    ConnectedAt = DateTime.Now, State = ClientState.Bagli
                };
                _students.Add(cs);
                StudentConnected?.Invoke(this, cs);
                Log(LogLevel.Bilgi, s.Ip, $"{s.No} bağlandı");
            }
            else
            {
                // sınavdaki öğrencilerin ilerlemesi
                foreach (var cs in _students.Where(x => x.State == ClientState.SinavDa))
                {
                    if (_rnd.NextDouble() < 0.3 && cs.AnsweredCount < 6) cs.AnsweredCount++;
                    if (cs.AnsweredCount == 6 && _rnd.NextDouble() < 0.2)
                    {
                        cs.State = ClientState.TeslimEtti;
                        Log(LogLevel.Bilgi, cs.IpAddress, $"{cs.StudentNumber} sınavı teslim etti");
                    }
                    StudentChanged?.Invoke(this, cs);
                }
            }
        };
        _timer.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _timer?.Stop();
        IsRunning = false;
        _students.Clear();
        Log(LogLevel.Bilgi, "Sunucu", "Sunucu durduruldu");
        return Task.CompletedTask;
    }

    public Task BroadcastExamAsync(Exam exam)
    {
        foreach (var s in _students.Where(x => x.State == ClientState.Bagli))
        {
            s.State = ClientState.SinavDa;
            StudentChanged?.Invoke(this, s);
        }
        Log(LogLevel.Bilgi, "Sunucu", $"\"{exam.Title}\" {_students.Count} öğrenciye gönderildi ({exam.DurationMinutes} dk)");
        return Task.CompletedTask;
    }

    public Task EndExamAsync(int examId)
    {
        foreach (var s in _students.Where(x => x.State == ClientState.SinavDa))
        {
            s.State = ClientState.TeslimEtti;
            StudentChanged?.Invoke(this, s);
        }
        Log(LogLevel.Uyari, "Sunucu", "Sınav eğitmen tarafından sonlandırıldı, yeni gönderim kabul edilmeyecek");
        return Task.CompletedTask;
    }

    private void Log(LogLevel level, string source, string msg)
        => LogWritten?.Invoke(this, new LogEntry { Time = DateTime.Now, Level = level, Source = source, Message = msg });
}
