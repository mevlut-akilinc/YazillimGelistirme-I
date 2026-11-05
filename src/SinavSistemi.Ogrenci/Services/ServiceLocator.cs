using SinavSistemi.Shared.Code;
using SinavSistemi.Ogrenci.Services.Mock;

namespace SinavSistemi.Ogrenci.Services;

/// <summary>Servislerin tek kayıt noktası. Gerçek istemci hazır olunca MockExamClient satırını değiştirin.</summary>
public static class ServiceLocator
{
    public static IExamClient Client { get; set; } = new MockExamClient();

    /// <summary>"Derle ve test et" bu bilgisayardaki gcc ile çalışır (hibrit model).</summary>
    public static GccRunner CodeRunner { get; } = new();

    public const int DefaultPort = 5050;
}
