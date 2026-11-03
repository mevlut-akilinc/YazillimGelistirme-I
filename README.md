# Lab Sınav Sistemi

Yazılım Geliştirme I (2026-27 Güz). C# / .NET 8 / WPF, MVVM.

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
1. `SinavSistemi.sln` dosyasını Visual Studio 2022'de açın.
2. Solution'a sağ tıklayın → *Configure Startup Projects* → *Multiple startup projects* → Egitmen ve Ogrenci için **Start** seçin. F5 ile ikisi birlikte açılır.
3. Şu an her şey **Mock** servislerle çalışır:
   - Egitmen.exe: `egitmen` / `1234` → Canlı Sınav → Başlat → öğrenciler gelince Sınavı dağıt
   - Ogrenci.exe: herhangi bir numara / `1234`, IP `127.0.0.1` → 2 sn sonra sınav açılır
4. Kodlama sorusunda "Derle ve test et" **gerçek gcc** kullanır. `gcc --version` çalışmıyorsa `ServiceLocator.CodeRunner.GccPath` değerine tam yol verin.

## İki laptopla test
- İki bilgisayarı **telefon hotspot'una** bağlayın. Okul Wi-Fi'ı cihazların birbirini görmesini çoğu zaman engeller.
- Egitmen.exe ilk port açışında Windows Güvenlik Duvarı izin isteyecek: **Özel ağlar**'ı işaretleyin.
- Öğrenci girişinde Canlı Sınav ekranındaki IP'yi kullanın.

## Backend'i bağlamak

**Egitmen.exe** (`Services/IServices.cs` → `ServiceLocator.cs`):
- `IAuthService`: eğitmen girişi, doğrudan PostgreSQL
- `IQuestionService`, `IExamService`: PostgreSQL (Npgsql)
- `IExamServer`: `TcpListener` :5050. Öğrenci LOGIN'ini doğrular, sınavı `Protocol.StripForStudent` ile gönderir, SUBMIT'te süre kontrolü + puanlama + log yapar.

**Ogrenci.exe** (`Services/IExamClient.cs`):
- `IExamClient`: `TcpClient` + `MessageStream`. Login → okuma döngüsü (Exam, ExamEnded) → Submit.

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
