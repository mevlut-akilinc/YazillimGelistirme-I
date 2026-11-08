using Microsoft.Extensions.Configuration;
using SinavSistemi.Egitmen.Services.Data;
using SinavSistemi.Egitmen.Services.Mock;
using SinavSistemi.Egitmen.Services.Network;

namespace SinavSistemi.Egitmen.Services;

/// <summary>
/// Servislerin tek kayıt noktası. App.OnStartup → Initialize(config) ile doldurulur.
/// appsettings.json'daki "UseDatabase" anahtarı PostgreSQL (Pg*) ile bellek içi (Mock*) servisler arasında seçim yapar.
/// </summary>
public static class ServiceLocator
{
    public static IAuthService Auth { get; set; } = null!;
    public static IStudentAuthenticator StudentAuth { get; set; } = null!;
    public static IQuestionService Questions { get; set; } = null!;
    public static IExamService Exams { get; set; } = null!;

    /// <summary>Gerçek LAN sunucusu. Arayüzü öğrencisiz denemek için Initialize içinde: new MockExamServer()</summary>
    public static IExamServer Server { get; set; } = null!;

    public const int DefaultPort = 5050;

    /// <summary>
    /// Servisleri oluşturur. Sıra önemli: Server, StudentAuth ve Exams'ı constructor'da aldığı için en son oluşturulur.
    /// UseDatabase=true ise Db.ConnectAsync daha önce başarıyla çağrılmış olmalı.
    /// </summary>
    public static void Initialize(IConfiguration config)
    {
        bool useDb = Db.UseDatabase(config);
        if (useDb && Db.DataSource is null)
            throw new InvalidOperationException("UseDatabase=true ama veritabanı bağlantısı kurulmadı (Db.ConnectAsync).");

        // Pg* servisleri yazıldıkça her satır şu kalıba dönüşecek:
        //   Auth = useDb ? new PgAuthService(Db.DataSource!) : new MockAuthService();
        Auth = new MockAuthService();
        StudentAuth = new MockStudentAuthenticator();
        Questions = new MockQuestionService();
        Exams = new MockExamService();

        Server = new TcpExamServer(StudentAuth, Exams);
    }
}
