using Dapper;
using Npgsql;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Egitmen.Services.Data;

/// <summary>
/// Eğitmen girişi: users tablosundan kullanıcıyı çeker, şifreyi BCrypt ile doğrular.
/// Sadece role = 'Egitmen' olanlar girebilir (öğrenci hesabıyla eğitmen paneline girilemez).
/// </summary>
public sealed class PgAuthService(NpgsqlDataSource dataSource) : IAuthService
{
    public Session? CurrentSession { get; private set; }

    public async Task<Session?> LoginAsync(string username, string password)
    {
        UserRow? row;
        await using (var conn = await dataSource.OpenConnectionAsync())
        {
            row = await conn.QuerySingleOrDefaultAsync<UserRow>(
                """
                SELECT id, username, password_hash, full_name, role, student_number, class_name
                FROM users
                WHERE username = @username AND role = 'Egitmen'
                """,
                new { username });
        }

        // "Kullanıcı yok" ile "şifre yanlış" aynı sonucu (null) verir; çağıran ayrım yapamaz.
        if (!await PasswordCheck.VerifyAsync(password, row?.PasswordHash)) return null;

        CurrentSession = new Session
        {
            User = row!.ToUser(),
            Token = Guid.NewGuid().ToString("N"),
            StartedAt = DateTime.Now
        };
        return CurrentSession;
    }

    public Task LogoutAsync()
    {
        CurrentSession = null;
        return Task.CompletedTask;
    }
}
