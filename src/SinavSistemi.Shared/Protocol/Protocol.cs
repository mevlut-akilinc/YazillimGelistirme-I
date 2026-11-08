using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Shared.Protocol;

// Egitmen.exe (sunucu) ile Ogrenci.exe (istemci) arasındaki mesaj sözleşmesi.
// Taşıma: TCP, her mesaj TEK SATIR JSON + '\n'. İki taraf da bu dosyayı kullandığı için format tek yerde tanımlı.
//
//  Öğrenci → Sunucu            Sunucu → Öğrenci
//  ---------------------       ----------------------------
//  Login                       LoginOk / LoginFail
//                              Exam            (dağıtım anında herkese; sınav sürerken girene de)
//  Progress                    —
//  Submit                      SubmitOk / SubmitRejected
//                              ExamEnded       (eğitmen bitirdi / süre doldu)
//  Ping                        Pong

public enum MessageType
{
    Login, LoginOk, LoginFail,
    Exam, ExamEnded,
    Progress,
    Submit, SubmitOk, SubmitRejected,
    Ping, Pong
}

public class Envelope
{
    public MessageType Type { get; set; }
    /// <summary>Login dışındaki tüm öğrenci mesajlarında dolu olmalı.</summary>
    public string? Token { get; set; }
    public JsonElement? Payload { get; set; }

    public T? Read<T>() => Payload is { } p ? p.Deserialize<T>(Protocol.Json) : default;
}

// ---- Mesaj içerikleri ----
public record LoginRequest(string StudentNumber, string Password);
public record LoginOkPayload(string Token, string FullName, string? ClassName);
public record LoginFailPayload(string Reason);
/// <summary>
/// Sınav + kalan süre. Öğrenci kalan süreyi kendi saatinden değil bu değerden hesaplar;
/// böylece iki bilgisayarın saati farklı olsa da süre herkes için aynı biter.
/// </summary>
public record ExamPayload(Exam Exam, int RemainingSeconds);
public record ProgressPayload(int ExamId, int AnsweredCount);
public record SubmitPayload(int ExamId, List<StudentAnswer> Answers);
public record SubmitResultPayload(bool Accepted, string? Reason, DateTime ServerTime);

public static class Protocol
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Süre dolduktan / eğitmen bitirdikten sonra teslimlerin kabul edildiği ek süre.
    /// Süre dolunca öğrenci ekranı cevapları otomatik gönderir; bu gönderimin ağdan gelmesi için pay bırakılır.
    /// </summary>
    public static readonly TimeSpan SubmitGracePeriod = TimeSpan.FromSeconds(15);

    public static string Serialize(MessageType type, object? payload = null, string? token = null)
    {
        var env = new Envelope
        {
            Type = type,
            Token = token,
            Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, Json)
        };
        return JsonSerializer.Serialize(env, Json);
    }

    public static Envelope? Parse(string line)
    {
        try { return JsonSerializer.Deserialize<Envelope>(line, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Sınavı öğrenciye göndermeden önce cevapları ayıklar:
    /// doğru şık, klasik cevap anahtarı, gizli test case'ler ve beklenen karmaşıklık gitmez.
    /// </summary>
    public static Exam StripForStudent(Exam exam) => new()
    {
        Id = exam.Id,
        Title = exam.Title,
        ClassName = exam.ClassName,
        DurationMinutes = exam.DurationMinutes,
        Status = exam.Status,
        StartedAt = exam.StartedAt,
        Questions = exam.Questions.Select(eq => new ExamQuestion
        {
            Order = eq.Order,
            Points = eq.Points,
            Question = new Question
            {
                Id = eq.Question.Id,
                Type = eq.Question.Type,
                Category = eq.Question.Category,
                Text = eq.Question.Text,
                DefaultPoints = eq.Question.DefaultPoints,
                StarterCode = eq.Question.StarterCode,
                Options = eq.Question.Options
                    .Select(o => new QuestionOption { Id = o.Id, Label = o.Label, Text = o.Text })
                    .ToList(),
                TestCases = eq.Question.TestCases
                    .Where(t => !t.IsHidden)
                    .Select(t => new TestCase { Id = t.Id, Input = t.Input, ExpectedOutput = t.ExpectedOutput })
                    .ToList()
            }
        }).ToList()
    };
}

/// <summary>
/// TCP bağlantısı üzerinde satır satır mesaj okuma/yazma. Hem sunucu hem istemci kullanır.
/// Kullanım: var ms = new MessageStream(tcpClient); await ms.SendAsync(MessageType.Login, new LoginRequest(...));
/// </summary>
public sealed class MessageStream : IDisposable
{
    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public MessageStream(TcpClient client)
    {
        _client = client;
        var stream = client.GetStream();
        _reader = new StreamReader(stream, new UTF8Encoding(false));
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    }

    public string RemoteAddress => _client.Client.RemoteEndPoint?.ToString() ?? "?";

    public async Task SendAsync(MessageType type, object? payload = null, string? token = null)
    {
        var line = Protocol.Serialize(type, payload, token);
        await _writeLock.WaitAsync();
        try { await _writer.WriteLineAsync(line); }
        finally { _writeLock.Release(); }
    }

    /// <summary>Bağlantı kapanınca null döner.</summary>
    public async Task<Envelope?> ReadAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var line = await _reader.ReadLineAsync(ct);
            if (line is null) return null;
            var env = Protocol.Parse(line);
            if (env is not null) return env;   // bozuk satırı atla
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
        _client.Dispose();
    }
}
