using System.IO;
using System.Text;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Egitmen.Services.Data;

/// <summary>
/// Logları günlük dosyalara ekler: {klasör}/sinav-YYYYMMDD.log
/// Satır biçimi: zaman | seviye | kaynak | mesaj
///
/// Sunucu thread'lerinden aynı anda çağrılır. Aynı dosyaya iki yazım çakışmasın diye
/// SemaphoreSlim ile tek seferde tek yazım yapılır (lock, await içeren kodda kullanılamaz).
/// </summary>
public sealed class LogFileWriter(string directory)
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Directory { get; } = directory;

    public async Task AppendAsync(LogEntry entry)
    {
        // Mesajdaki satır sonları tek satır kuralını bozmasın
        var message = entry.Message.Replace("\r", " ").Replace("\n", " ");
        var line = $"{entry.Time:yyyy-MM-dd HH:mm:ss} | {entry.Level} | {entry.Source} | {message}{Environment.NewLine}";
        var path = Path.Combine(Directory, $"sinav-{entry.Time:yyyyMMdd}.log");

        await _gate.WaitAsync();
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            await File.AppendAllTextAsync(path, line, Utf8NoBom);
        }
        finally
        {
            _gate.Release();
        }
    }
}
