using SinavSistemi.Shared.Models;

namespace SinavSistemi.Shared.Mock;

/// <summary>Arayüzü backend olmadan çalıştırmak için örnek veriler.</summary>
public static class MockData
{
    public static readonly List<Question> Questions = new()
    {
        new Question
        {
            Id = 1, Type = QuestionType.CoktanSecmeli, Category = "Pointer'lar", DefaultPoints = 10,
            Text = "int a = 5; int *p = &a; *p = 10; ifadesinden sonra a'nın değeri nedir?",
            Options =
            {
                new() { Id = 1, Label = "A", Text = "5" },
                new() { Id = 2, Label = "B", Text = "10", IsCorrect = true },
                new() { Id = 3, Label = "C", Text = "a'nın adresi" },
                new() { Id = 4, Label = "D", Text = "Tanımsız davranış" },
            }
        },
        new Question
        {
            Id = 2, Type = QuestionType.CoktanSecmeli, Category = "Diziler", DefaultPoints = 10,
            Text = "C'de int dizi[10]; tanımında geçerli son indeks hangisidir?",
            Options =
            {
                new() { Id = 5, Label = "A", Text = "10" },
                new() { Id = 6, Label = "B", Text = "9", IsCorrect = true },
                new() { Id = 7, Label = "C", Text = "11" },
                new() { Id = 8, Label = "D", Text = "0" },
            }
        },
        new Question
        {
            Id = 3, Type = QuestionType.CoktanSecmeli, Category = "Karmaşıklık", DefaultPoints = 10,
            Text = "İkili arama (binary search) algoritmasının en kötü durum zaman karmaşıklığı nedir?",
            Options =
            {
                new() { Id = 9, Label = "A", Text = "O(n)" },
                new() { Id = 10, Label = "B", Text = "O(log n)", IsCorrect = true },
                new() { Id = 11, Label = "C", Text = "O(n log n)" },
                new() { Id = 12, Label = "D", Text = "O(1)" },
            }
        },
        new Question
        {
            Id = 4, Type = QuestionType.Klasik, Category = "Bellek Yönetimi", DefaultPoints = 15,
            Text = "malloc ile ayrılan belleği serbest bırakan fonksiyonun adını yazınız.",
            ExpectedAnswer = "free"
        },
        new Question
        {
            Id = 5, Type = QuestionType.Klasik, Category = "Diziler", DefaultPoints = 15,
            Text = "Bir karakter dizisinin (string) sonunu belirten karakter nedir?",
            ExpectedAnswer = "\\0"
        },
        new Question
        {
            Id = 6, Type = QuestionType.Kodlama, Category = "Sıralama", DefaultPoints = 40,
            Text = "İlk satırda n, ikinci satırda n tam sayı veriliyor. Sayıları küçükten büyüğe sıralayıp aralarında boşluk olacak şekilde yazdırın.",
            ExpectedTimeComplexity = Complexity.ONLogN,
            ExpectedSpaceComplexity = Complexity.ON,
            StarterCode = "#include <stdio.h>\n\nint main(void) {\n    int n;\n    scanf(\"%d\", &n);\n    // kodunuzu yazın\n    return 0;\n}\n",
            TestCases =
            {
                new() { Id = 1, Input = "5\n3 1 4 1 5", ExpectedOutput = "1 1 3 4 5" },
                new() { Id = 2, Input = "3\n9 8 7", ExpectedOutput = "7 8 9" },
                new() { Id = 3, Input = "1\n42", ExpectedOutput = "42", IsHidden = true },
            }
        },
    };

    public static readonly List<string> Categories = new()
    {
        "Pointer'lar", "Diziler", "Karmaşıklık", "Bellek Yönetimi", "Sıralama", "Döngüler"
    };

    public static Exam SampleExam()
    {
        var exam = new Exam
        {
            Id = 1, Title = "Lab 3 - Pointer ve Diziler Quiz", ClassName = "1-A",
            DurationMinutes = 20, Status = ExamStatus.Yayinda, StartedAt = DateTime.Now
        };
        int order = 1;
        foreach (var q in Questions)
            exam.Questions.Add(new ExamQuestion { Question = q, Order = order++, Points = q.DefaultPoints });
        return exam;
    }

    public static readonly List<(string No, string Name, string Ip)> Students = new()
    {
        ("230201001", "Ayşe Yılmaz", "192.168.1.21"),
        ("230201014", "Mehmet Demir", "192.168.1.22"),
        ("230201027", "Zeynep Kaya", "192.168.1.23"),
        ("230201033", "Emre Şahin", "192.168.1.24"),
        ("230201048", "Elif Aydın", "192.168.1.25"),
        ("230201052", "Burak Çelik", "192.168.1.26"),
    };
}
