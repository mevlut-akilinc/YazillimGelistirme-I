using SinavSistemi.Shared.Models;
using SinavSistemi.Shared.Protocol;

namespace SinavSistemi.Ogrenci.Services;

public record LoginResult(Session? Session, string? Error);

/// <summary>
/// Öğrenci bilgisayarındaki istemci. Veritabanını bilmez; her şey eğitmen bilgisayarındaki sunucu üzerinden.
/// Gerçek implementasyon: TcpClient + Shared.Protocol.MessageStream.
///   LoginAsync  → bağlan, Login gönder, LoginOk/LoginFail bekle, token'ı sakla, okuma döngüsünü başlat
///   Okuma döngüsü: Exam → ExamReceived, ExamEnded → ExamEnded, bağlantı kapanırsa → ConnectionLost
/// Olaylar arka plan thread'inden geldiği için Application.Current.Dispatcher.Invoke ile tetiklenmeli.
/// </summary>
public interface IExamClient
{
    bool IsConnected { get; }

    Task<LoginResult> LoginAsync(string serverIp, int port, string studentNumber, string password);
    Task DisconnectAsync();
    Task SendProgressAsync(int examId, int answeredCount);
    Task<SubmitResultPayload> SubmitAsync(int examId, List<StudentAnswer> answers);

    event EventHandler<Exam>? ExamReceived;
    event EventHandler? ExamEnded;
    event EventHandler? ConnectionLost;
}
