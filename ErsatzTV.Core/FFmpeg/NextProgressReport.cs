using System.Globalization;

namespace ErsatzTV.Core.FFmpeg;

// latest ffmpeg -progress report published by the ErsatzTV Next channel binary
public record NextProgressReport(
    string ItemId,
    int? ProcessId,
    DateTimeOffset StartedAt,
    DateTimeOffset OutputStart,
    bool IsRealtime,
    string Arguments,
    long Sequence,
    List<string> ProgressLines)
{
    public const string FileName = "ffmpeg-progress.txt";

    public bool IsEnded => ProgressLines.Contains("progress=end");

    public static Option<NextProgressReport> Parse(string contents)
    {
        if (string.IsNullOrWhiteSpace(contents))
        {
            return None;
        }

        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        var progressLines = new List<string>();
        var inProgress = false;

        foreach (string line in contents.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (inProgress)
            {
                if (trimmed.Length > 0)
                {
                    progressLines.Add(trimmed);
                }

                continue;
            }

            int separator = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            string key = trimmed[..separator];
            header[key] = trimmed[(separator + 1)..];
            inProgress = key == "seq";
        }

        if (!header.TryGetValue("item_id", out string itemId)
            || !TryGetLong(header, "seq", out long sequence)
            || !TryGetLong(header, "started_at", out long startedAt)
            || !TryGetLong(header, "output_start", out long outputStart))
        {
            return None;
        }

        int? processId = TryGetLong(header, "pid", out long pid) ? (int)pid : null;

        return new NextProgressReport(
            itemId,
            processId,
            DateTimeOffset.FromUnixTimeMilliseconds(startedAt),
            DateTimeOffset.FromUnixTimeMilliseconds(outputStart),
            header.TryGetValue("realtime", out string realtime) && realtime == "true",
            header.GetValueOrDefault("args", string.Empty),
            sequence,
            progressLines);
    }

    private static bool TryGetLong(Dictionary<string, string> header, string key, out long value)
    {
        value = 0;
        return header.TryGetValue(key, out string text)
               && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }
}
