using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using SinavSistemi.Egitmen.Grading;
using SinavSistemi.Shared.Models;
using SinavSistemi.Shared.Protocol;

namespace SinavSistemi.Egitmen.Services.Network;

/// <summary>
/// Eğitmen bilgisayarındaki LAN sunucusu.
///
/// Akış:
///   1. StartAsync → TcpListener 0.0.0.0:port dinler, her yeni bağlantı için HandleClientAsync ayrı görevde çalışır.
///   2. İlk mesaj Login olmalı. Öğrenci IStudentAuthenticator ile doğrulanır, token verilir.
///      Sınav sürüyorsa sınav hemen gönderilir (geç gelen / bağlantısı kopup geri gelen öğrenci).
///   3. BroadcastExamAsync → sınav cevapları ayıklanarak (StripForStudent) bağlı herkese gider.
///   4. Submit → süre kontrolü (+ SubmitGracePeriod) → Grader → IExamService.SaveSubmissionAsync.
///   5. EndExamAsync → herkese ExamEnded; öğrenci ekranları cevapları otomatik gönderir, ek süre içinde kabul edilir.
///
/// Thread'ler: ağ işleri arka plan thread'lerinde yürür. Arayüzün gördüğü her şey
/// (olaylar ve ConnectedStudent özellikleri) Ui(...) ile WPF thread'ine aktarılır.
/// </summary>
public sealed class TcpExamServer : IExamServer
{
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(15);

    private readonly IStudentAuthenticator _auth;
    private readonly IExamService _exams;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>Şu an bağlı (giriş yapmış) öğrenciler. Anahtar: öğrenci no. Aynı numarayla ikinci girişi engeller.</summary>
    private readonly ConcurrentDictionary<string, ClientConnection> _online = new();

    /// <summary>Eğitmen ekranındaki satırlar. Bağlantı kopup geri gelince aynı satır kullanılır. Yalnızca UI thread'inde değişir.</summary>
    private readonly Dictionary<string, ConnectedStudent> _rows = new();

    // Aktif sınav durumu; _examLock ile korunur.
    private readonly object _examLock = new();
    private Exam? _activeExam;           // cevaplı tam hali (puanlama için)
    private Exam? _studentCopy;          // öğrenciye giden, cevapları ayıklanmış hali
    private DateTime? _endedAt;          // eğitmen bitirdiyse
    private readonly HashSet<string> _submitted = new();

    public TcpExamServer(IStudentAuthenticator auth, IExamService exams)
    {
        _auth = auth;
        _exams = exams;
    }

    public bool IsRunning { get; private set; }
    public string LocalAddress { get; private set; } = "127.0.0.1";
    public int Port { get; private set; }

    public event EventHandler<ConnectedStudent>? StudentConnected;
    public event EventHandler<ConnectedStudent>? StudentChanged;
    public event EventHandler<LogEntry>? LogWritten;

    // ------------------------------------------------------------------ başlat / durdur

    public Task StartAsync(int port)
    {
        if (IsRunning) return Task.CompletedTask;

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();   // port doluysa SocketException → LiveExamViewModel kullanıcıya gösterir

        _listener = listener;
        _cts = new CancellationTokenSource();
        Port = port;
        LocalAddress = NetworkInfo.GetLanAddress();
        IsRunning = true;

        _ = AcceptLoopAsync(listener, _cts.Token);
        Log(LogLevel.Bilgi, "Sunucu", $"Sunucu {LocalAddress}:{port} üzerinde dinliyor");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!IsRunning) return Task.CompletedTask;

        _cts?.Cancel();
        _listener?.Stop();
        foreach (var c in _online.Values) c.Dispose();
        _online.Clear();
        Ui(() => _rows.Clear());

        lock (_examLock)
        {
            _activeExam = _studentCopy = null;
            _endedAt = null;
            _submitted.Clear();
        }

        IsRunning = false;
        Log(LogLevel.Bilgi, "Sunucu", "Sunucu durduruldu");
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex)
            {
                if (ct.IsCancellationRequested) break;
                Log(LogLevel.Hata, "Sunucu", "Bağlantı kabul edilemedi: " + ex.Message);
                continue;
            }

            tcp.NoDelay = true;
            _ = Task.Run(() => HandleClientAsync(tcp, ct));
        }
    }

    // ------------------------------------------------------------------ tek öğrenci bağlantısı

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken serverCt)
    {
        var ip = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
        var stream = new MessageStream(tcp);
        ClientConnection? conn = null;

        try
        {
            conn = await LoginAsync(stream, ip, serverCt);
            if (conn is null) return;

            // Mesaj döngüsü: bağlantı kapanınca ReadAsync null döner
            while (await stream.ReadAsync(serverCt) is { } env)
            {
                if (env.Token != conn.Token)
                {
                    Log(LogLevel.Uyari, ip, $"{conn.Number}: geçersiz token ile mesaj geldi, yok sayıldı");
                    continue;
                }

                switch (env.Type)
                {
                    case MessageType.Progress when env.Read<ProgressPayload>() is { } p:
                        Ui(() => { conn.Row.AnsweredCount = p.AnsweredCount; StudentChanged?.Invoke(this, conn.Row); });
                        break;

                    case MessageType.Submit:
                        await HandleSubmitAsync(conn, env.Read<SubmitPayload>());
                        break;

                    case MessageType.Ping:
                        await stream.SendAsync(MessageType.Pong);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { /* sunucu durduruluyor */ }
        catch (IOException) { /* ağ koptu */ }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            Log(LogLevel.Hata, ip, "Beklenmeyen hata: " + ex.Message);
        }
        finally
        {
            stream.Dispose();
            if (conn is not null) OnDisconnected(conn, serverCt.IsCancellationRequested);
        }
    }

    /// <returns>Giriş başarılıysa bağlantı, değilse null (bağlantı kapatılır).</returns>
    private async Task<ClientConnection?> LoginAsync(MessageStream stream, string ip, CancellationToken serverCt)
    {
        // Bağlanıp hiçbir şey göndermeyen istemci sonsuza kadar yer tutmasın
        using var loginCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
        loginCts.CancelAfter(LoginTimeout);

        Envelope? env;
        try { env = await stream.ReadAsync(loginCts.Token); }
        catch (OperationCanceledException) when (!serverCt.IsCancellationRequested) { return null; }

        if (env?.Type != MessageType.Login || env.Read<LoginRequest>() is not { } req
            || string.IsNullOrWhiteSpace(req.StudentNumber))
            return null;

        var number = req.StudentNumber.Trim();

        async Task<ClientConnection?> Fail(string reason, LogLevel level = LogLevel.Uyari)
        {
            await stream.SendAsync(MessageType.LoginFail, new LoginFailPayload(reason));
            Log(level, ip, $"{number} girişi reddedildi: {reason}");
            return null;
        }

        var user = await _auth.ValidateAsync(number, req.Password);
        if (user is null) return await Fail("Öğrenci numarası veya şifre hatalı.");

        bool alreadySubmitted;
        lock (_examLock) alreadySubmitted = _activeExam is not null && _submitted.Contains(number);
        if (alreadySubmitted) return await Fail("Bu sınavı zaten teslim ettiniz.");

        var conn = new ClientConnection(stream, number, user, ip, Guid.NewGuid().ToString("N"));

        // Aynı numara zaten bağlıysa:
        //  - aynı IP'den geliyorsa öğrencinin kendisi yeniden bağlanıyordur (Wi-Fi koptu, sunucu henüz fark etmedi):
        //    eski bağlantı kapatılır, yenisi geçer.
        //  - farklı IP'den geliyorsa başka bir bilgisayardan aynı numarayla giriş denemesidir: reddedilir.
        while (!_online.TryAdd(number, conn))
        {
            if (!_online.TryGetValue(number, out var existing)) continue;   // tam bu arada çıktı, tekrar dene
            if (existing.Ip != ip)
                return await Fail("Bu numarayla başka bir bilgisayardan giriş yapılmış.");

            existing.IsReplaced = true;
            if (_online.TryUpdate(number, conn, existing))
            {
                existing.Dispose();
                break;
            }
        }

        try
        {
            await stream.SendAsync(MessageType.LoginOk, new LoginOkPayload(conn.Token, user.FullName, user.ClassName));
        }
        catch
        {
            _online.TryRemove(new KeyValuePair<string, ClientConnection>(number, conn));
            throw;
        }

        // Eğitmen ekranındaki satırı oluştur / güncelle
        bool reconnect = false;
        await UiAsync(() =>
        {
            if (_rows.TryGetValue(number, out var row))
            {
                reconnect = true;
                row.IpAddress = ip;
                row.State = ClientState.Bagli;
                conn.Row = row;
                StudentChanged?.Invoke(this, row);
            }
            else
            {
                row = new ConnectedStudent
                {
                    StudentNumber = number, FullName = user.FullName, IpAddress = ip,
                    ConnectedAt = DateTime.Now, State = ClientState.Bagli
                };
                _rows[number] = row;
                conn.Row = row;
                StudentConnected?.Invoke(this, row);
            }
        });
        Log(LogLevel.Bilgi, ip, reconnect ? $"{number} yeniden bağlandı" : $"{number} bağlandı ({user.FullName})");

        // Bundan sonra BroadcastExamAsync bu öğrenciyi de görür. Daha önce görseydi
        // öğrenciye LoginOk'tan önce Exam gidebilirdi.
        conn.IsReady = true;

        // Sınav sürüyorsa hemen gönder. (Tam bu arada dağıtım yapılırsa sınav iki kez gidebilir;
        // istemci aynı sınavı ikinci kez açmaz.)
        if (GetOpenExam() is { } open) await SendExamAsync(conn, open.Copy, open.EndsAt);

        return conn;
    }

    private void OnDisconnected(ClientConnection conn, bool serverStopping)
    {
        // Yalnızca listedeki bağlantı buysa çıkar (yerine yeni bağlantı gelmiş olabilir)
        _online.TryRemove(new KeyValuePair<string, ClientConnection>(conn.Number, conn));

        // Aynı öğrenci yeniden bağlandığı için kapatıldıysa satır yeni bağlantıya ait, dokunma
        if (serverStopping || conn.IsReplaced) return;

        Ui(() =>
        {
            if (conn.Row.State == ClientState.TeslimEtti) return;   // teslim edip çıkması normal
            conn.Row.State = ClientState.BaglantiKoptu;
            StudentChanged?.Invoke(this, conn.Row);
        });
        Log(LogLevel.Uyari, conn.Ip, $"{conn.Number} bağlantısı koptu");
    }

    // ------------------------------------------------------------------ sınav dağıtma / bitirme

    public async Task BroadcastExamAsync(Exam exam)
    {
        Exam copy;
        lock (_examLock)
        {
            _activeExam = exam;
            _studentCopy = copy = Protocol.StripForStudent(exam);
            _endedAt = null;
            _submitted.Clear();
        }

        var targets = _online.Values.Where(c => c.IsReady).ToList();
        await Task.WhenAll(targets.Select(c => SendExamAsync(c, copy, exam.EndsAt)));
        Log(LogLevel.Bilgi, "Sunucu", $"\"{exam.Title}\" {targets.Count} öğrenciye gönderildi ({exam.DurationMinutes} dk)");
    }

    public async Task EndExamAsync(int examId)
    {
        lock (_examLock)
        {
            if (_activeExam?.Id != examId) return;
            _endedAt ??= DateTime.Now;
        }

        // Öğrenci ekranları ExamEnded alınca cevapları otomatik gönderir (ek süre içinde kabul edilir)
        await Task.WhenAll(_online.Values.Where(c => c.IsReady).ToList().Select(c => SafeSendAsync(c, MessageType.ExamEnded)));
        Log(LogLevel.Uyari, "Sunucu",
            $"Sınav sonlandırıldı. Açık cevaplar {Protocol.SubmitGracePeriod.TotalSeconds:0} sn içinde toplanıyor, sonra gönderim kabul edilmeyecek");
    }

    private async Task SendExamAsync(ClientConnection conn, Exam copy, DateTime? endsAt)
    {
        int remaining = endsAt is DateTime end ? Math.Max(0, (int)(end - DateTime.Now).TotalSeconds) : 0;
        if (await SafeSendAsync(conn, MessageType.Exam, new ExamPayload(copy, remaining)))
            Ui(() => { conn.Row.State = ClientState.SinavDa; StudentChanged?.Invoke(this, conn.Row); });
    }

    /// <summary>Sınav hâlâ yeni girişlere açık mı (dağıtıldı, bitirilmedi, süresi dolmadı)?</summary>
    private (Exam Copy, DateTime? EndsAt)? GetOpenExam()
    {
        lock (_examLock)
        {
            if (_activeExam is null || _studentCopy is null || _endedAt is not null) return null;
            if (_activeExam.EndsAt is DateTime end && DateTime.Now >= end) return null;
            return (_studentCopy, _activeExam.EndsAt);
        }
    }

    // ------------------------------------------------------------------ teslim

    private async Task HandleSubmitAsync(ClientConnection conn, SubmitPayload? payload)
    {
        var now = DateTime.Now;
        Exam? exam = null;
        string? rejectReason;

        lock (_examLock)
        {
            rejectReason = CheckCanSubmit(conn.Number, payload, now);
            if (rejectReason is null)
            {
                exam = _activeExam;
                _submitted.Add(conn.Number);   // ikinci teslimi engelle
            }
        }

        if (rejectReason is not null || exam is null || payload is null)
        {
            var reason = rejectReason ?? "Aktif bir sınav yok.";
            await SafeSendAsync(conn, MessageType.SubmitRejected, new SubmitResultPayload(false, reason, now));
            Log(LogLevel.Hata, conn.Ip, $"{conn.Number} gönderimi reddedildi: {reason}");
            return;
        }

        try
        {
            var grade = Grader.Grade(exam, payload.Answers ?? new List<StudentAnswer>());
            var submission = new Submission
            {
                ExamId = exam.Id,
                Student = conn.User,
                Answers = payload.Answers ?? new List<StudentAnswer>(),
                SubmittedAt = now,
                IpAddress = conn.Ip,
                EarnedPoints = grade.EarnedPoints,
                MaxPoints = grade.MaxPoints,
                CorrectCount = grade.CorrectCount,
                GradedCount = grade.GradedCount,
                Score = grade.Percentage
            };
            await _exams.SaveSubmissionAsync(submission);

            await SafeSendAsync(conn, MessageType.SubmitOk, new SubmitResultPayload(true, null, now));
            Ui(() => { conn.Row.State = ClientState.TeslimEtti; StudentChanged?.Invoke(this, conn.Row); });
            Log(LogLevel.Bilgi, conn.Ip,
                $"{conn.Number} sınavı teslim etti — {grade.CorrectCount}/{grade.GradedCount} doğru, puan {grade.Percentage:0.0}");
        }
        catch (Exception ex)
        {
            // Kayıt başarısızsa öğrenci tekrar gönderebilsin
            lock (_examLock) _submitted.Remove(conn.Number);
            await SafeSendAsync(conn, MessageType.SubmitRejected,
                new SubmitResultPayload(false, "Sunucu cevabı kaydedemedi, tekrar deneyin.", now));
            Log(LogLevel.Hata, "Sunucu", $"{conn.Number} teslimi kaydedilemedi: {ex.Message}");
        }
    }

    /// <summary>Teslim kabul edilebilir mi? Edilemiyorsa sebebini döndürür. _examLock altında çağrılmalı.</summary>
    private string? CheckCanSubmit(string studentNumber, SubmitPayload? payload, DateTime now)
    {
        if (_activeExam is null || payload is null || payload.ExamId != _activeExam.Id)
            return "Aktif bir sınav yok.";

        // Son teslim anı: süre bitişi ile eğitmenin bitirdiği andan hangisi önceyse + ek süre
        DateTime? closeAt = _activeExam.EndsAt;
        if (_endedAt is DateTime ended && (closeAt is null || ended < closeAt)) closeAt = ended;
        if (closeAt is DateTime close && now > close + Protocol.SubmitGracePeriod)
            return "Sınav süresi dolmuş.";

        if (_submitted.Contains(studentNumber))
            return "Bu sınavı zaten teslim ettiniz.";

        return null;
    }

    // ------------------------------------------------------------------ yardımcılar

    private static async Task<bool> SafeSendAsync(ClientConnection conn, MessageType type, object? payload = null)
    {
        try
        {
            await conn.Stream.SendAsync(type, payload);
            return true;
        }
        catch (Exception)
        {
            // Bağlantı kopmuş; okuma döngüsü bunu fark edip OnDisconnected'ı çağıracak
            return false;
        }
    }

    private void Log(LogLevel level, string source, string message)
    {
        var entry = new LogEntry { Time = DateTime.Now, Level = level, Source = source, Message = message };
        Ui(() => LogWritten?.Invoke(this, entry));
    }

    /// <summary>Arayüz thread'inde çalıştırır (sıra korunur). Test gibi WPF olmayan ortamda doğrudan çalıştırır.</summary>
    private static void Ui(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }

    private static Task UiAsync(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) { action(); return Task.CompletedTask; }
        return d.InvokeAsync(action).Task;
    }

    /// <summary>Giriş yapmış bir öğrencinin bağlantısı.</summary>
    private sealed class ClientConnection(MessageStream stream, string number, User user, string ip, string token) : IDisposable
    {
        public MessageStream Stream { get; } = stream;
        public string Number { get; } = number;
        public User User { get; } = user;
        public string Ip { get; } = ip;
        public string Token { get; } = token;
        public ConnectedStudent Row { get; set; } = null!;

        /// <summary>LoginOk gönderildi ve eğitmen ekranındaki satır hazır; artık sınav mesajları gönderilebilir.</summary>
        public volatile bool IsReady;

        /// <summary>Aynı öğrencinin yeni bağlantısı bunun yerini aldı.</summary>
        public volatile bool IsReplaced;

        public void Dispose()
        {
            try { Stream.Dispose(); } catch { /* zaten kapalı */ }
        }
    }
}
