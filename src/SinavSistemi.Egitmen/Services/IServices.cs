using SinavSistemi.Shared.Models;

namespace SinavSistemi.Egitmen.Services;

// Egitmen.exe'nin servisleri. Arayüz sadece bu interface'leri bilir;
// gerçek implementasyonlar hazır olunca ServiceLocator'daki Mock satırları değiştirilir.

/// <summary>
/// Eğitmen girişi. Veritabanı aynı bilgisayarda olduğu için doğrudan PostgreSQL'e sorar (ağ yok).
/// Öğrenci girişi bu servisi DEĞİL, IExamServer içindeki LOGIN mesajı işleyicisini kullanır
/// (aynı şifre-hash kontrol fonksiyonu paylaşılabilir).
/// </summary>
public interface IAuthService
{
    Task<Session?> LoginAsync(string username, string password);
    Task LogoutAsync();
    Session? CurrentSession { get; }
}

/// <summary>
/// Öğrenci girişini doğrular. IExamServer, ağdan gelen LOGIN mesajında bunu çağırır.
/// Gerçek implementasyon: PostgreSQL'den öğrenciyi bulup şifre hash'ini (BCrypt) karşılaştırır.
/// </summary>
public interface IStudentAuthenticator
{
    /// <returns>Bilgiler doğruysa öğrenci, değilse null.</returns>
    Task<User?> ValidateAsync(string studentNumber, string password);
}

/// <summary>Soru bankası CRUD + kategoriler (PostgreSQL).</summary>
public interface IQuestionService
{
    Task<IReadOnlyList<Question>> GetAllAsync();
    Task<IReadOnlyList<string>> GetCategoriesAsync();
    Task<Question> AddAsync(Question question);
    Task UpdateAsync(Question question);
    Task DeleteAsync(int questionId);
}

/// <summary>Sınav kaydı, sonuçlar ve log kayıtları (PostgreSQL + log dosyaları).</summary>
public interface IExamService
{
    Task<IReadOnlyList<Exam>> GetExamsAsync();
    Task<Exam> SaveAsync(Exam exam);
    Task SaveSubmissionAsync(Submission submission);
    Task<IReadOnlyList<ResultRow>> GetResultsAsync(int examId);
    Task<IReadOnlyList<LogEntry>> GetLogsAsync(int? examId = null);
}

/// <summary>
/// LAN sunucusu (TcpListener). Görevleri:
/// - Öğrenci LOGIN mesajını DB'den doğrulamak, token vermek, aynı numarayla ikinci girişi reddetmek
/// - Sınavı Protocol.StripForStudent ile cevapsız halde tüm öğrencilere göndermek
/// - SUBMIT geldiğinde süre kontrolü + puanlama (kodlama: GccRunner ile tüm testler) + log
/// Olaylar arka plan thread'inden geliyorsa Application.Current.Dispatcher.Invoke ile tetiklenmeli.
/// </summary>
public interface IExamServer
{
    bool IsRunning { get; }
    string LocalAddress { get; }
    int Port { get; }

    Task StartAsync(int port);
    Task StopAsync();
    Task BroadcastExamAsync(Exam exam);
    Task EndExamAsync(int examId);

    event EventHandler<ConnectedStudent>? StudentConnected;
    event EventHandler<ConnectedStudent>? StudentChanged;
    event EventHandler<LogEntry>? LogWritten;
}
