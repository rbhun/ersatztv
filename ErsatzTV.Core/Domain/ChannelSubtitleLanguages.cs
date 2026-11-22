namespace ErsatzTV.Core.Domain;

public static class ChannelSubtitleLanguages
{
    private static readonly char[] Separators = [',', ' ', ';'];

    // ordered; the first language is the primary subtitle track
    public static List<string> Parse(string languages) =>
        (languages ?? string.Empty)
        .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(l => l.ToLowerInvariant())
        .Distinct()
        .ToList();

    public static string Normalize(string languages)
    {
        List<string> parsed = Parse(languages);
        return parsed.Count == 0 ? null : string.Join(",", parsed);
    }
}
