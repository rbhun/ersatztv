using System.Collections.Immutable;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.FFmpeg;

namespace ErsatzTV.Core.Interfaces.FFmpeg;

public interface IFFmpegStreamSelector
{
    Task<MediaStream> SelectVideoStream(MediaVersion version);

    Task<Option<MediaStream>> SelectAudioStream(
        MediaItemAudioVersion version,
        StreamingMode streamingMode,
        Channel channel,
        string preferredAudioLanguage,
        string preferredAudioTitle,
        bool shouldLogMessages,
        CancellationToken cancellationToken);

    Option<MediaStream> SelectAudioStreamForLanguage(
        MediaVersion version,
        string language,
        bool shouldLogMessages);

    Task<Option<Subtitle>> SelectSubtitleStream(
        ImmutableList<Subtitle> subtitles,
        Channel channel,
        string preferredSubtitleLanguage,
        ChannelSubtitleMode subtitleMode,
        bool shouldLogMessages,
        CancellationToken cancellationToken);
}
