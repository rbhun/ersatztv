using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ErsatzTV.Core.FFmpeg;

public partial record FFmpegAccelerationInfo(
    string HardwareAcceleration,
    string VideoDecoder,
    bool IsHardwareDecode,
    string VideoEncoder,
    bool IsHardwareEncode,
    bool IsVideoCopy,
    string OutputFrameRate,
    List<string> HardwareFilters,
    bool DownloadsFromHardware)
{
    private static readonly string[] HardwareCodecSuffixes =
    [
        "_cuvid", "_nvenc", "_qsv", "_vaapi", "_videotoolbox", "_amf", "_v4l2m2m", "_rkmpp", "_mf", "_vulkan"
    ];

    private static readonly string[] FilterOptions = ["-filter_complex", "-vf", "-filter:v"];

    public static readonly FFmpegAccelerationInfo Unknown =
        new(null, null, false, null, false, false, null, [], false);

    public Option<double> OutputFrameRateValue => ParseFrameRate(OutputFrameRate);

    public static FFmpegAccelerationInfo FromArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return Unknown;
        }

        List<string> tokens = Tokenize(arguments);
        int firstInput = tokens.IndexOf("-i");
        int lastInput = tokens.LastIndexOf("-i");
        if (firstInput < 0)
        {
            return Unknown;
        }

        // global options and video input options both come before the first input, which is always the video
        string hwaccel = LastValue(tokens, "-hwaccel", 0, firstInput);
        if (hwaccel is "none")
        {
            hwaccel = null;
        }

        string decoder = LastValue(tokens, "-c:v", 0, firstInput);
        bool isHardwareDecode = hwaccel is not null || IsHardwareCodec(decoder);

        int outputStart = Math.Min(tokens.Count, lastInput + 2);
        string encoder = LastValue(tokens, "-c:v", outputStart, tokens.Count);
        bool isVideoCopy = encoder is "copy" || encoder is null && LastValue(tokens, "-c", outputStart, tokens.Count) is "copy";
        if (encoder is null && isVideoCopy)
        {
            encoder = "copy";
        }

        string filters = string.Join(
            ';',
            FilterOptions
                .Map(option => LastValue(tokens, option, 0, tokens.Count))
                .Filter(f => f is not null));

        var hardwareFilters = HardwareFilter().Matches(filters)
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        return new FFmpegAccelerationInfo(
            hwaccel,
            decoder,
            isHardwareDecode,
            encoder,
            IsHardwareCodec(encoder),
            isVideoCopy,
            LastValue(tokens, "-r", outputStart, tokens.Count),
            hardwareFilters,
            filters.Contains("hwdownload", StringComparison.Ordinal));
    }

    public static Option<double> ParseFrameRate(string frameRate)
    {
        if (string.IsNullOrWhiteSpace(frameRate))
        {
            return None;
        }

        string[] split = frameRate.Split('/');
        if (split.Length == 2 &&
            double.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double numerator) &&
            double.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator) &&
            denominator > 0)
        {
            return numerator / denominator;
        }

        if (double.TryParse(frameRate, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0)
        {
            return value;
        }

        return None;
    }

    private static bool IsHardwareCodec(string codec) =>
        !string.IsNullOrWhiteSpace(codec) &&
        HardwareCodecSuffixes.Any(suffix => codec.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static string LastValue(List<string> tokens, string option, int start, int end)
    {
        string result = null;
        for (int i = start; i < end - 1; i++)
        {
            if (tokens[i] == option)
            {
                result = tokens[i + 1];
            }
        }

        return result;
    }

    private static List<string> Tokenize(string arguments)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        for (var i = 0; i < arguments.Length; i++)
        {
            char c = arguments[i];
            if (c == '\\' && i + 1 < arguments.Length && arguments[i + 1] == '"')
            {
                current.Append('"');
                hasToken = true;
                i++;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    [GeneratedRegex(@"\b(hwupload\w*|hwmap|[a-z0-9]+_(cuda|npp|vaapi|qsv|vt|videotoolbox|opencl|vulkan|rkrga))\b")]
    private static partial Regex HardwareFilter();
}
