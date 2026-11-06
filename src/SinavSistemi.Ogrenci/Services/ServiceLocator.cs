using SinavSistemi.Shared.Code;
using SinavSistemi.Ogrenci.Services.Mock;

namespace SinavSistemi.Ogrenci.Services;

/// <summary>Servislerin tek kayıt noktası.</summary>
public static class ServiceLocator
{
    /// <summary>Gerçek TCP istemcisi. Sunucusuz arayüz denemesi için: new MockExamClient()</summary>
    public static IExamClient Client { get; set; } = new TcpExamClient();

    /// <summary>"Derle ve test et" bu bilgisayardaki gcc ile çalışır (hibrit model).</summary>
    public static GccRunner CodeRunner { get; } = new();

    public const int DefaultPort = 5050;
}
