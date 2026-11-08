\encoding UTF8
\set ON_ERROR_STOP on
-- =====================================================================
-- 02_seed.sql — demo verisi (Shared/Mock/MockData.cs'in veritabanı karşılığı)
--
-- Örnek/demo hesaplardır, gerçek şifre içermez. Hepsinin şifresi: 1234
--
--   psql -U sinav_app -d sinav_db -h localhost -f db/02_seed.sql
--
-- Boş şemaya bir kez çalıştırılır (önce 01_schema.sql). İkinci kez çalıştırılırsa
-- UNIQUE kısıtları hata verir ve transaction geri alınır, veri bozulmaz.
--
-- Şifre hash'i pgcrypto ile BCrypt ($2a$, maliyet 10) üretilir; uygulama
-- BCrypt.Net.BCrypt.Verify ile doğrular.
--
-- Identity kolonlarına elle id yazılmaz. FK'ler alt sorguyla veya
-- INSERT ... RETURNING + psql \gset değişkenleriyle (:q1 ... :q6) bağlanır.
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------- kullanıcılar
INSERT INTO users (username, password_hash, full_name, role)
VALUES ('egitmen', crypt('1234', gen_salt('bf', 10)), 'Arş. Gör. Demo Eğitmen', 'Egitmen');

-- MockData.Students: kullanıcı adı = öğrenci numarası
INSERT INTO users (username, password_hash, full_name, role, student_number, class_name)
SELECT v.no, crypt('1234', gen_salt('bf', 10)), v.name, 'Ogrenci', v.no, '1-A'
FROM (VALUES
    ('230201001', 'Ayşe Yılmaz'),
    ('230201014', 'Mehmet Demir'),
    ('230201027', 'Zeynep Kaya'),
    ('230201033', 'Emre Şahin'),
    ('230201048', 'Elif Aydın'),
    ('230201052', 'Burak Çelik')
) AS v(no, name);

-- ---------------------------------------------------------------- kategoriler
INSERT INTO categories (name)
VALUES ('Pointer''lar'), ('Diziler'), ('Karmaşıklık'), ('Bellek Yönetimi'), ('Sıralama'), ('Döngüler');

-- ---------------------------------------------------------------- sorular
-- Her soru tek ifade: soruyu ekle, RETURNING id ile şıkları/testleri ekle,
-- üretilen id'yi \gset ile psql değişkenine al (sınava bağlamak için).

-- 1) Çoktan seçmeli — Pointer'lar
WITH q AS (
    INSERT INTO questions (category_id, type, body, default_points, created_by)
    VALUES ((SELECT id FROM categories WHERE name = 'Pointer''lar'), 'CoktanSecmeli',
            'int a = 5; int *p = &a; *p = 10; ifadesinden sonra a''nın değeri nedir?', 10,
            (SELECT id FROM users WHERE username = 'egitmen'))
    RETURNING id
), o AS (
    INSERT INTO question_options (question_id, label, body, is_correct)
    SELECT q.id, v.label, v.body, v.ok
    FROM q, (VALUES ('A', '5', false),
                    ('B', '10', true),
                    ('C', 'a''nın adresi', false),
                    ('D', 'Tanımsız davranış', false)) AS v(label, body, ok)
)
SELECT id AS q1 FROM q \gset

-- 2) Çoktan seçmeli — Diziler
WITH q AS (
    INSERT INTO questions (category_id, type, body, default_points, created_by)
    VALUES ((SELECT id FROM categories WHERE name = 'Diziler'), 'CoktanSecmeli',
            'C''de int dizi[10]; tanımında geçerli son indeks hangisidir?', 10,
            (SELECT id FROM users WHERE username = 'egitmen'))
    RETURNING id
), o AS (
    INSERT INTO question_options (question_id, label, body, is_correct)
    SELECT q.id, v.label, v.body, v.ok
    FROM q, (VALUES ('A', '10', false),
                    ('B', '9', true),
                    ('C', '11', false),
                    ('D', '0', false)) AS v(label, body, ok)
)
SELECT id AS q2 FROM q \gset

-- 3) Çoktan seçmeli — Karmaşıklık
WITH q AS (
    INSERT INTO questions (category_id, type, body, default_points, created_by)
    VALUES ((SELECT id FROM categories WHERE name = 'Karmaşıklık'), 'CoktanSecmeli',
            'İkili arama (binary search) algoritmasının en kötü durum zaman karmaşıklığı nedir?', 10,
            (SELECT id FROM users WHERE username = 'egitmen'))
    RETURNING id
), o AS (
    INSERT INTO question_options (question_id, label, body, is_correct)
    SELECT q.id, v.label, v.body, v.ok
    FROM q, (VALUES ('A', 'O(n)', false),
                    ('B', 'O(log n)', true),
                    ('C', 'O(n log n)', false),
                    ('D', 'O(1)', false)) AS v(label, body, ok)
)
SELECT id AS q3 FROM q \gset

-- 4) Klasik — Bellek Yönetimi
INSERT INTO questions (category_id, type, body, default_points, expected_answer, created_by)
VALUES ((SELECT id FROM categories WHERE name = 'Bellek Yönetimi'), 'Klasik',
        'malloc ile ayrılan belleği serbest bırakan fonksiyonun adını yazınız.', 15, 'free',
        (SELECT id FROM users WHERE username = 'egitmen'))
RETURNING id AS q4 \gset

-- 5) Klasik — Diziler. Beklenen cevap iki karakter: ters bölü + sıfır (C#'taki "\\0")
INSERT INTO questions (category_id, type, body, default_points, expected_answer, created_by)
VALUES ((SELECT id FROM categories WHERE name = 'Diziler'), 'Klasik',
        'Bir karakter dizisinin (string) sonunu belirten karakter nedir?', 15, '\0',
        (SELECT id FROM users WHERE username = 'egitmen'))
RETURNING id AS q5 \gset

-- 6) Kodlama — Sıralama (E'...' dizgelerinde \n satır sonudur)
WITH q AS (
    INSERT INTO questions (category_id, type, body, default_points,
                           expected_time_complexity, expected_space_complexity, starter_code, created_by)
    VALUES ((SELECT id FROM categories WHERE name = 'Sıralama'), 'Kodlama',
            'İlk satırda n, ikinci satırda n tam sayı veriliyor. Sayıları küçükten büyüğe sıralayıp aralarında boşluk olacak şekilde yazdırın.',
            40, 'ONLogN', 'ON',
            E'#include <stdio.h>\n\nint main(void) {\n    int n;\n    scanf("%d", &n);\n    // kodunuzu yazın\n    return 0;\n}\n',
            (SELECT id FROM users WHERE username = 'egitmen'))
    RETURNING id
), t AS (
    INSERT INTO test_cases (question_id, input, expected_output, is_hidden)
    SELECT q.id, v.input, v.expected, v.hidden
    FROM q, (VALUES (E'5\n3 1 4 1 5', '1 1 3 4 5', false),
                    (E'3\n9 8 7',     '7 8 9',     false),
                    (E'1\n42',        '42',        true)) AS v(input, expected, hidden)
)
SELECT id AS q6 FROM q \gset

-- ---------------------------------------------------------------- örnek sınav
-- MockData.SampleExam(): 6 soru, sırayla, puan = varsayılan puan.
-- Mock'tan farkı: Taslak ve started_at NULL (dağıtılınca Yayinda olur).
WITH e AS (
    INSERT INTO exams (title, class_name, duration_minutes, status, started_at, created_by)
    VALUES ('Lab 3 - Pointer ve Diziler Quiz', '1-A', 20, 'Taslak', NULL,
            (SELECT id FROM users WHERE username = 'egitmen'))
    RETURNING id
)
INSERT INTO exam_questions (exam_id, question_id, order_no, points)
SELECT e.id, v.question_id, v.order_no, v.points
FROM e, (VALUES (:q1, 1, 10),
                (:q2, 2, 10),
                (:q3, 3, 10),
                (:q4, 4, 15),
                (:q5, 5, 15),
                (:q6, 6, 40)) AS v(question_id, order_no, points);

COMMIT;
