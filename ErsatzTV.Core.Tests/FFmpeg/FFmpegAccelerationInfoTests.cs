using ErsatzTV.Core.FFmpeg;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Core.Tests.FFmpeg;

[TestFixture]
public class FFmpegAccelerationInfoTests
{
    [Test]
    public void Software_Decode_And_Encode()
    {
        const string ARGUMENTS =
            "-nostdin -hide_banner -nostats -progress - -loglevel error -fflags +genpts+discardcorrupt+igndts -ss 1000ms -c:v h264 -readrate 1.05 -i /tmp/whatever.mkv -filter_complex [0:1]aresample=async=1[a] -map 0:0 -map [a] -b:v 2000k -c:v libx265 -tag:v hvc1 -c:a aac -r 30000/1001 -vsync cfr -f mpegts pipe:1";

        FFmpegAccelerationInfo result = FFmpegAccelerationInfo.FromArguments(ARGUMENTS);

        result.HardwareAcceleration.ShouldBeNull();
        result.VideoDecoder.ShouldBe("h264");
        result.IsHardwareDecode.ShouldBeFalse();
        result.VideoEncoder.ShouldBe("libx265");
        result.IsHardwareEncode.ShouldBeFalse();
        result.IsVideoCopy.ShouldBeFalse();
        result.OutputFrameRate.ShouldBe("30000/1001");
        result.HardwareFilters.ShouldBeEmpty();
        result.OutputFrameRateValue.IfNone(0).ShouldBe(29.97, 0.001);
        result.OutputWidth.ShouldBeNull();
        result.VideoBitrate.ShouldBe("2000k");
    }

    [Test]
    public void Output_Size_Prefers_Pad_Over_Scale()
    {
        const string ARGUMENTS =
            "-i /tmp/a.mkv -i /tmp/logo.png -filter_complex [1:0]scale=200:-1[wm];[0:0]scale=1920:800:force_original_aspect_ratio=decrease,pad=1920:1080:-1:-1:color=black[v];[v][wm]overlay[vo] -c:v libx264 -b:v 6000k -f mpegts pipe:1";

        FFmpegAccelerationInfo result = FFmpegAccelerationInfo.FromArguments(ARGUMENTS);

        result.OutputWidth.ShouldBe(1920);
        result.OutputHeight.ShouldBe(1080);
        result.VideoBitrate.ShouldBe("6000k");
    }

    [Test]
    public void Output_Size_From_Hardware_Scale()
    {
        FFmpegAccelerationInfo result = FFmpegAccelerationInfo.FromArguments(
            "-hwaccel vaapi -i /tmp/a.mkv -filter_complex [0:0]scale_vaapi=1280:720:force_original_aspect_ratio=decrease[v] -c:v h264_vaapi -f mpegts pipe:1");

        result.OutputWidth.ShouldBe(1280);
        result.OutputHeight.ShouldBe(720);
    }

    [Test]
    public void VideoToolbox_Decode_And_Encode()
    {
        const string ARGUMENTS =
            "-nostdin -hwaccel videotoolbox -readrate 1.0 -i \"/media/My Movie (2020).mkv\" -filter_complex \"[0:0]scale_vt=w=1920:h=1080,hwdownload,format=nv12,pad=1920:1080[v]\" -map [v] -c:v h264_videotoolbox -r 24 -f mpegts pipe:1";

        FFmpegAccelerationInfo result = FFmpegAccelerationInfo.FromArguments(ARGUMENTS);

        result.HardwareAcceleration.ShouldBe("videotoolbox");
        result.VideoDecoder.ShouldBeNull();
        result.IsHardwareDecode.ShouldBeTrue();
        result.VideoEncoder.ShouldBe("h264_videotoolbox");
        result.IsHardwareEncode.ShouldBeTrue();
        result.HardwareFilters.ShouldBe(["scale_vt"]);
        result.DownloadsFromHardware.ShouldBeTrue();
    }

    [Test]
    public void Nvidia_Cuvid_Decoder_With_Graphics_Input()
    {
        const string ARGUMENTS =
            "-init_hw_device cuda -c:v hevc_cuvid -i /tmp/a.mkv -f rawvideo -pix_fmt bgra -r 30 -c:v rawvideo -i pipe:0 -filter_complex [0:0]scale_cuda=1280:720,hwupload_cuda[v] -c:v h264_nvenc -r 30 -f mpegts pipe:1";

        FFmpegAccelerationInfo result = FFmpegAccelerationInfo.FromArguments(ARGUMENTS);

        result.HardwareAcceleration.ShouldBeNull();
        result.VideoDecoder.ShouldBe("hevc_cuvid");
        result.IsHardwareDecode.ShouldBeTrue();
        result.VideoEncoder.ShouldBe("h264_nvenc");
        result.IsHardwareEncode.ShouldBeTrue();
        result.HardwareFilters.ShouldBe(["scale_cuda", "hwupload_cuda"]);
        result.DownloadsFromHardware.ShouldBeFalse();
    }

    [Test]
    public void Copy()
    {
        const string ARGUMENTS =
            "-nostdin -readrate 1.0 -i /tmp/whatever.mkv -map 0:0 -map 0:1 -c:v copy -c:a copy -f mp4 pipe:1";

        FFmpegAccelerationInfo result = FFmpegAccelerationInfo.FromArguments(ARGUMENTS);

        result.IsVideoCopy.ShouldBeTrue();
        result.VideoEncoder.ShouldBe("copy");
        result.IsHardwareEncode.ShouldBeFalse();
    }

    [Test]
    public void Copy_All()
    {
        FFmpegAccelerationInfo result =
            FFmpegAccelerationInfo.FromArguments("-i http://localhost/concat -c copy -f mpegts pipe:1");

        result.IsVideoCopy.ShouldBeTrue();
        result.VideoEncoder.ShouldBe("copy");
    }

    [Test]
    public void Empty_Is_Unknown()
    {
        FFmpegAccelerationInfo.FromArguments(string.Empty).ShouldBe(FFmpegAccelerationInfo.Unknown);
        FFmpegAccelerationInfo.FromArguments("-version").ShouldBe(FFmpegAccelerationInfo.Unknown);
    }
}
