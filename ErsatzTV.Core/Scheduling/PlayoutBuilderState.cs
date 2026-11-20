using ErsatzTV.Core.Interfaces.Scheduling;

namespace ErsatzTV.Core.Scheduling;

public record PlayoutBuilderState(
    int PlayoutId,
    IScheduleItemsEnumerator ScheduleItemsEnumerator,
    Option<int> MultipleRemaining,
    Option<DateTimeOffset> DurationFinish,
    bool InFlood,
    bool InDurationFiller,
    int NextGuideGroup,
    DateTimeOffset CurrentTime)
{
    public EndCreditsTrim EndCreditsTrim { get; init; }

    public int IncrementGuideGroup => (NextGuideGroup + 1) % 10000;
    public int DecrementGuideGroup => (NextGuideGroup - 1) % 10000;
}
