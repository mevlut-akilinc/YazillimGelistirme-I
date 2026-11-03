using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Shared.Converters;

/// <summary>true → Visible. ConverterParameter="Invert" ile tersi.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (parameter as string == "Invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>null değilse Visible. ConverterParameter="Invert" ile tersi.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasValue = value is not null && !(value is string s && s.Length == 0);
        if (parameter as string == "Invert") hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Değer == ConverterParameter ise true (menüde aktif sayfayı işaretlemek için).</summary>
public class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Enum değerlerini Türkçe etikete çevirir.</summary>
public class EnumDisplayConverter : IValueConverter
{
    public static string ToDisplay(object? value) => value switch
    {
        QuestionType.CoktanSecmeli => "Çoktan Seçmeli",
        QuestionType.Klasik => "Klasik",
        QuestionType.Kodlama => "Kodlama (C)",

        Complexity.O1 => "O(1)",
        Complexity.OLogN => "O(log n)",
        Complexity.ON => "O(n)",
        Complexity.ONLogN => "O(n log n)",
        Complexity.ON2 => "O(n²)",
        Complexity.ON3 => "O(n³)",
        Complexity.O2N => "O(2ⁿ)",

        ClientState.Bagli => "Bağlı",
        ClientState.SinavDa => "Sınavda",
        ClientState.TeslimEtti => "Teslim etti",
        ClientState.BaglantiKoptu => "Bağlantı koptu",

        ExamStatus.Taslak => "Taslak",
        ExamStatus.Yayinda => "Yayında",
        ExamStatus.Bitti => "Bitti",

        LogLevel.Bilgi => "BİLGİ",
        LogLevel.Uyari => "UYARI",
        LogLevel.Hata => "HATA",

        null => "—",
        _ => value.ToString() ?? ""
    };

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => ToDisplay(value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Durum enum'larını renk fırçasına çevirir (rozetler için).</summary>
public class StatusBrushConverter : IValueConverter
{
    private static Brush R(string key) => (Brush)Application.Current.Resources[key];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ClientState.Bagli => R("InfoBrush"),
        ClientState.SinavDa => R("AccentBrush"),
        ClientState.TeslimEtti => R("SuccessBrush"),
        ClientState.BaglantiKoptu => R("DangerBrush"),
        LogLevel.Bilgi => R("InfoBrush"),
        LogLevel.Uyari => R("WarningBrush"),
        LogLevel.Hata => R("DangerBrush"),
        ExamStatus.Yayinda => R("AccentBrush"),
        ExamStatus.Bitti => R("MutedBrush"),
        QuestionType.CoktanSecmeli => R("InfoBrush"),
        QuestionType.Klasik => R("WarningBrush"),
        QuestionType.Kodlama => R("AccentBrush"),
        _ => R("MutedBrush")
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
