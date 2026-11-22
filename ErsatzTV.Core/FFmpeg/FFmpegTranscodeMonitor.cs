using System.Diagnostics;

namespace ErsatzTV.Core.FFmpeg;

public sealed class FFmpegTranscodeMonitor : IDisposable
{
    private readonly Lock _sync = new();
    private readonly FFmpegProgress _progress = new();
    private readonly DateTimeOffset _outputStart;
    private readonly int? _mediaItemId;
    private readonly bool _isWorkingAhead;
    private readonly bool _isSlug;
    private readonly DateTimeOffset _startedAt;
    private Process _process;
    private long _processAttachedTimestamp;
    private long _lastCpuTimestamp;
    private TimeSpan _lastCpuTime;
    private double? _cpuPercent;
    private double? _averageCpuPercent;
    private DateTimeOffset? _finishedAt;
    private bool? _succeeded;

    public FFmpegTranscodeMonitor(
        Option<int> mediaItemId,
        bool isWorkingAhead,
        bool isSlug,
        DateTimeOffset outputStart,
        string arguments,
        DateTimeOffset? startedAt = null)
    {
        _mediaItemId = mediaItemId.MatchUnsafe(id => (int?)id, () => null);
        _isWorkingAhead = isWorkingAhead;
        _isSlug = isSlug;
        _outputStart = outputStart;
        _startedAt = startedAt ?? DateTimeOffset.Now;
        Acceleration = FFmpegAccelerationInfo.FromArguments(arguments);
    }

    public FFmpegProgress Progress => _progress;

    public FFmpegAccelerationInfo Acceleration { get; }

    // wall clock time that the transcoded output currently reaches, when it can be determined
    public Option<DateTimeOffset> TranscodedUntil
    {
        get
        {
            FFmpegProgressSnapshot snapshot = _progress.GetSnapshot();
            foreach (double frameRate in Acceleration.OutputFrameRateValue)
            {
                if (snapshot.Frame is { } frames)
                {
                    return _outputStart.AddSeconds(frames / frameRate);
                }
            }

            return None;
        }
    }

    public void AttachProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            lock (_sync)
            {
                _process = process;
                _processAttachedTimestamp = Stopwatch.GetTimestamp();
            }
        }
        catch (Exception)
        {
            // process may have already exited
        }
    }

    public void ParseLine(string line)
    {
        _progress.ParseLine(line);

        if (line.StartsWith("progress=", StringComparison.Ordinal))
        {
            SampleCpu();
        }
    }

    public void Complete(bool? succeeded)
    {
        lock (_sync)
        {
            _finishedAt = DateTimeOffset.Now;
            _succeeded = succeeded;
            _cpuPercent = null;
            ReleaseProcess();
        }
    }

    public FFmpegTranscodePerformance GetPerformance()
    {
        lock (_sync)
        {
            return new FFmpegTranscodePerformance(
                _mediaItemId,
                _isWorkingAhead,
                _isSlug,
                _startedAt,
                _finishedAt,
                _succeeded,
                _progress.GetSnapshot(),
                _cpuPercent,
                _averageCpuPercent,
                Acceleration);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            ReleaseProcess();
        }
    }

    private void SampleCpu()
    {
        lock (_sync)
        {
            if (_process is null)
            {
                return;
            }

            try
            {
                _process.Refresh();
                TimeSpan cpuTime = _process.TotalProcessorTime;
                long now = Stopwatch.GetTimestamp();

                if (_lastCpuTimestamp > 0)
                {
                    double wallSeconds = Stopwatch.GetElapsedTime(_lastCpuTimestamp, now).TotalSeconds;
                    if (wallSeconds > 0)
                    {
                        _cpuPercent = (cpuTime - _lastCpuTime).TotalSeconds / wallSeconds * 100;
                    }
                }

                double totalSeconds = Stopwatch.GetElapsedTime(_processAttachedTimestamp, now).TotalSeconds;
                if (totalSeconds > 0)
                {
                    _averageCpuPercent = cpuTime.TotalSeconds / totalSeconds * 100;
                }

                _lastCpuTime = cpuTime;
                _lastCpuTimestamp = now;
            }
            catch (Exception)
            {
                // process has exited or is inaccessible
                ReleaseProcess();
            }
        }
    }

    private void ReleaseProcess()
    {
        _process?.Dispose();
        _process = null;
    }
}
