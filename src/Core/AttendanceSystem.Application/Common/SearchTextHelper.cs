using System.Globalization;
using System.Text;

namespace AttendanceSystem.Application.Common;

/// <summary>
/// Helpers for accent-insensitive, case-insensitive, multi-token text search.
/// </summary>
public static class SearchTextHelper
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string[] Tokenize(string? query)
    {
        var normalized = Normalize(query);
        if (string.IsNullOrEmpty(normalized))
            return Array.Empty<string>();

        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static bool MatchesAllTokens(string[] tokens, params string?[] fields)
    {
        if (tokens.Length == 0)
            return true;

        var normalizedFields = new string[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            normalizedFields[i] = Normalize(fields[i]);
        }

        foreach (var token in tokens)
        {
            var tokenMatched = false;
            for (var i = 0; i < normalizedFields.Length; i++)
            {
                var field = normalizedFields[i];
                if (!string.IsNullOrEmpty(field) && field.Contains(token, StringComparison.Ordinal))
                {
                    tokenMatched = true;
                    break;
                }
            }

            if (!tokenMatched)
                return false;
        }

        return true;
    }
}
