using SinavSistemi.Egitmen.Services.Mock;
using SinavSistemi.Egitmen.Services.Network;

namespace SinavSistemi.Egitmen.Services;

/// <summary>Servislerin tek kayıt noktası. Gerçek sınıflar hazır olunca "new Mock..." satırlarını değiştirin.</summary>
public static class ServiceLocator
{
    // Sıra önemli: Server, kendinden önce tanımlanan servisleri kullanır.
    public static IAuthService Auth { get; set; } = new MockAuthService();
    public static IStudentAuthenticator StudentAuth { get; set; } = new MockStudentAuthenticator();
    public static IQuestionService Questions { get; set; } = new MockQuestionService();
    public static IExamService Exams { get; set; } = new MockExamService();

    /// <summary>Gerçek LAN sunucusu. Arayüzü öğrencisiz denemek için: new MockExamServer()</summary>
    public static IExamServer Server { get; set; } = new TcpExamServer(StudentAuth, Exams);

    public const int DefaultPort = 5050;
}
