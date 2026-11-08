using SinavSistemi.Shared.Models;

namespace SinavSistemi.Egitmen.Services.Data;

/// <summary>users tablosundan okunan satır (Dapper bunu doldurur, snake_case → PascalCase).</summary>
internal sealed class UserRow
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "";          // 'Egitmen' | 'Ogrenci' (C# enum adıyla aynı)
    public string? StudentNumber { get; set; }
    public string? ClassName { get; set; }

    /// <summary>Şifre hash'i model nesnesine KOPYALANMAZ; sadece doğrulamada kullanılır.</summary>
    public User ToUser() => new()
    {
        Id = Id,
        Username = Username,
        FullName = FullName,
        Role = Enum.Parse<UserRole>(Role),
        StudentNumber = StudentNumber,
        ClassName = ClassName
    };
}

/// <summary>BCrypt şifre kontrolü (seed'deki pgcrypto crypt(..., gen_salt('bf')) hash'leriyle uyumlu).</summary>
internal static class PasswordCheck
{
    /// <summary>
    /// Kullanıcı bulunamadığında da aynı süre harcansın diye karşılaştırılan sahte hash.
    /// Böylece cevap süresinden "bu kullanıcı adı var mı" anlaşılamaz.
    /// </summary>
    private static readonly Lazy<string> DummyHash =
        new(() => BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), 10));

    /// <summary>
    /// Şifre hash'le eşleşiyor mu? hash null ise (kullanıcı yok) sahte hash'le karşılaştırıp false döner.
    /// BCrypt kasıtlı olarak yavaştır (~50–100 ms); arayüz donmasın diye thread pool'da çalışır.
    /// </summary>
    public static Task<bool> VerifyAsync(string password, string? hash) => Task.Run(() =>
    {
        try
        {
            bool ok = BCrypt.Net.BCrypt.Verify(password, hash ?? DummyHash.Value);
            return ok && hash is not null;
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;   // veritabanındaki hash bozuk/BCrypt değil: giriş reddedilir
        }
    });
}
