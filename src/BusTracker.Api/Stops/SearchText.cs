using System.Globalization;
using System.Text;

namespace BusTracker.Api.Stops;

public static class SearchText
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Dictionary<char, char> Folded = new()
    {
        ['Ç'] = 'C', ['Ğ'] = 'G', ['İ'] = 'I', ['I'] = 'I', ['Ö'] = 'O', ['Ş'] = 'S', ['Ü'] = 'U',
    };

    public static string Normalize(string text)
    {
        var upper = text.Trim().ToUpper(Turkish);
        var sb = new StringBuilder(upper.Length);
        foreach (var c in upper)
            sb.Append(Folded.TryGetValue(c, out var plain) ? plain : char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
