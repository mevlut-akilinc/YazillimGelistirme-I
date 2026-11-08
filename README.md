# Lab Sınav Sistemi

Yazılım Geliştirme I (2026-27 Güz). C# / .NET 10 / WPF, MVVM.

## Mimari: iki exe + ortak kütüphane

| Proje | Çıktı | Nerede çalışır | İçinde ne var |
|---|---|---|---|
| `SinavSistemi.Egitmen` | **Egitmen.exe** | Sunucu (eğitmen) bilgisayarı | Eğitmen paneli + LAN sunucusu + PostgreSQL erişimi + puanlama |
| `SinavSistemi.Ogrenci` | **Ogrenci.exe** | Öğrenci bilgisayarları | Giriş, sınav ekranı, yerel "Derle ve test et" |
| `SinavSistemi.Shared` | DLL | İkisi de kullanır | Modeller, tema, mesaj protokolü, gcc çalıştırıcı |

Alınan kararlar:
1. Sunucu ile eğitmen paneli **aynı exe**. Eğitmen "Sunucuyu başlat"a basar. Sınav sürerken pencere kapatılırsa uyarı çıkar.
2. **Hibrit derleme:** "Derle ve test et" öğrencinin bilgisayarında, sadece görünür testlerle çalışır. Gizli testler, puanlama ve karmaşıklık ölçümü teslimden sonra Egitmen.exe'de yapılır.
3. **Öğrenci girişi sunucu üzerinden:** Ogrenci.exe veritabanını bilmez. Numara ve şifre TCP ile Egitmen.exe'ye gider, orada doğrulanır.
4. PostgreSQL yalnızca `localhost`'u dinler (`listen_addresses = 'localhost'`), internet gerekmez.

## Çalıştırma (geliştirme)
1. `SinavSistemi.sln` dosyasını Visual Studio 2026'da açın (.NET 10 SDK gerekir).
2. Solution'a sağ tıklayın → *Configure Startup Projects* → *Multiple startup projects* → Egitmen ve Ogrenci için **Start** seçin. F5 ile ikisi birlikte açılır.
3. **Veritabanı:** Egitmen.exe varsayılan olarak PostgreSQL ile çalışır (`"UseDatabase": true`). Kurulum, `db/` betikleri ve şifre ayarı için **[KURULUM.md](KURULUM.md)**'yi izleyin.
   Veritabanı olmadan denemek için `src/SinavSistemi.Egitmen/appsettings.json` (veya `appsettings.Local.json`) içinde `"UseDatabase": false` yapın; kullanıcılar, sorular ve teslimler bellekte (mock) tutulur.
4. Ağ iletişimi **gerçek TCP** ile çalışır:
   - Egitmen.exe: `egitmen` / `1234` → Canlı Sınav → **Sunucuyu başlat** → öğrenciler bağlanınca **Sınavı dağıt**
   - Ogrenci.exe: demo öğrenci numaralarından biri (`230201001`, `230201014`, … bkz. KURULUM.md; mock modda herhangi bir numara) / `1234`, sunucu IP'si `127.0.0.1` (aynı bilgisayar) veya Canlı Sınav ekranında yazan IP
   - Öğrenciler teslim edince puanlar **Sonuçlar** ekranına düşer (veritabanında `submissions` + soru bazlı `answers`).
   - Ağ olmadan yalnızca arayüzü denemek için `ServiceLocator.Initialize` içinde `MockExamServer`, Ogrenci'nin `ServiceLocator.cs` dosyasında `MockExamClient` satırını açın.
5. Kodlama sorusunda "Derle ve test et" **gerçek gcc** kullanır. `gcc --version` çalışmıyorsa `ServiceLocator.CodeRunner.GccPath` değerine tam yol verin.

## İki laptopla test
- İki bilgisayarı **telefon hotspot'una** bağlayın. Okul Wi-Fi'ı cihazların birbirini görmesini çoğu zaman engeller.
- Egitmen.exe ilk port açışında Windows Güvenlik Duvarı izin isteyecek: **Özel ağlar**'ı işaretleyin.
- Öğrenci girişinde Canlı Sınav ekranındaki IP'yi kullanın.

## Servisler ve veritabanı

**Egitmen.exe** (`Services/IServices.cs`). `App.OnStartup` ayarları okur, `UseDatabase=true` ise bağlantıyı test eder,
sonra `ServiceLocator.Initialize(config)` servisleri oluşturur:

| Arayüz | `UseDatabase: true` (`Services/Data/`) | `UseDatabase: false` |
|---|---|---|
| `IAuthService` (eğitmen girişi) | `PgAuthService`: `users`, BCrypt, sadece `Egitmen` rolü | `MockAuthService` |
| `IStudentAuthenticator` (öğrenci LOGIN'i) | `PgStudentAuthenticator`: öğrenci no + BCrypt | `MockStudentAuthenticator` |
| `IQuestionService` (soru bankası) | `PgQuestionService`: soft delete, şıklar id bazlı güncellenir | `MockQuestionService` |
| `IExamService` (sınav, teslim, sonuç, log) | `PgExamService`: DB + log dosyası | `MockExamService` |
| `IExamServer` | **`Services/Network/TcpExamServer.cs`** (her iki modda) | — |

Veri erişimi:
- **Dapper + Npgsql**, EF Core yok. Uygulama boyunca tek `NpgsqlDataSource` (`Services/Data/Db.cs`); her işlem havuzdan bağlantı açar.
- Tüm sorgular **parametreli** ve **async**; çok adımlı yazmalar (soru, sınav, teslim) tek transaction.
- Enum'lar metin olarak saklanır, değerler C# enum adlarıyla aynıdır (`'Yayinda'`, `'ONLogN'`…). Şema: `db/01_schema.sql`, diyagram: `docs/diagrams/03-er.mmd`.
- Silinen sorular `is_deleted = true` olur; eski sınavlar ve cevaplar bozulmaz.

Ayarlar (`src/SinavSistemi.Egitmen/`):
- `appsettings.json` repoya girer, **şifre içermez**. `appsettings.Local.json` (git'te yok) gerçek şifreyi içerir ve onu ezer. Şablon: `appsettings.Local.example.json`.
- `UseDatabase=true` iken bağlantı açılamazsa uygulama mock'a düşmez; hatayı gösterip kapanır.

**TcpExamServer** (`TcpListener` :5050): Öğrenci LOGIN'ini `IStudentAuthenticator` ile doğrular, sınavı `Protocol.StripForStudent` ile gönderir,
SUBMIT'te süre kontrolü + `Grader` + `SaveSubmissionAsync` yapar. Soru bazlı sonuçlar (`IsCorrect`, `EarnedPoints`, `IsPending`)
sunucuda Grader'dan doldurulur; bu alanlar `[JsonIgnore]` olduğu için protokolde taşınmaz.

**Loglar** (PDF isteri 12): Sunucunun her logu hem `logs` tablosuna hem exe'nin yanındaki `logs/sinav-YYYYMMDD.log` dosyasına yazılır
(`zaman | seviye | kaynak | mesaj`), **Loglar** ekranında görüntülenir.

**Ogrenci.exe** → **`Services/TcpExamClient.cs`**: `TcpClient` + `MessageStream`. Login → okuma döngüsü (Exam, ExamEnded, SubmitOk/Rejected) → Submit.
Veritabanına hiç bağlanmaz.

### Sunucu kuralları
| Durum | Davranış |
|---|---|
| Aynı numara, **aynı IP**'den tekrar giriş | Öğrenci yeniden bağlanıyordur: eski bağlantı kapatılır, yenisi geçer |
| Aynı numara, **farklı IP**'den giriş | Reddedilir |
| Sınav başladıktan sonra giriş | Sınav hemen gönderilir, kalan süre herkesle aynıdır |
| Teslim etmiş öğrenci tekrar girerse | "Bu sınavı zaten teslim ettiniz" ile reddedilir |
| Egitmen.exe yeniden başlatılıp aynı sınav tekrar dağıtılırsa | Önceki teslimler veritabanından yüklenir; teslim etmiş öğrenci yine reddedilir (DB'de `UNIQUE (exam_id, student_id)`) |
| Sınav dağıtılınca / bitince | `exams.status` → `Yayinda` (+ `started_at`) / `Bitti` |
| Süre doldu / eğitmen bitirdi | Öğrenci ekranları cevapları otomatik gönderir; `Protocol.SubmitGracePeriod` (15 sn) boyunca kabul edilir, sonra reddedilir |
| Kalan süre | Sunucu `ExamPayload.RemainingSeconds` gönderir; öğrenci bilgisayarının saati yanlış olsa da süre doğru biter |
| Teslim sırasında bağlantı kopmuşsa | İstemci aynı bilgilerle otomatik yeniden giriş yapıp gönderir |

**Ortak protokol** (`Shared/Protocol/Protocol.cs`): Her mesaj tek satır JSON'dur. Mesaj türleri `MessageType` enum'unda tanımlı, gönderme/okuma `MessageStream` ile yapılır.

Ağdan gelen olayları arayüze iletirken `Application.Current.Dispatcher.Invoke` kullanın.

## Yayınlama (demo günü gerekirse)
```
dotnet publish src/SinavSistemi.Egitmen -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/SinavSistemi.Ogrenci -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
Kendi bilgisayarlarınızda denerken buna gerek yok.

## Diyagramlar
`docs/diagrams/*.mmd` (Mermaid). mermaid.live'a yapıştırıp PNG/SVG alın.
