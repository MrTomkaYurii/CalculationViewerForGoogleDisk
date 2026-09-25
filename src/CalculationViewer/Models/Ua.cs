using System.Globalization;

namespace CalculationViewer.Models;

/// <summary>Українські форми множини, розміри файлів і дати. Не залежить від ICU: Blazor WASM за замовчуванням працює в invariant-режимі.</summary>
public static class Ua
{
    public static string Plural(int n, string one, string few, string many)
    {
        var a = n % 10;
        var b = n % 100;
        if (a == 1 && b != 11) return one;
        if (a is >= 2 and <= 4 && b is < 12 or > 14) return few;
        return many;
    }

    public static string Count(int n, string one, string few, string many) => $"{Num(n)} {Plural(n, one, few, many)}";

    /// <summary>Число з нерозривним пробілом як роздільником тисяч: 89 536.</summary>
    public static string Num(int n) => n.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

    public static string Folders(int n) => Count(n, "папка", "папки", "папок");
    public static string Subfolders(int n) => Count(n, "підпапка", "підпапки", "підпапок");
    public static string Files(int n) => Count(n, "файл", "файли", "файлів");
    public static string Images(int n) => Count(n, "зображення", "зображення", "зображень");
    public static string Documents(int n) => Count(n, "документ", "документи", "документів");
    public static string Items(int n) => Count(n, "елемент", "елементи", "елементів");
    public static string Collections(int n) => Count(n, "добірка", "добірки", "добірок");

    public static string Size(long bytes)
    {
        const double kb = 1024, mb = kb * 1024, gb = mb * 1024;
        if (bytes < kb) return $"{bytes} Б";
        if (bytes < mb) return $"{Math.Round(bytes / kb)} КБ";
        if (bytes < gb) return Dec(bytes / mb) + " МБ";
        return Dec(bytes / gb) + " ГБ";
    }

    static string Dec(double v) => v.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');

    public static string Date(DateTime d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}

/// <summary>Природне сортування: f2 перед f10, step_2 перед step_10.</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                var sj = j;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x.AsSpan(si, i - si).TrimStart('0');
                var b = y.AsSpan(sj, j - sj).TrimStart('0');
                if (a.Length != b.Length) return a.Length < b.Length ? -1 : 1;
                var c = a.CompareTo(b, StringComparison.Ordinal);
                if (c != 0) return c;
            }
            else
            {
                var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
