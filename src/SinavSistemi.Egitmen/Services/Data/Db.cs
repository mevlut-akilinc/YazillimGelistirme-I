using System.IO;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace SinavSistemi.Egitmen.Services.Data;

/// <summary>
/// Veritabanı erişiminin tek giriş noktası:
///  - appsettings.json (+ varsa appsettings.Local.json) okunur,
///  - uygulama boyunca kullanılacak TEK NpgsqlDataSource oluşturulur (bağlantı havuzu onun içinde),
///  - Dapper'ın snake_case → PascalCase eşlemesi açılır.
///
/// Servisler bağlantıyı şöyle açar:
///   await using var conn = await dataSource.OpenConnectionAsync();
/// </summary>
public static class Db
{
    public const string SettingsFile = "appsettings.json";
    /// <summary>Gerçek şifreyi içerir, git'e girmez. Varsa appsettings.json'daki değerleri ezer.</summary>
    public const string LocalSettingsFile = "appsettings.Local.json";
    public const string ConnectionStringName = "SinavDb";

    /// <summary>UseDatabase=true ise ConnectAsync sonrası dolu, aksi halde null.</summary>
    public static NpgsqlDataSource? DataSource { get; private set; }

    /// <summary>Ayarları exe'nin yanındaki json dosyalarından okur. Local dosyası yoksa hata vermez.</summary>
    public static IConfiguration LoadConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(SettingsFile, optional: false)
            .AddJsonFile(LocalSettingsFile, optional: true)
            .Build();

    public static bool UseDatabase(IConfiguration config) => config.GetValue("UseDatabase", false);

    /// <summary>Log klasörünün tam yolu. Göreli yol verildiyse exe'nin yanındaki klasör kabul edilir.</summary>
    public static string GetLogDirectory(IConfiguration config)
    {
        var dir = config["LogDirectory"];
        if (string.IsNullOrWhiteSpace(dir)) dir = "logs";
        return Path.IsPathRooted(dir) ? dir : Path.Combine(AppContext.BaseDirectory, dir);
    }

    /// <summary>
    /// DataSource'u oluşturur ve bir bağlantı açıp kapatarak sunucuya ulaşılabildiğini test eder.
    /// Bağlantı açılamazsa NpgsqlException / SocketException vb. fırlatır (çağıran kullanıcıya gösterir).
    /// </summary>
    public static async Task ConnectAsync(IConfiguration config)
    {
        var cs = config.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} ayarı bulunamadı.");

        // Kolon adları snake_case (full_name) → özellik adları PascalCase (FullName).
        // Adı farklı olanlar (body → Text gibi) SQL'de AS ile eşlenir.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var dataSource = NpgsqlDataSource.Create(cs);
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync();
        }
        catch
        {
            await dataSource.DisposeAsync();
            throw;
        }
        DataSource = dataSource;
    }

    /// <summary>Uygulama kapanırken havuzdaki bağlantıları kapatır.</summary>
    public static void Dispose()
    {
        DataSource?.Dispose();
        DataSource = null;
    }
}
