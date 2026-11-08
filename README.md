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
3. Ağ iletişimi **gerçek TCP** ile çalışır. Veritabanı henüz bağlı değil; kullanıcılar, sorular ve teslimler bellekte (mock) tutulur:
   - Egitmen.exe: `egitmen` / `1234` → Canlı Sınav → **Sunucuyu başlat** → öğrenciler bağlanınca **Sınavı dağıt**
   - Ogrenci.exe: herhangi bir numara / `1234`, sunucu IP'si `127.0.0.1` (aynı bilgisayar) veya Canlı Sınav ekranında yazan IP
   - Öğrenciler teslim edince puanlar **Sonuçlar** ekranına düşer.
   - Ağ olmadan yalnızca arayüzü denemek için iki `ServiceLocator.cs` dosyasında `MockExamServer` / `MockExamClient` satırını açın.
4. Kodlama sorusunda "Derle ve test et" **gerçek gcc** kullanır. `gcc --version` çalışmıyorsa `ServiceLocator.CodeRunner.GccPath` değerine tam yol verin.

## İki laptopla test
- İki bilgisayarı **telefon hotspot'una** bağlayın. Okul Wi-Fi'ı cihazların birbirini görmesini çoğu zaman engeller.
- Egitmen.exe ilk port açışında Windows Güvenlik Duvarı izin isteyecek: **Özel ağlar**'ı işaretleyin.
- Öğrenci girişinde Canlı Sınav ekranındaki IP'yi kullanın.

## Backend'i bağlamak

**Egitmen.exe** (`Services/IServices.cs` → `ServiceLocator.cs`):
- `IAuthService`: eğitmen girişi, doğrudan PostgreSQL *(yapılacak)*
- `IStudentAuthenticator`: öğrenci numarası + şifre kontrolü, PostgreSQL + BCrypt *(yapılacak, şimdilik mock)*
- `IQuestionService`, `IExamService`: PostgreSQL (Npgsql) *(yapılacak)*
- `IExamServer` → **`Services/Network/TcpExamServer.cs`** ✔: `TcpListener` :5050. Öğrenci LOGIN'ini `IStudentAuthenticator` ile doğrular,
  sınavı `Protocol.StripForStudent` ile gönderir, SUBMIT'te süre kontrolü + `Grader` + `SaveSubmissionAsync` yapar.

**Ogrenci.exe** → **`Services/TcpExamClient.cs`** ✔: `TcpClient` + `MessageStream`. Login → okuma döngüsü (Exam, ExamEnded, SubmitOk/Rejected) → Submit.

### Sunucu kuralları
| Durum | Davranış |
|---|---|
| Aynı numara, **aynı IP**'den tekrar giriş | Öğrenci yeniden bağlanıyordur: eski bağlantı kapatılır, yenisi geçer |
| Aynı numara, **farklı IP**'den giriş | Reddedilir |
| Sınav başladıktan sonra giriş | Sınav hemen gönderilir, kalan süre herkesle aynıdır |
| Teslim etmiş öğrenci tekrar girerse | "Bu sınavı zaten teslim ettiniz" ile reddedilir |
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
