using ErsatzTV.Core.FFmpeg;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Core.Tests.FFmpeg;

[TestFixture]
public class FFmpegProgressTests
{
    [Test]
    public void Parses_Progress_Block()
    {
        var progress = new FFmpegProgress();
        string[] lines =
        [
            "frame=1440",
            "fps=29.97",
            "stream_0_0_q=-1.0",
            "bitrate=2048.3kbits/s",
            "total_size=12345678",
            "out_time_us=48048000",
            "out_time_ms=48048000",
            "out_time=00:00:48.048000",
            "dup_frames=12",
            "drop_frames=3",
            "speed=1.01x",
            "progress=continue"
        ];

        foreach (string line in lines)
        {
            progress.ParseLine(line);
        }

        FFmpegProgressSnapshot snapshot = progress.GetSnapshot();
        snapshot.Frame.ShouldBe(1440);
        snapshot.Fps.ShouldBe(29.97);
        snapshot.BitrateKbps.ShouldBe(2048.3);
        snapshot.DuplicatedFrames.ShouldBe(12);
        snapshot.DroppedFrames.ShouldBe(3);
        snapshot.Speed.ShouldBe(1.01);
        progress.Speed.IfNone(0).ShouldBe(1.01);
    }

    [Test]
    public void Ignores_Unavailable_Values()
    {
        var progress = new FFmpegProgress();
        progress.ParseLine("bitrate=N/A");
        progress.ParseLine("out_time_us=N/A");
        progress.ParseLine("speed=N/A");
        progress.ParseLine("progress=continue");

        FFmpegProgressSnapshot snapshot = progress.GetSnapshot();
        snapshot.BitrateKbps.ShouldBeNull();
        snapshot.Speed.ShouldBeNull();
        snapshot.CurrentSpeed.ShouldBeNull();
    }

    [Test]
    public async Task Calculates_Current_Bitrate_Between_Reports()
    {
        var progress = new FFmpegProgress();
        progress.ParseLine("total_size=1000000");
        progress.ParseLine("out_time_us=10000000");
        progress.ParseLine("progress=continue");

        await Task.Delay(300);

        // 750,000 bytes over 1 second of output = 6,000 kbps
        progress.ParseLine("total_size=1750000");
        progress.ParseLine("out_time_us=11000000");
        progress.ParseLine("progress=continue");

        progress.GetSnapshot().CurrentBitrateKbps.ShouldBe(6000);
    }

    [Test]
    public void Parses_Legacy_Stats_Line_Speed()
    {
        var progress = new FFmpegProgress();
        progress.ParseLine("frame=  100 fps= 25 q=28.0 size=     256kB time=00:00:04.00 bitrate= 524.3kbits/s speed=2.5x");

        progress.Speed.IfNone(0).ShouldBe(2.5);
    }
}
