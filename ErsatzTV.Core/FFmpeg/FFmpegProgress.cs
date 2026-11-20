using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ErsatzTV.Core.FFmpeg;

public partial class FFmpegProgress
{
    // the first few progress reports include startup/probing time and are not representative
    private const int WarmupReports = 3;

    private readonly Lock _sync = new();
    private double? _speed;
    private double? _currentSpeed;
    private double? _slowestSpeed;
    private double? _fps;
    private long? _frame;
    private long? _droppedFrames;
    private long? _duplicatedFrames;
    private string _bitrate;
    private TimeSpan? _outTime;
    private TimeSpan? _lastReportOutTime;
    private long _lastReportTimestamp;
    private int _reportCount;

    public Option<double> Speed
    {
        get
        {
            lock (_sync)
            {
                return Optional(_speed);
            }
        }
    }

    public FFmpegProgressSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new FFmpegProgressSnapshot(
                _speed,
                _currentSpeed,
                _slowestSpeed,
                _fps,
                _frame,
                _droppedFrames,
                _duplicatedFrames,
                _bitrate);
        }
    }

    public void ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_sync)
        {
            Match match = FFmpegSpeed().Match(line);
            if (match.Success && TryParseDouble(match.Groups[1].Value, out double speed))
            {
                _speed = speed;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                return;
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();

            switch (key)
            {
                case "frame" when TryParseLong(value, out long frame):
                    _frame = frame;
                    break;
                case "fps" when TryParseDouble(value, out double fps):
                    _fps = fps;
                    break;
                case "drop_frames" when TryParseLong(value, out long dropped):
                    _droppedFrames = dropped;
                    break;
                case "dup_frames" when TryParseLong(value, out long duplicated):
                    _duplicatedFrames = duplicated;
                    break;
                case "bitrate":
                    _bitrate = value == "N/A" ? null : value;
                    break;
                case "out_time_us" when TryParseLong(value, out long outTimeUs) && outTimeUs >= 0:
                    _outTime = TimeSpan.FromTicks(outTimeUs * 10);
                    break;
                case "progress":
                    CompleteReport();
                    break;
            }
        }
    }

    public void LogSpeed(Option<int> mediaItemId, bool isWorkingAhead, string channelNumber, ILogger logger)
    {
        foreach (double speed in Speed)
        {
            if (isWorkingAhead)
            {
                if (speed < 1.0)
                {
                    logger.LogCritical(
                        "Media item {MediaItemId} on channel {Channel} transcoded at {Speed}x (NOT throttled) which is NOT fast enough to support playback",
                        mediaItemId,
                        channelNumber,
                        speed);
                }
                else if (speed <= 1.5)
                {
                    logger.LogWarning(
                        "Media item {MediaItemId} on channel {Channel} transcoded at {Speed}x (NOT throttled) which may not be fast enough to support playback",
                        mediaItemId,
                        channelNumber,
                        speed);
                }
            }
            else if (speed < 0.99)
            {
                logger.LogWarning(
                    "Media item {MediaItemId} on channel {Channel} transcoded at {Speed}x (throttled) which may not be fast enough to support playback",
                    mediaItemId,
                    channelNumber,
                    speed);
            }
        }

        FFmpegProgressSnapshot snapshot = GetSnapshot();
        if (snapshot.DroppedFrames > 0 || snapshot.DuplicatedFrames > 0)
        {
            logger.LogDebug(
                "Media item {MediaItemId} on channel {Channel} dropped {Dropped} and duplicated {Duplicated} frames",
                mediaItemId,
                channelNumber,
                snapshot.DroppedFrames,
                snapshot.DuplicatedFrames);
        }
    }

    private void CompleteReport()
    {
        _reportCount++;
        long now = Stopwatch.GetTimestamp();

        if (_outTime is not { } outTime)
        {
            return;
        }

        if (_lastReportOutTime is { } lastOutTime && _lastReportTimestamp > 0)
        {
            double wallSeconds = Stopwatch.GetElapsedTime(_lastReportTimestamp, now).TotalSeconds;
            if (wallSeconds < 0.25)
            {
                return;
            }

            double currentSpeed = (outTime - lastOutTime).TotalSeconds / wallSeconds;
            _currentSpeed = currentSpeed;

            if (_reportCount > WarmupReports)
            {
                _slowestSpeed = _slowestSpeed is { } slowest ? Math.Min(slowest, currentSpeed) : currentSpeed;
            }
        }

        _lastReportOutTime = outTime;
        _lastReportTimestamp = now;
    }

    private static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static bool TryParseLong(string value, out long result) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    [GeneratedRegex(@"speed=\s*([\d\.]+)x", RegexOptions.IgnoreCase)]
    private static partial Regex FFmpegSpeed();
}

public record FFmpegProgressSnapshot(
    double? Speed,
    double? CurrentSpeed,
    double? SlowestSpeed,
    double? Fps,
    long? Frame,
    long? DroppedFrames,
    long? DuplicatedFrames,
    string Bitrate);
