using System.IO;
using System.Net.Sockets;
using System.Windows;
using SinavSistemi.Shared.Models;
using SinavSistemi.Shared.Protocol;

namespace SinavSistemi.Ogrenci.Services;

/// <summary>
/// Öğrenci bilgisayarındaki TCP istemcisi.
///
///   LoginAsync  → bağlan (5 sn zaman aşımı), Login gönder, ilk cevabı (LoginOk/LoginFail) doğrudan oku,
///                 sonra arka planda okuma döngüsünü başlat.
///   Okuma döngüsü → Exam: ExamReceived, ExamEnded: ExamEnded, SubmitOk/Rejected: bekleyen SubmitAsync'e,
///                   bağlantı kapanırsa: ConnectionLost.
///   SubmitAsync → bağlantı kopmuşsa aynı bilgilerle sessizce yeniden giriş yapar, sonra gönderir.
///
/// Olaylar her zaman WPF arayüz thread'inde tetiklenir.
/// </summary>
public sealed class TcpExamClient : IExamClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(15);

    private TcpClient? _tcp;
    private MessageStream? _stream;
    private CancellationTokenSource? _readCts;
    private string? _token;
    private TaskCompletionSource<SubmitResultPayload>? _pendingSubmit;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    // Yeniden bağlanmak için son giriş bilgileri
    private (string Ip, int Port, string Number, string Password)? _credentials;

    // Açılmış sınav: yeniden bağlanınca sunucu sınavı tekrar gönderir, aynı sınav ikinci kez açılmamalı
    // (açılırsa öğrencinin cevapları sıfırlanır).
    private int? _openedExamId;

    // Giriş biter bitmez gelen sınav, pencere henüz ExamReceived'e abone olmadan kaybolmasın
    private EventHandler<Exam>? _examReceived;
    private Exam? _undeliveredExam;

    private volatile bool _isConnected;
    public bool IsConnected => _isConnected;

    public event EventHandler<Exam>? ExamReceived
    {
        add
        {
            _examReceived += value;
            if (_undeliveredExam is { } exam)
            {
                _undeliveredExam = null;
                Ui(() => _examReceived?.Invoke(this, exam));
            }
        }
        remove => _examReceived -= value;
    }

    public event EventHandler? ExamEnded;
    public event EventHandler? ConnectionLost;

    // ------------------------------------------------------------------ giriş / çıkış

    public async Task<LoginResult> LoginAsync(string serverIp, int port, string studentNumber, string password)
    {
        _openedExamId = null;
        var result = await ConnectAndLoginAsync(serverIp, port, studentNumber, password);
        if (result.Session is not null) _credentials = (serverIp, port, studentNumber, password);
        return result;
    }

    public Task DisconnectAsync()
    {
        _credentials = null;
        _openedExamId = null;
        _undeliveredExam = null;
        CloseConnection();
        return Task.CompletedTask;
    }

    private async Task<LoginResult> ConnectAndLoginAsync(string ip, int port, string number, string password)
    {
        await _connectLock.WaitAsync();
        try
        {
            CloseConnection();

            var tcp = new TcpClient { NoDelay = true };
            try
            {
                using (var cts = new CancellationTokenSource(ConnectTimeout))
                    await tcp.ConnectAsync(ip, port, cts.Token);
            }
            catch (OperationCanceledException)
            {
                tcp.Dispose();
                throw new TimeoutException("Sunucuya bağlanılamadı (zaman aşımı).");
            }
            catch
            {
                tcp.Dispose();
                throw;
            }

            var stream = new MessageStream(tcp);
            try
            {
                await stream.SendAsync(MessageType.Login, new LoginRequest(number, password));

                Envelope? reply;
                using (var cts = new CancellationTokenSource(ResponseTimeout))
                    reply = await stream.ReadAsync(cts.Token);

                if (reply?.Type == MessageType.LoginFail)
                {
                    stream.Dispose();
                    return new LoginResult(null, reply.Read<LoginFailPayload>()?.Reason ?? "Giriş reddedildi.");
                }
                if (reply?.Type != MessageType.LoginOk || reply.Read<LoginOkPayload>() is not { } ok)
                    throw new IOException("Sunucudan beklenmeyen cevap geldi.");

                _tcp = tcp;
                _stream = stream;
                _token = ok.Token;
                _isConnected = true;
                _readCts = new CancellationTokenSource();
                _ = ReadLoopAsync(stream, _readCts.Token);

                return new LoginResult(new Session
                {
                    Token = ok.Token,
                    User = new User
                    {
                        Username = number, StudentNumber = number,
                        FullName = ok.FullName, ClassName = ok.ClassName, Role = UserRole.Ogrenci
                    }
                }, null);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        finally { _connectLock.Release(); }
    }

    private void CloseConnection()
    {
        _isConnected = false;
        _readCts?.Cancel();
        _stream?.Dispose();
        _stream = null;
        _tcp = null;
        _token = null;
        _pendingSubmit?.TrySetException(new IOException("Bağlantı kapatıldı."));
    }

    // ------------------------------------------------------------------ okuma döngüsü

    private async Task ReadLoopAsync(MessageStream stream, CancellationToken ct)
    {
        try
        {
            while (await stream.ReadAsync(ct) is { } env)
            {
                switch (env.Type)
                {
                    case MessageType.Exam when env.Read<ExamPayload>() is { } p:
                        // Kalan süreyi bu bilgisayarın saatine çevir: EndsAt = şimdi + kalan
                        p.Exam.StartedAt = DateTime.Now.AddSeconds(p.RemainingSeconds).AddMinutes(-p.Exam.DurationMinutes);
                        Ui(() => DeliverExam(p.Exam));
                        break;

                    case MessageType.ExamEnded:
                        Ui(() => ExamEnded?.Invoke(this, EventArgs.Empty));
                        break;

                    case MessageType.SubmitOk or MessageType.SubmitRejected:
                        var result = env.Read<SubmitResultPayload>()
                                     ?? new SubmitResultPayload(false, "Sunucudan geçersiz cevap.", DateTime.Now);
                        _pendingSubmit?.TrySetResult(result);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { return; }   // biz kapattık
        catch (Exception) { /* ağ koptu */ }

        if (ct.IsCancellationRequested) return;
        _isConnected = false;
        _pendingSubmit?.TrySetException(new IOException("Eğitmen bilgisayarıyla bağlantı koptu."));
        Ui(() => ConnectionLost?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>UI thread'inde çağrılır.</summary>
    private void DeliverExam(Exam exam)
    {
        if (_openedExamId == exam.Id) return;   // yeniden bağlanma: zaten açık
        _openedExamId = exam.Id;

        if (_examReceived is null) _undeliveredExam = exam;
        else _examReceived.Invoke(this, exam);
    }

    // ------------------------------------------------------------------ gönderimler

    public async Task SendProgressAsync(int examId, int answeredCount)
    {
        if (!_isConnected || _stream is null) return;   // ilerleme bilgisi kaybolabilir, önemli değil
        try { await _stream.SendAsync(MessageType.Progress, new ProgressPayload(examId, answeredCount), _token); }
        catch { /* okuma döngüsü kopmayı bildirecek */ }
    }

    public async Task<SubmitResultPayload> SubmitAsync(int examId, List<StudentAnswer> answers)
    {
        if (!_isConnected)
        {
            if (_credentials is not { } c) throw new IOException("Sunucuya bağlı değilsiniz.");
            var relogin = await ConnectAndLoginAsync(c.Ip, c.Port, c.Number, c.Password);
            if (relogin.Session is null)
                return new SubmitResultPayload(false, relogin.Error, DateTime.Now);
        }

        var stream = _stream ?? throw new IOException("Sunucuya bağlı değilsiniz.");
        var tcs = new TaskCompletionSource<SubmitResultPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingSubmit = tcs;
        try
        {
            await stream.SendAsync(MessageType.Submit, new SubmitPayload(examId, answers), _token);
            return await tcs.Task.WaitAsync(ResponseTimeout);
        }
        catch (TimeoutException)
        {
            throw new IOException("Sunucu cevap vermedi.");
        }
        finally
        {
            Interlocked.CompareExchange(ref _pendingSubmit, null, tcs);
        }
    }

    // ------------------------------------------------------------------ yardımcı

    private static void Ui(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }
}
