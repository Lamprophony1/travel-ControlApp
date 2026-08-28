using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TravelControl.Api.Services;

public static partial class TextNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) builder.Append(ch);
        return Spaces().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ").ToUpperInvariant();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
