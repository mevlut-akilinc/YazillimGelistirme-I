\encoding UTF8
-- =====================================================================
-- 00_setup.sql — uygulama rolü ve veritabanı
--
-- postgres süper kullanıcısıyla BİR KEZ çalıştırılır:
--   psql -U postgres -h localhost -f db/00_setup.sql
--
-- Belge amaçlıdır (bu adım elle yapıldı). 'DEGISTIR' yerine gerçek şifreyi
-- yalnızca çalıştırırken yazın; gerçek şifreyi bu dosyaya kaydedip commit'lemeyin.
-- =====================================================================

-- Uygulamanın bağlandığı rol: giriş yapabilir, süper kullanıcı değildir.
CREATE ROLE sinav_app LOGIN PASSWORD 'DEGISTIR';

-- template0: küme (cluster) locale'i English_United States.1252 olsa da UTF8 veritabanı açabilmek için.
CREATE DATABASE sinav_db
    OWNER sinav_app
    ENCODING 'UTF8'
    TEMPLATE template0;
