# Kurulum: PostgreSQL ve veritabanı

Egitmen.exe'nin veritabanıyla çalışması için gereken adımlar (Windows 11). Ogrenci.exe veritabanı kullanmaz; öğrenci bilgisayarlarına bu kurulum **yapılmaz**.

> Veritabanı olmadan arayüzü denemek için bu belgeye gerek yok: `src/SinavSistemi.Egitmen/appsettings.json` içinde `"UseDatabase": false` yapın (mock veriler kullanılır).

İçindekiler:
1. [PostgreSQL 18 kurulumu](#1-postgresql-18-kurulumu)
2. [Yarım kalmış kurulumu temizleme](#2-yarım-kalmış-kurulumu-temizleme)
3. [PATH](#3-path)
4. [Servisin otomatik başlaması](#4-servisin-otomatik-başlaması)
5. [Sadece bu bilgisayardan bağlantı (listen_addresses)](#5-sadece-bu-bilgisayardan-bağlantı-listen_addresses)
6. [Veritabanını oluşturma (db/ betikleri)](#6-veritabanını-oluşturma-db-betikleri)
7. [Uygulamanın bağlantı ayarı (appsettings.Local.json)](#7-uygulamanın-bağlantı-ayarı-appsettingslocaljson)
8. [postgres şifresini unuttum](#8-postgres-şifresini-unuttum)
9. [Sık karşılaşılan hatalar](#9-sık-karşılaşılan-hatalar)

---

## 1. PostgreSQL 18 kurulumu

1. EDB (EnterpriseDB) Windows installer'ını indirin: postgresql.org → Download → Windows → PostgreSQL **18**.
2. Kurulum adımları:
   - **Components:** PostgreSQL Server ve Command Line Tools yeterli. **Stack Builder'ın işaretini kaldırın** (gerekmiyor, kurulum sonunda ek indirme yapmaya çalışır).
   - **Password:** `postgres` süper kullanıcısının şifresi. Bir yere not edin (unutursanız → [bölüm 8](#8-postgres-şifresini-unuttum)).
   - **Port:** `5432` (varsayılan).
   - **Locale:** **English, United States** seçin.

> **Neden İngilizce locale?** Windows dili Türkçe olduğunda varsayılan locale `Turkish_Türkiye.1254` olur ve kurulum veritabanı kümesini oluştururken şu hatayla yarıda kalır:
>
> ```
> initdb: error: locale name "Turkish_Türkiye.1254" contains non-ASCII characters
> ```
>
> Locale yalnızca sıralama/biçim kurallarını belirler. Veritabanı yine **UTF8** açılır (bkz. `db/00_setup.sql`, `TEMPLATE template0`), Türkçe karakterler sorunsuz saklanır.

Kurulum hata verip yarıda kaldıysa önce [bölüm 2](#2-yarım-kalmış-kurulumu-temizleme)'yi uygulayın, sonra tekrar kurun.

## 2. Yarım kalmış kurulumu temizleme

Yarıda kalan bir kurulum, yeniden kurmaya çalışınca "zaten kurulu" gibi davranabilir. Sırayla:

1. **Kaldırma:** Ayarlar → Uygulamalar → *PostgreSQL 18* → Kaldır. (Listede yoksa `C:\Program Files\PostgreSQL\18\uninstall-postgresql.exe` varsa onu çalıştırın.)
2. **Servis kaldıysa** (Yönetici olarak açılmış Komut İstemi):
   ```bat
   sc query postgresql-x64-18
   sc delete postgresql-x64-18
   ```
3. **Klasör:** `C:\Program Files\PostgreSQL\18` hâlâ duruyorsa silin. (İçindeki `data` klasöründe korumak istediğiniz veri varsa önce yedekleyin.)
4. **Kayıt defteri:** `regedit` → önce *Dosya → Dışarı aktar* ile yedek alın. Sonra şu anahtarlar varsa silin:
   - `HKEY_LOCAL_MACHINE\SOFTWARE\PostgreSQL`
   - `HKEY_LOCAL_MACHINE\SOFTWARE\PostgreSQL Global Development Group`
   - `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PostgreSQL 18`
5. Bilgisayarı yeniden başlatıp [bölüm 1](#1-postgresql-18-kurulumu)'den tekrar kurun.

## 3. PATH

`psql` komutunun her klasörden çalışması için:

1. Başlat → "ortam değişkenleri" → *Sistem ortam değişkenlerini düzenleyin* → **Ortam Değişkenleri**.
2. *Sistem değişkenleri* → `Path` → Düzenle → Yeni → `C:\Program Files\PostgreSQL\18\bin`
3. **Yeni** bir Komut İstemi açıp deneyin:
   ```bat
   psql --version
   ```
   Beklenen: `psql (PostgreSQL) 18.x`

## 4. Servisin otomatik başlaması

Installer servisi zaten otomatik başlayacak şekilde kurar. Kontrol için `Win + R` → `services.msc` → **postgresql-x64-18**:

- *Durum:* Çalışıyor
- *Başlangıç türü:* Otomatik

Değilse çift tıklayıp *Başlangıç türü = Otomatik* yapın ve **Başlat**'a basın. Komut satırından (Yönetici):
```bat
sc config postgresql-x64-18 start= auto
net start postgresql-x64-18
```
(`start=` sonrasındaki boşluk zorunludur.)

## 5. Sadece bu bilgisayardan bağlantı (listen_addresses)

Veritabanına yalnızca aynı bilgisayardaki Egitmen.exe bağlanır; ağdan erişime gerek yok.

1. Not Defteri'ni **Yönetici olarak** açıp şu dosyayı açın: `C:\Program Files\PostgreSQL\18\data\postgresql.conf`
2. Şu satırı bulun ve böyle olduğundan emin olun (başında `#` olmasın):
   ```
   listen_addresses = 'localhost'
   ```
3. Servisi yeniden başlatın (Yönetici Komut İstemi):
   ```bat
   net stop postgresql-x64-18
   net start postgresql-x64-18
   ```

## 6. Veritabanını oluşturma (db/ betikleri)

Betikler **sırayla** çalıştırılır. Komutlar repo kökünde, normal bir Komut İstemi'nde çalıştırılır:

```bat
chcp 1254
cd /d "<repo klasörü>"
```

> **`chcp 1254` neden?** Komut İstemi varsayılan olarak eski bir kod sayfası kullanır. psql'in mesajları ve sorgu sonuçlarındaki ş, ğ, ı gibi harfler bozuk görünür. Betik dosyalarının kendisi UTF-8'dir ve her birinin ilk satırındaki `\encoding UTF8` sayesinde kod sayfasından bağımsız doğru okunur.

| Sıra | Dosya | Kim çalıştırır | Ne yapar |
|---|---|---|---|
| 1 | `db/00_setup.sql` | `postgres` (bir kez) | `sinav_app` rolünü ve `sinav_db` veritabanını oluşturur |
| 2 | `db/01_schema.sql` | `sinav_app` | Tabloları **siler ve yeniden oluşturur** (geliştirme içindir, tüm veriyi siler) |
| 3 | `db/02_seed.sql` | `sinav_app` | Demo verisi: eğitmen, 6 öğrenci, kategoriler, 6 soru, örnek sınav |

### 6.1 Rol ve veritabanı

```bat
psql -U postgres -h localhost -f db/00_setup.sql
```
Betik `sinav_app` şifresini yer tutucu olarak `DEGISTIR` yapar. Gerçek şifreyi **dosyaya yazmadan** hemen değiştirin:
```bat
psql -U postgres -h localhost
```
```
postgres=# \password sinav_app
Enter new password for user "sinav_app":
Enter it again:
postgres=# \q
```

### 6.2 Şema ve demo verisi

```bat
psql -U sinav_app -d sinav_db -h localhost -f db/01_schema.sql
psql -U sinav_app -d sinav_db -h localhost -f db/02_seed.sql
```
- Her komut `sinav_app` şifresini sorar (yazarken ekranda görünmez).
- `01` çalışırken çıkan `NOTICE: table "..." does not exist, skipping` mesajları normaldir.
- `02`'nin son satırı `COMMIT` olmalıdır. `02` boş şemaya **bir kez** çalışır. Sıfırdan başlamak için önce `01`'i, sonra `02`'yi tekrar çalıştırın.

`01` çalışırken `permission denied to create extension "pgcrypto"` hatası alırsanız eklentiyi bir kez süper kullanıcıyla kurup `01`'i tekrar çalıştırın:
```bat
psql -U postgres -d sinav_db -h localhost -c "CREATE EXTENSION IF NOT EXISTS pgcrypto;"
```

### 6.3 Kontrol

```bat
psql -U sinav_app -d sinav_db -h localhost
```
```sql
\dt
SELECT (SELECT count(*) FROM users) AS users, (SELECT count(*) FROM questions) AS questions;
\q
```
Beklenen: 11 tablo, `7 | 6`.

Demo hesaplar (hepsinin şifresi `1234`, gerçek şifre değildir):

| Rol | Kullanıcı |
|---|---|
| Eğitmen (Egitmen.exe) | `egitmen` |
| Öğrenci (Ogrenci.exe) | `230201001`, `230201014`, `230201027`, `230201033`, `230201048`, `230201052` |

## 7. Uygulamanın bağlantı ayarı (appsettings.Local.json)

`src/SinavSistemi.Egitmen/appsettings.json` repoya girer ve **şifre içermez** (`Password=DEGISTIR`). Gerçek şifre, git'e girmeyen `appsettings.Local.json` dosyasına yazılır ve ilk dosyadaki değerleri ezer.

Repo kökünde:
```bat
copy src\SinavSistemi.Egitmen\appsettings.Local.example.json src\SinavSistemi.Egitmen\appsettings.Local.json
notepad src\SinavSistemi.Egitmen\appsettings.Local.json
```
`BURAYA_GERCEK_SIFRE` yerine `sinav_app` şifresini yazıp kaydedin, sonra `dotnet build`. Derleme, dosyayı exe'nin yanına kopyalar.

- `git status` çalıştırınca `appsettings.Local.json` **görünmemelidir** (`.gitignore`'da).
- Ayar anahtarları:

| Anahtar | Anlamı |
|---|---|
| `UseDatabase` | `true`: PostgreSQL servisleri. `false`: bellek içi (mock) servisler |
| `ConnectionStrings:SinavDb` | Npgsql bağlantı dizesi |
| `LogDirectory` | Log dosyalarının klasörü. Göreli ise exe'nin yanında (varsayılan `logs`) |

`UseDatabase = true` iken bağlantı açılamazsa Egitmen.exe sessizce mock'a geçmez: hatayı gösterip kapanır. Mesajdaki *Ayrıntı* satırı sebebi söyler (bkz. [bölüm 9](#9-sık-karşılaşılan-hatalar)).

## 8. postgres şifresini unuttum

Geçici olarak şifresiz yerel girişe izin verip şifreyi yeniden belirleriz.

> ⚠️ **Bu adımın sonunda ayarı mutlaka geri alın.** `trust` açık kaldığı sürece bu bilgisayardaki herkes şifresiz süper kullanıcı olur.

1. Not Defteri'ni **Yönetici olarak** açın → `C:\Program Files\PostgreSQL\18\data\pg_hba.conf`
2. Dosyanın sonundaki şu satırlarda `scram-sha-256` yazan yeri `trust` yapın:
   ```
   host    all    all    127.0.0.1/32    trust
   host    all    all    ::1/128         trust
   ```
3. Servisi yeniden başlatın (Yönetici):
   ```bat
   net stop postgresql-x64-18
   net start postgresql-x64-18
   ```
4. Şifre sormadan girip yeni şifreyi belirleyin:
   ```bat
   psql -U postgres -h localhost
   ```
   ```
   postgres=# \password postgres
   postgres=# \q
   ```
5. **Geri alın:** `pg_hba.conf`'ta iki satırı tekrar `scram-sha-256` yapın, kaydedin ve servisi yeniden başlatın.
6. Kontrol: `psql -U postgres -h localhost` artık şifre **sormalı**.

## 9. Sık karşılaşılan hatalar

| Belirti | Sebep / Çözüm |
|---|---|
| `'psql' is not recognized...` | PATH ayarlı değil ([bölüm 3](#3-path)) veya Komut İstemi PATH değişikliğinden önce açılmış (yeni pencere açın). |
| `connection refused` / `could not connect to server` | Servis çalışmıyor ([bölüm 4](#4-servisin-otomatik-başlaması)). |
| `password authentication failed for user "sinav_app"` (Egitmen.exe açılışında *Ayrıntı: 28P01*) | `appsettings.Local.json` yok, şifre yanlış veya dosya kaydedildikten sonra `dotnet build` yapılmamış. |
| `database "sinav_db" does not exist` | `db/00_setup.sql` çalıştırılmamış ([bölüm 6.1](#61-rol-ve-veritabanı)). |
| `relation "users" does not exist` | `db/01_schema.sql` çalıştırılmamış. |
| `02_seed.sql`'de `duplicate key value violates unique constraint` | Seed zaten yüklü. Sıfırlamak için `01` + `02`. |
| psql çıktısında Türkçe harfler bozuk | Önce `chcp 1254`. |
| Ogrenci.exe: "Eğitmen bilgisayarına bağlanılamadı" | Egitmen.exe'de **Canlı Sınav → Sunucuyu başlat**'a basılmamış, IP yanlış veya güvenlik duvarı engelliyor (veritabanıyla ilgisi yoktur). |
