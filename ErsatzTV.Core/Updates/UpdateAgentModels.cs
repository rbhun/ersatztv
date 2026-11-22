namespace ErsatzTV.Core.Updates;

public record UpdateSource(string Id, string Label, string Url, string Branch);

public record UpdateSourcesResponse(List<UpdateSource> Sources);

public record UpdateCheckResponse(
    UpdateSource Source,
    string LocalSha,
    string RemoteSha,
    bool UpdateAvailable,
    string InstalledVersion,
    double CheckedAt);

public record UpdateStatusResponse(
    bool Updating,
    double? LastStarted,
    double? LastFinished,
    bool? LastOk,
    string LastError,
    string LastSource,
    string InstalledVersion,
    string LocalHead,
    string DeployDir);

public record UpdateStartResponse(bool Started, string Source);
