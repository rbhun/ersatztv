using ErsatzTV.Core.Domain;

namespace ErsatzTV.Core.Scheduling;

public record EndCreditsTrim(TimeSpan Keep)
{
    public static EndCreditsTrim ForChannel(Channel channel) =>
        channel.SkipEndCredits
            ? new EndCreditsTrim(TimeSpan.FromSeconds(Math.Max(0, channel.EndCreditsKeepSeconds)))
            : null;
}
