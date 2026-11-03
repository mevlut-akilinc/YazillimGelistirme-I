namespace SinavSistemi.Shared.Models;

public enum UserRole
{
    Egitmen,
    Ogrenci
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public UserRole Role { get; set; }
    /// <summary>Öğrenci için öğrenci numarası, eğitmen için boş.</summary>
    public string? StudentNumber { get; set; }
    /// <summary>Öğrencinin şubesi / sınıfı (sınıf bazlı raporlama için).</summary>
    public string? ClassName { get; set; }
}

/// <summary>Başarılı girişte backend'in döndürdüğü oturum bilgisi.</summary>
public class Session
{
    public User User { get; set; } = new();
    public string Token { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.Now;
}
