using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Extensions;
using ErsatzTV.Core.Scheduling;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Core.Tests.Extensions;

[TestFixture]
public class MediaItemExtensionsTests
{
    private static readonly EndCreditsTrim SkipAll = new(TimeSpan.Zero);

    private static Episode EpisodeWithCredits(TimeSpan? creditsStart) => new()
    {
        MediaVersions =
        [
            new MediaVersion
            {
                Duration = TimeSpan.FromMinutes(30),
                CreditsStart = creditsStart,
                Chapters =
                [
                    new MediaChapter
                        { ChapterId = 1, StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromMinutes(10) },
                    new MediaChapter
                        { ChapterId = 2, StartTime = TimeSpan.FromMinutes(10), EndTime = TimeSpan.FromMinutes(25) },
                    new MediaChapter
                        { ChapterId = 3, StartTime = TimeSpan.FromMinutes(25), EndTime = TimeSpan.FromMinutes(30) }
                ]
            }
        ]
    };

    [Test]
    public void Duration_Should_End_At_Credits_When_Skipping()
    {
        Episode episode = EpisodeWithCredits(TimeSpan.FromMinutes(22));

        episode.GetDurationForPlayout(SkipAll).ShouldBe(TimeSpan.FromMinutes(22));
    }

    [Test]
    public void Duration_Should_Keep_Requested_Part_Of_Credits()
    {
        Episode episode = EpisodeWithCredits(TimeSpan.FromMinutes(22));

        episode.GetDurationForPlayout(new EndCreditsTrim(TimeSpan.FromMinutes(2)))
            .ShouldBe(TimeSpan.FromMinutes(24));
    }

    [Test]
    public void Duration_Should_Not_Exceed_Media_When_Keeping_All_Credits()
    {
        Episode episode = EpisodeWithCredits(TimeSpan.FromMinutes(22));

        episode.GetDurationForPlayout(new EndCreditsTrim(TimeSpan.FromMinutes(10)))
            .ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Test]
    public void Duration_Should_Include_Credits_When_Not_Skipping()
    {
        Episode episode = EpisodeWithCredits(TimeSpan.FromMinutes(22));

        episode.GetDurationForPlayout(null).ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Test]
    public void Duration_Should_Ignore_Missing_Or_Invalid_Credits()
    {
        EpisodeWithCredits(null).GetDurationForPlayout(SkipAll).ShouldBe(TimeSpan.FromMinutes(30));
        EpisodeWithCredits(TimeSpan.Zero).GetDurationForPlayout(SkipAll).ShouldBe(TimeSpan.FromMinutes(30));
        EpisodeWithCredits(TimeSpan.FromMinutes(31)).GetDurationForPlayout(SkipAll).ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Test]
    public void Chapters_Should_Be_Trimmed_At_Credits_Without_Changing_Source()
    {
        Episode episode = EpisodeWithCredits(TimeSpan.FromMinutes(22));

        List<MediaChapter> chapters = episode.GetChaptersForPlayout(SkipAll);

        chapters.Count.ShouldBe(2);
        chapters[0].EndTime.ShouldBe(TimeSpan.FromMinutes(10));
        chapters[1].StartTime.ShouldBe(TimeSpan.FromMinutes(10));
        chapters[1].EndTime.ShouldBe(TimeSpan.FromMinutes(22));

        episode.MediaVersions[0].Chapters[1].EndTime.ShouldBe(TimeSpan.FromMinutes(25));
        episode.MediaVersions[0].Chapters.Count.ShouldBe(3);
    }

    [Test]
    public void Chapters_Should_Be_Trimmed_After_Kept_Credits()
    {
        Episode episode = EpisodeWithCredits(TimeSpan.FromMinutes(22));

        List<MediaChapter> chapters = episode.GetChaptersForPlayout(new EndCreditsTrim(TimeSpan.FromMinutes(4)));

        chapters.Count.ShouldBe(3);
        chapters[2].StartTime.ShouldBe(TimeSpan.FromMinutes(25));
        chapters[2].EndTime.ShouldBe(TimeSpan.FromMinutes(26));
    }

    [Test]
    public void Chapters_Should_Be_Unchanged_When_Not_Skipping()
    {
        Episode episode = EpisodeWithCredits(TimeSpan.FromMinutes(22));

        List<MediaChapter> chapters = episode.GetChaptersForPlayout(null);

        chapters.Count.ShouldBe(3);
        chapters[2].EndTime.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [TestCase(false, 120, null)]
    [TestCase(true, 120, 120)]
    [TestCase(true, -30, 0)]
    public void Trim_Should_Follow_Channel_Settings(bool skipEndCredits, int keepSeconds, int? expectedSeconds)
    {
        var channel = new Channel(Guid.Empty) { SkipEndCredits = skipEndCredits, EndCreditsKeepSeconds = keepSeconds };

        EndCreditsTrim trim = EndCreditsTrim.ForChannel(channel);

        ((int?)trim?.Keep.TotalSeconds).ShouldBe(expectedSeconds);
    }

    [Test]
    public void Trim_Should_Use_Global_When_Channel_Does_Not_Override()
    {
        var channel = new Channel(Guid.Empty) { SkipEndCredits = false, EndCreditsKeepSeconds = 0 };

        EndCreditsTrim trim = EndCreditsTrim.Resolve(channel, globalSkip: true, globalKeepSeconds: 90);

        ((int?)trim?.Keep.TotalSeconds).ShouldBe(90);
    }

    [Test]
    public void Trim_Should_Prefer_Channel_Override_Over_Global()
    {
        var channel = new Channel(Guid.Empty) { SkipEndCredits = true, EndCreditsKeepSeconds = 30 };

        EndCreditsTrim trim = EndCreditsTrim.Resolve(channel, globalSkip: true, globalKeepSeconds: 90);

        ((int?)trim?.Keep.TotalSeconds).ShouldBe(30);
    }
}
