namespace ErsatzTV.Core.FFmpeg;

public record HlsSessionPerformanceModel(
    string ChannelNumber,
    string State,
    bool IsSupported,
    DateTimeOffset StartedAt,
    double? BufferSeconds,
    FFmpegTranscodePerformance Current,
    List<FFmpegTranscodePerformance> History)
{
    public static HlsSessionPerformanceModel NotSupported(string channelNumber, string state, DateTimeOffset startedAt) =>
        new(channelNumber, state, false, startedAt, null, null, []);
}

public record FFmpegTranscodePerformance(
    int? MediaItemId,
    bool IsWorkingAhead,
    bool IsSlug,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    bool? Succeeded,
    FFmpegProgressSnapshot Progress,
    double? CpuPercent,
    double? AverageCpuPercent,
    FFmpegAccelerationInfo Acceleration);
