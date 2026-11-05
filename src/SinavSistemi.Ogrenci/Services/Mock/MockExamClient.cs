using System.Windows;
using SinavSistemi.Shared.Mock;
using SinavSistemi.Shared.Models;
using SinavSistemi.Shared.Protocol;

namespace SinavSistemi.Ogrenci.Services.Mock;

/// <summary>Sunucu yokken arayüzü denemek için. Giriş: herhangi bir numara / 1234.</summary>
internal class MockExamClient : IExamClient
{
    private Exam? _exam;

    public bool IsConnected { get; private set; }
    public event EventHandler<Exam>? ExamReceived;
    public event EventHandler? ExamEnded;
    public event EventHandler? ConnectionLost;

    public async Task<LoginResult> LoginAsync(string serverIp, int port, string studentNumber, string password)
    {
        await Task.Delay(500);
        if (password != "1234") return new LoginResult(null, "Öğrenci numarası veya şifre hatalı.");

        IsConnected = true;
        var session = new Session
        {
            Token = Guid.NewGuid().ToString("N"),
            User = new User { Username = studentNumber, StudentNumber = studentNumber, FullName = "Demo Öğrenci", ClassName = "1-A", Role = UserRole.Ogrenci }
        };

        // 2 sn sonra eğitmen sınavı dağıtmış gibi davran (cevaplar ayıklanmış halde)
        _ = Task.Delay(2000).ContinueWith(_ => Application.Current.Dispatcher.Invoke(() =>
        {
            _exam = Protocol.StripForStudent(MockData.SampleExam());
            ExamReceived?.Invoke(this, _exam);
        }));
        return new LoginResult(session, null);
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendProgressAsync(int examId, int answeredCount) => Task.CompletedTask;

    public async Task<SubmitResultPayload> SubmitAsync(int examId, List<StudentAnswer> answers)
    {
        await Task.Delay(400);
        bool late = _exam?.EndsAt is DateTime end && DateTime.Now > end.AddSeconds(5);
        return late
            ? new SubmitResultPayload(false, "Sınav süresi dolmuş.", DateTime.Now)
            : new SubmitResultPayload(true, null, DateTime.Now);
    }

    // Olayları test etmek için (ör. geçici bir butondan çağrılabilir)
    internal void SimulateExamEnded() => ExamEnded?.Invoke(this, EventArgs.Empty);
    internal void SimulateConnectionLost() => ConnectionLost?.Invoke(this, EventArgs.Empty);
}
