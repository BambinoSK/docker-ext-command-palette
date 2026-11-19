using System;
using System.Globalization;

namespace DockerExtension.Helpers;

internal static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < Units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : value.ToString(value < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " " + Units[unit];
    }

    public static string Ago(DateTimeOffset time)
    {
        var span = DateTimeOffset.UtcNow - time;
        return span.TotalSeconds switch
        {
            < 60 => "just now",
            < 3600 => $"{(int)span.TotalMinutes} min ago",
            < 86400 => $"{(int)span.TotalHours} h ago",
            < 86400 * 60 => $"{(int)span.TotalDays} days ago",
            < 86400 * 730 => $"{(int)(span.TotalDays / 30)} months ago",
            _ => $"{(int)(span.TotalDays / 365)} years ago",
        };
    }

    public static string Ago(long unixSeconds) => Ago(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

    public static string Ago(string? isoTime) =>
        DateTimeOffset.TryParse(isoTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) && t.Year > 1
            ? Ago(t)
            : "—";

    public static string ShortId(string id)
    {
        var raw = id.StartsWith("sha256:", StringComparison.Ordinal) ? id[7..] : id;
        return raw.Length > 12 ? raw[..12] : raw;
    }

    public static string Plural(long count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    // Escapes characters that markdown would otherwise interpret in user-controlled text.
    public static string Md(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("\\", "\\\\", StringComparison.Ordinal)
                  .Replace("|", "\\|", StringComparison.Ordinal)
                  .Replace("*", "\\*", StringComparison.Ordinal)
                  .Replace("_", "\\_", StringComparison.Ordinal)
                  .Replace("`", "\\`", StringComparison.Ordinal)
                  .Replace("<", "&lt;", StringComparison.Ordinal);
}
