namespace ErsatzTV.Application.Configuration;

public class PlayoutSettingsViewModel
{
    public int DaysToBuild { get; set; }
    public bool SkipMissingItems { get; set; }
    public int ScriptedScheduleTimeoutSeconds { get; set; }
    public bool SkipEndCredits { get; set; }
    public int EndCreditsKeepSeconds { get; set; }
}
