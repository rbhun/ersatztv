using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Abstractions;
using CliWrap;
using ErsatzTV.Application.Playouts;
using ErsatzTV.Core;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.FFmpeg;
using ErsatzTV.Core.Interfaces.FFmpeg;
using ErsatzTV.Core.Interfaces.Metadata;
using ErsatzTV.Core.Next.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ErsatzTV.Application.Streaming;

public class NextSessionWorker(
    string channelBinary,
    ChannelConfig channelConfig,
    IFileSystem fileSystem,
    ILocalFileSystem localFileSystem,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<NextSessionWorker> logger)
    : IHlsSessionWorker
{
    private const int MaxPerformanceHistory = 25;

    private readonly TimeSpan _checkpointThreshold = TimeSpan.FromMinutes(5);
    private readonly Lock _performanceSync = new();
    private readonly LinkedList<(string ItemId, FFmpegTranscodePerformance Performance)> _performanceHistory = [];
    private readonly ConcurrentDictionary<string, int?> _mediaItemIds = new(StringComparer.Ordinal);
    private NextTranscode _currentTranscode;

    private readonly SemaphoreSlim _slim = new(1, 1);
    private CancellationTokenSource _cancellationTokenSource;
    private IServiceScope _serviceScope = serviceScopeFactory.CreateScope();
    private bool _disposedValue;
    private string _channelNumber;
    private string _workingDirectory;
    private string _heartbeatFileName;
    private DateTimeOffset _lastTouch;
    private DateTimeOffset _sessionStart;
    private DateTimeOffset _lastCheckpoint;
    private ChannelPlayoutMode _channelPlayoutMode = ChannelPlayoutMode.Continuous;

    private IMediator Mediator => _serviceScope.ServiceProvider.GetRequiredService<IMediator>();

    void IDisposable.Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                lock (_performanceSync)
                {
                    _currentTranscode?.Monitor.Dispose();
                }

                _serviceScope.Dispose();
                _serviceScope = null;
            }

            _disposedValue = true;
        }
    }

    public async Task Cancel(CancellationToken cancellationToken)
    {
        logger.LogInformation("API termination request for HLS session for channel {Channel}", _channelNumber);

        await _slim.WaitAsync(cancellationToken);
        try
        {
            await _cancellationTokenSource.CancelAsync();
        }
        finally
        {
            _slim.Release();
        }
    }

    public void Touch(Option<string> fileName)
    {
        _lastTouch = DateTimeOffset.Now;

        if (!fileSystem.File.Exists(_heartbeatFileName))
        {
            fileSystem.File.WriteAllBytes(_heartbeatFileName, []);
        }
        else
        {
            fileSystem.File.SetLastWriteTimeUtc(_heartbeatFileName, DateTime.UtcNow);
        }
    }

    public Task<Option<TrimPlaylistResult>> TrimPlaylist(
        DateTimeOffset filterBefore,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Legacy path called for next session worker on channel {Number}; this is NOT supported",
            _channelNumber);

        return Task.FromResult(Option<TrimPlaylistResult>.None);
    }

    public void PlayoutUpdated()
    {
        // nothing to do here; channel binary should detect that by itself
    }

    public HlsSessionModel GetModel() => new(_channelNumber, "next", null, _lastTouch, _sessionStart);

    public HlsSessionPerformanceModel GetPerformance()
    {
        lock (_performanceSync)
        {
            RefreshTranscode();

            if (_currentTranscode is null && _performanceHistory.Count == 0)
            {
                return HlsSessionPerformanceModel.NotSupported(_channelNumber, "next", _sessionStart);
            }

            FFmpegTranscodePerformance current = null;
            double? bufferSeconds = null;
            if (_currentTranscode is not null)
            {
                current = WithMediaItem(_currentTranscode.ItemId, _currentTranscode.Monitor.GetPerformance());
                foreach (DateTimeOffset until in _currentTranscode.Monitor.TranscodedUntil)
                {
                    bufferSeconds = (until - DateTimeOffset.Now).TotalSeconds;
                }
            }

            return new HlsSessionPerformanceModel(
                _channelNumber,
                "next",
                true,
                _sessionStart,
                bufferSeconds,
                current,
                _performanceHistory.Map(h => WithMediaItem(h.ItemId, h.Performance)).ToList());
        }
    }

    private void RefreshTranscode()
    {
        if (string.IsNullOrWhiteSpace(_workingDirectory))
        {
            return;
        }

        string contents;
        try
        {
            contents = fileSystem.File.ReadAllText(
                fileSystem.Path.Combine(_workingDirectory, NextProgressReport.FileName));
        }
        catch (Exception)
        {
            // not published yet, or replaced while reading
            return;
        }

        foreach (NextProgressReport report in NextProgressReport.Parse(contents))
        {
            if (_currentTranscode is null
                || _currentTranscode.ItemId != report.ItemId
                || _currentTranscode.StartedAt != report.StartedAt)
            {
                if (_currentTranscode is not null)
                {
                    FinishTranscode(_currentTranscode, succeeded: null);
                }

                var monitor = new FFmpegTranscodeMonitor(
                    None,
                    !report.IsRealtime,
                    false,
                    report.OutputStart,
                    report.Arguments,
                    report.StartedAt);
                if (report.ProcessId is { } processId)
                {
                    monitor.AttachProcess(processId);
                }

                _currentTranscode = new NextTranscode(report.ItemId, report.StartedAt, monitor);
                ResolveMediaItemId(report.ItemId);
            }

            if (report.Sequence > _currentTranscode.Sequence)
            {
                _currentTranscode.Sequence = report.Sequence;
                foreach (string line in report.ProgressLines)
                {
                    _currentTranscode.Monitor.ParseLine(line);
                }

                if (report.IsEnded && !_currentTranscode.IsEnded)
                {
                    _currentTranscode.IsEnded = true;
                    _currentTranscode.Monitor.Complete(true);
                }
            }
        }
    }

    private void FinishTranscode(NextTranscode transcode, bool? succeeded)
    {
        if (!transcode.IsEnded)
        {
            transcode.Monitor.Complete(succeeded);
        }

        transcode.Monitor.Dispose();
        _performanceHistory.AddFirst((transcode.ItemId, transcode.Monitor.GetPerformance()));
        while (_performanceHistory.Count > MaxPerformanceHistory)
        {
            _performanceHistory.RemoveLast();
        }
    }

    private FFmpegTranscodePerformance WithMediaItem(string itemId, FFmpegTranscodePerformance performance) =>
        _mediaItemIds.TryGetValue(itemId, out int? mediaItemId) && mediaItemId is not null
            ? performance with { MediaItemId = mediaItemId }
            : performance;

    private void ResolveMediaItemId(string itemId)
    {
        if (_mediaItemIds.Count > MaxPerformanceHistory * 4)
        {
            _mediaItemIds.Clear();
        }

        if (!_mediaItemIds.TryAdd(itemId, null))
        {
            return;
        }

        // unsaved-{start}-{mediaItemId}, e.g. fallback filler
        if (itemId.StartsWith("unsaved-", StringComparison.Ordinal))
        {
            if (int.TryParse(
                    itemId[(itemId.LastIndexOf('-') + 1)..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int unsavedMediaItemId))
            {
                _mediaItemIds[itemId] = unsavedMediaItemId;
            }

            return;
        }

        if (!int.TryParse(itemId, NumberStyles.None, CultureInfo.InvariantCulture, out int playoutItemId))
        {
            return;
        }

        IMediator mediator = Mediator;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (int mediaItemId in await mediator.Send(new GetMediaItemIdByPlayoutItemId(playoutItemId)))
                {
                    _mediaItemIds[itemId] = mediaItemId;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to find media item for playout item {PlayoutItemId}", playoutItemId);
            }
        });
    }

    private sealed class NextTranscode(string itemId, DateTimeOffset startedAt, FFmpegTranscodeMonitor monitor)
    {
        public string ItemId { get; } = itemId;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public FFmpegTranscodeMonitor Monitor { get; } = monitor;
        public long Sequence { get; set; }
        public bool IsEnded { get; set; }
    }

    public async Task Run(
        string channelNumber,
        Option<TimeSpan> idleTimeout,
        CancellationToken incomingCancellationToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(incomingCancellationToken);
        using var checkpointCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
        Task checkpointLoop = Task.CompletedTask;

        _sessionStart = DateTimeOffset.Now;
        _lastTouch = _sessionStart;
        _lastCheckpoint = _lastTouch;

        try
        {
            _channelNumber = channelNumber;
            _workingDirectory = fileSystem.Path.Combine(FileSystemLayout.TranscodeFolder, _channelNumber);
            _heartbeatFileName = fileSystem.Path.Combine(_workingDirectory, NextContract.HeartbeatFileName);

            Option<PlayoutModeViewModel> maybePlayout = await Mediator.Send(
                new GetPlayoutModeByChannelNumber(_channelNumber),
                _cancellationTokenSource.Token);

            foreach (PlayoutModeViewModel playout in maybePlayout)
            {
                _channelPlayoutMode = playout.PlayoutMode;

                if (_channelPlayoutMode is ChannelPlayoutMode.OnDemand)
                {
                    checkpointLoop = CheckpointLoop(checkpointCts.Token);

                    await Mediator.Send(
                        new TimeShiftOnDemandPlayout(playout.PlayoutId, _sessionStart, true),
                        _cancellationTokenSource.Token);

                    // next reads serialized playout files rather than the database, so ensure it
                    // sees the time-shifted items before starting the channel process
                    await Mediator.Send(
                        new SyncNextPlayout(_channelNumber),
                        _cancellationTokenSource.Token);
                }
            }

            List<string> arguments = ["run", "--output-folder", _workingDirectory, "--number", channelNumber, "-"];

            string defaultOverlayFile = fileSystem.Path.Combine(
                FileSystemLayout.NextChannelConfigOverlaysFolder,
                "default.json");
            if (fileSystem.File.Exists(defaultOverlayFile))
            {
                arguments.Add(defaultOverlayFile);
            }

            string channelOverlayFile = fileSystem.Path.Combine(
                FileSystemLayout.NextChannelConfigOverlaysFolder,
                $"{channelNumber}.json");
            if (fileSystem.File.Exists(channelOverlayFile))
            {
                arguments.Add(channelOverlayFile);
            }

            CommandResult commandResult = await Cli.Wrap(channelBinary)
                .WithArguments(arguments)
                .WithJsonLogs()
                .WithStandardInputPipe(PipeSource.FromString(channelConfig.ToJson()))
                .WithStandardOutputPipe(PipeTarget.ToDelegate(l => NextLogger.LogNextLine(l, logger)))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(l => NextLogger.LogNextLine(l, logger)))
                //.WithStandardOutputPipe(PipeTarget.ToDelegate(progressParser.ParseLine))
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(_cancellationTokenSource.Token);

            if (commandResult.ExitCode != 0)
            {
                await _cancellationTokenSource.CancelAsync();

                logger.LogError(
                    "ErsatzTV Next session for channel {Channel} has terminated unsuccessfully with exit code {ExitCode}",
                    _channelNumber,
                    commandResult.ExitCode);
            }
            else
            {
                logger.LogDebug("ErsatzTV Next session has completed for channel {Channel}", _channelNumber);
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            logger.LogInformation("Terminating ErsatzTV Next session for channel {Channel}", _channelNumber);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error running ErsatzTV Next session");
        }
        finally
        {
            await checkpointCts.CancelAsync();
            try
            {
                await checkpointLoop;
            }
            catch (OperationCanceledException)
            {
                // do nothing
            }

            try
            {
                await UpdateOnDemandCheckpoint(CancellationToken.None);
            }
            catch
            {
                // do nothing
            }

            try
            {
                localFileSystem.EmptyFolder(_workingDirectory);
            }
            catch
            {
                // do nothing
            }
        }
    }

    public async Task WaitForPlaylistSegments(int initialSegmentCount, CancellationToken cancellationToken)
    {
        string readyFileName = fileSystem.Path.Combine(_workingDirectory, NextContract.ReadyFileName);

        logger.LogDebug("Waiting for ErsatzTV Next channel to be ready");
        while (!fileSystem.File.Exists(readyFileName))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private async Task CheckpointLoop(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_checkpointThreshold);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                if (_lastTouch > _lastCheckpoint)
                {
                    await UpdateOnDemandCheckpoint(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update on demand checkpoint for channel {Channel}", _channelNumber);
            }
        }
    }

    private async Task UpdateOnDemandCheckpoint(CancellationToken cancellationToken)
    {
        if (_channelPlayoutMode is ChannelPlayoutMode.OnDemand)
        {
            await Mediator.Send(
                new UpdateOnDemandCheckpoint(_channelNumber, _lastTouch),
                cancellationToken);
        }

        _lastCheckpoint = _lastTouch;
    }
}
