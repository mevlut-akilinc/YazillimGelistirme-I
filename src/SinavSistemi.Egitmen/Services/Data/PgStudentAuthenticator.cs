using Dapper;
using Npgsql;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Egitmen.Services.Data;

/// <summary>
/// Öğrenci girişi (TcpExamServer, ağdan gelen LOGIN mesajında çağırır).
/// Öğrenci numarasıyla users tablosundan öğrenciyi bulur, şifreyi BCrypt ile doğrular.
/// Dönen User.Id dolu: teslim kaydında submissions.student_id buradan gelir.
/// Sunucu thread'lerinden aynı anda çağrılabilir; her çağrı kendi bağlantısını açar (havuzdan).
/// </summary>
public sealed class PgStudentAuthenticator(NpgsqlDataSource dataSource) : IStudentAuthenticator
{
    public async Task<User?> ValidateAsync(string studentNumber, string password)
    {
        UserRow? row;
        await using (var conn = await dataSource.OpenConnectionAsync())
        {
            row = await conn.QuerySingleOrDefaultAsync<UserRow>(
                """
                SELECT id, username, password_hash, full_name, role, student_number, class_name
                FROM users
                WHERE role = 'Ogrenci' AND student_number = @studentNumber
                """,
                new { studentNumber });
        }

        if (!await PasswordCheck.VerifyAsync(password, row?.PasswordHash)) return null;
        return row!.ToUser();
    }
}
