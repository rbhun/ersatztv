using ErsatzTV.Core.FFmpeg;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Core.Tests.FFmpeg;

[TestFixture]
public class NextProgressReportTests
{
    private const string Report =
        "item_id=42\n" +
        "pid=1234\n" +
        "started_at=1767225600000\n" +
        "output_start=1767225601000\n" +
        "realtime=false\n" +
        "args=-hwaccel videotoolbox -i \"/media/My Movie.mkv\" -c:v h264_videotoolbox -f hls live.m3u8\n" +
        "seq=3\n" +
        "frame=250\n" +
        "fps=50.0\n" +
        "drop_frames=0\n" +
        "progress=continue\n";

    [Test]
    public void Should_Parse_Header_And_Progress()
    {
        NextProgressReport report = NextProgressReport.Parse(Report).IfNoneUnsafe(() => null);

        report.ShouldNotBeNull();
        report.ItemId.ShouldBe("42");
        report.ProcessId.ShouldBe(1234);
        report.StartedAt.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1767225600000));
        report.OutputStart.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1767225601000));
        report.IsRealtime.ShouldBeFalse();
        report.Sequence.ShouldBe(3);
        report.ProgressLines.ShouldBe(["frame=250", "fps=50.0", "drop_frames=0", "progress=continue"]);
        report.IsEnded.ShouldBeFalse();

        FFmpegAccelerationInfo info = FFmpegAccelerationInfo.FromArguments(report.Arguments);
        info.IsHardwareDecode.ShouldBeTrue();
        info.IsHardwareEncode.ShouldBeTrue();
    }

    [Test]
    public void Should_Detect_End_And_Missing_Pid()
    {
        string contents = Report.Replace("pid=1234", "pid=").Replace("progress=continue", "progress=end");

        NextProgressReport report = NextProgressReport.Parse(contents).IfNoneUnsafe(() => null);

        report.ShouldNotBeNull();
        report.ProcessId.ShouldBeNull();
        report.IsEnded.ShouldBeTrue();
    }

    [Test]
    public void Should_Reject_Incomplete_Report()
    {
        NextProgressReport.Parse("item_id=42\npid=1\n").IsNone.ShouldBeTrue();
        NextProgressReport.Parse(string.Empty).IsNone.ShouldBeTrue();
    }
}
