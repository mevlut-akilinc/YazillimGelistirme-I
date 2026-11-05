using SinavSistemi.Egitmen.Services.Mock;

namespace SinavSistemi.Egitmen.Services;

/// <summary>Servislerin tek kayıt noktası. Gerçek sınıflar hazır olunca "new Mock..." satırlarını değiştirin.</summary>
public static class ServiceLocator
{
    public static IAuthService Auth { get; set; } = new MockAuthService();
    public static IQuestionService Questions { get; set; } = new MockQuestionService();
    public static IExamService Exams { get; set; } = new MockExamService();
    public static IExamServer Server { get; set; } = new MockExamServer();

    public const int DefaultPort = 5050;
}
