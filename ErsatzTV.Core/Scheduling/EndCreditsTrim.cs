using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Interfaces.Repositories;

namespace ErsatzTV.Core.Scheduling;

public record EndCreditsTrim(TimeSpan Keep)
{
    public static EndCreditsTrim ForChannel(Channel channel) =>
        Resolve(channel, globalSkip: false, globalKeepSeconds: 0);

    public static EndCreditsTrim Resolve(Channel channel, bool globalSkip, int globalKeepSeconds)
    {
        if (channel is { SkipEndCredits: true })
        {
            return new EndCreditsTrim(TimeSpan.FromSeconds(Math.Max(0, channel.EndCreditsKeepSeconds)));
        }

        if (globalSkip)
        {
            return new EndCreditsTrim(TimeSpan.FromSeconds(Math.Max(0, globalKeepSeconds)));
        }

        return null;
    }

    public static async Task<EndCreditsTrim> ResolveAsync(
        Channel channel,
        IConfigElementRepository configElementRepository,
        CancellationToken cancellationToken)
    {
        bool globalSkip = await configElementRepository
            .GetValue<bool>(ConfigElementKey.PlayoutSkipEndCredits, cancellationToken)
            .IfNoneAsync(false);

        int globalKeepSeconds = await configElementRepository
            .GetValue<int>(ConfigElementKey.PlayoutEndCreditsKeepSeconds, cancellationToken)
            .IfNoneAsync(0);

        return Resolve(channel, globalSkip, globalKeepSeconds);
    }
}
