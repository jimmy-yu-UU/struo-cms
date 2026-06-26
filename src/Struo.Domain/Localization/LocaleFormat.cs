// src/Struo.Domain/Localization/LocaleFormat.cs
using System.Text.RegularExpressions;

namespace Struo.Domain.Localization;

/// <summary>
/// Shared locale-code format guard used by both Application (ItemService) and
/// Infrastructure (SqlSugarItemRepository) to enforce the same whitelist.
/// Acceptable codes: 1–35 ASCII letters, digits, hyphens, or underscores.
/// Examples: "en", "zh-TW", "en_US", "pt-BR".
/// </summary>
public static class LocaleFormat
{
    private static readonly Regex ValidPattern =
        new(@"^[A-Za-z0-9_-]{1,35}$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Returns <c>true</c> when <paramref name="code"/> matches the locale-code whitelist.
    /// </summary>
    public static bool IsValid(string code) =>
        !string.IsNullOrEmpty(code) && ValidPattern.IsMatch(code);
}
