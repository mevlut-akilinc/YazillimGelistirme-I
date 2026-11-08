\encoding UTF8
\set ON_ERROR_STOP on
-- =====================================================================
-- 01_schema.sql — tablolar, kısıtlar, indeksler
--
-- !!! GELİŞTİRME İÇİNDİR, TÜM VERİYİ SİLER !!!
-- Var olan tabloları DROP edip yeniden oluşturur.
--
--   psql -U sinav_app -d sinav_db -h localhost -f db/01_schema.sql
--
-- Notlar:
--  * Enum kolonları VARCHAR + CHECK; değerler C# enum adlarıyla birebir aynıdır
--    (yazarken enum.ToString(), okurken Enum.Parse).
--  * Zamanlar TIMESTAMP (zaman dilimsiz): kod DateTime.Now (yerel saat) kullanıyor.
--  * C#'ta double olan ağırlık/puanlar DOUBLE PRECISION.
--  * FK indeksleri: bileşik PK/UNIQUE'in İLK kolonu olan FK'ler o indeksi kullanır,
--    ayrıca indeks açılmadı (yorumla belirtildi). Diğer tüm FK'lere indeks var.
-- =====================================================================

-- Bağımlılık sırasının tersine (önce başkasına referans verenler)
DROP TABLE IF EXISTS code_results    CASCADE;
DROP TABLE IF EXISTS answers         CASCADE;
DROP TABLE IF EXISTS submissions     CASCADE;
DROP TABLE IF EXISTS exam_questions  CASCADE;
DROP TABLE IF EXISTS logs            CASCADE;
DROP TABLE IF EXISTS exams           CASCADE;
DROP TABLE IF EXISTS test_cases      CASCADE;
DROP TABLE IF EXISTS question_options CASCADE;
DROP TABLE IF EXISTS questions       CASCADE;
DROP TABLE IF EXISTS categories      CASCADE;
DROP TABLE IF EXISTS users           CASCADE;

-- Seed'deki crypt()/gen_salt() için. PG13+ "trusted" eklenti: veritabanı sahibi kurabilir.
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ---------------------------------------------------------------- users
CREATE TABLE users (
    id             INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username       VARCHAR(50)  NOT NULL UNIQUE,
    password_hash  VARCHAR(100) NOT NULL,            -- BCrypt ($2a$/$2b$...)
    full_name      VARCHAR(100) NOT NULL,
    role           VARCHAR(10)  NOT NULL CHECK (role IN ('Egitmen', 'Ogrenci')),
    student_number VARCHAR(20)  UNIQUE,
    class_name     VARCHAR(20),
    created_at     TIMESTAMP    NOT NULL DEFAULT now(),
    CONSTRAINT chk_ogrenci_numarasi CHECK (role <> 'Ogrenci' OR student_number IS NOT NULL)
);

-- ---------------------------------------------------------------- categories
CREATE TABLE categories (
    id   INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name VARCHAR(50) NOT NULL UNIQUE
);

-- ---------------------------------------------------------------- questions
CREATE TABLE questions (
    id                        INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    category_id               INT NOT NULL REFERENCES categories ON DELETE RESTRICT,
    type                      VARCHAR(15) NOT NULL CHECK (type IN ('CoktanSecmeli', 'Klasik', 'Kodlama')),
    body                      TEXT NOT NULL,
    default_points            INT  NOT NULL CHECK (default_points > 0),
    expected_answer           TEXT,
    starter_code              TEXT,
    expected_time_complexity  VARCHAR(10)
        CHECK (expected_time_complexity  IN ('O1', 'OLogN', 'ON', 'ONLogN', 'ON2', 'ON3', 'O2N')),
    expected_space_complexity VARCHAR(10)
        CHECK (expected_space_complexity IN ('O1', 'OLogN', 'ON', 'ONLogN', 'ON2', 'ON3', 'O2N')),
    created_by                INT REFERENCES users,
    created_at                TIMESTAMP NOT NULL DEFAULT now(),
    -- Soft delete: eski sınavlar ve cevaplar bu soruya bağlı kalabilsin diye fiziksel silme yok
    is_deleted                BOOLEAN NOT NULL DEFAULT false
);
CREATE INDEX ix_questions_category_id ON questions (category_id);
CREATE INDEX ix_questions_created_by  ON questions (created_by);

-- ---------------------------------------------------------------- question_options
CREATE TABLE question_options (
    id          INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    question_id INT NOT NULL REFERENCES questions ON DELETE CASCADE,
    label       CHAR(1) NOT NULL CHECK (label IN ('A', 'B', 'C', 'D', 'E')),
    body        TEXT NOT NULL,
    is_correct  BOOLEAN NOT NULL DEFAULT false,
    -- DEFERRABLE: düzenlemede bir şık silinince harfler kayar (C→B gibi);
    -- kontrol transaction sonunda yapılır, ara adımlarda çakışma hatası olmaz.
    CONSTRAINT uq_question_options_label UNIQUE (question_id, label) DEFERRABLE INITIALLY DEFERRED
);
-- question_id: uq_question_options_label indeksinin ilk kolonu, ayrı indeks gerekmez.

-- ---------------------------------------------------------------- test_cases
CREATE TABLE test_cases (
    id              INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    question_id     INT NOT NULL REFERENCES questions ON DELETE CASCADE,
    input           TEXT NOT NULL,
    expected_output TEXT NOT NULL,
    is_hidden       BOOLEAN NOT NULL DEFAULT false
);
CREATE INDEX ix_test_cases_question_id ON test_cases (question_id);

-- ---------------------------------------------------------------- exams
CREATE TABLE exams (
    id                INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    title             VARCHAR(150) NOT NULL,
    class_name        VARCHAR(20),
    duration_minutes  INT NOT NULL CHECK (duration_minutes > 0),
    status            VARCHAR(10) NOT NULL DEFAULT 'Taslak' CHECK (status IN ('Taslak', 'Yayinda', 'Bitti')),
    test_case_weight  DOUBLE PRECISION NOT NULL DEFAULT 0.7,
    complexity_weight DOUBLE PRECISION NOT NULL DEFAULT 0.3,
    started_at        TIMESTAMP,
    created_by        INT REFERENCES users,
    created_at        TIMESTAMP NOT NULL DEFAULT now(),
    -- Kodlama ağırlıklarının toplamı 1 (double yuvarlama payıyla)
    CONSTRAINT chk_exams_agirlik_toplami CHECK (abs(test_case_weight + complexity_weight - 1) < 0.001)
);
CREATE INDEX ix_exams_created_by ON exams (created_by);

-- ---------------------------------------------------------------- exam_questions
CREATE TABLE exam_questions (
    exam_id     INT NOT NULL REFERENCES exams ON DELETE CASCADE,
    question_id INT NOT NULL REFERENCES questions ON DELETE RESTRICT,
    order_no    INT NOT NULL,
    points      INT NOT NULL CHECK (points > 0),
    PRIMARY KEY (exam_id, question_id)
);
-- exam_id: PK'nin ilk kolonu, ayrı indeks gerekmez.
CREATE INDEX ix_exam_questions_question_id ON exam_questions (question_id);

-- ---------------------------------------------------------------- submissions
CREATE TABLE submissions (
    id            INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    exam_id       INT NOT NULL REFERENCES exams,
    student_id    INT NOT NULL REFERENCES users,
    submitted_at  TIMESTAMP NOT NULL DEFAULT now(),
    client_ip     VARCHAR(45),                       -- IPv6 için 45 karakter
    -- Grader sonucu (Submission modeliyle birebir)
    earned_points INT,
    max_points    INT,
    correct_count INT,
    graded_count  INT,
    score         DOUBLE PRECISION,                  -- 100 üzerinden
    -- Bir öğrenci bir sınavı bir kez teslim eder (çift teslimde 23505 hatası)
    CONSTRAINT uq_submissions_exam_student UNIQUE (exam_id, student_id)
);
-- exam_id: uq_submissions_exam_student indeksinin ilk kolonu, ayrı indeks gerekmez.
CREATE INDEX ix_submissions_student_id ON submissions (student_id);

-- ---------------------------------------------------------------- answers
CREATE TABLE answers (
    id                 INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    submission_id      INT NOT NULL REFERENCES submissions ON DELETE CASCADE,
    question_id        INT NOT NULL REFERENCES questions ON DELETE RESTRICT,
    selected_option_id INT REFERENCES question_options ON DELETE SET NULL,
    answer_text        TEXT,
    code               TEXT,
    is_correct         BOOLEAN,
    earned_points      INT,
    is_pending         BOOLEAN NOT NULL DEFAULT false,   -- kodlama: finalde puanlanacak
    CONSTRAINT uq_answers_submission_question UNIQUE (submission_id, question_id)
);
-- submission_id: uq_answers_submission_question indeksinin ilk kolonu, ayrı indeks gerekmez.
CREATE INDEX ix_answers_question_id        ON answers (question_id);
CREATE INDEX ix_answers_selected_option_id ON answers (selected_option_id);

-- ---------------------------------------------------------------- code_results (final)
CREATE TABLE code_results (
    answer_id       INT PRIMARY KEY REFERENCES answers ON DELETE CASCADE,
    compiled        BOOLEAN,
    compiler_output TEXT,
    passed_tests    INT,
    total_tests     INT,
    measured_time   VARCHAR(10),
    measured_space  VARCHAR(10)
);
-- answer_id: PK, ayrı indeks gerekmez.

-- ---------------------------------------------------------------- logs
CREATE TABLE logs (
    id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    exam_id    INT REFERENCES exams ON DELETE SET NULL,   -- sınav dışı loglarda NULL
    created_at TIMESTAMP NOT NULL DEFAULT now(),
    level      VARCHAR(10) NOT NULL CHECK (level IN ('Bilgi', 'Uyari', 'Hata')),
    source     VARCHAR(50) NOT NULL,
    message    TEXT NOT NULL
);
CREATE INDEX ix_logs_exam_id ON logs (exam_id);
