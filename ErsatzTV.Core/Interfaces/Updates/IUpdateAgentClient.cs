using ErsatzTV.Core.Updates;

namespace ErsatzTV.Core.Interfaces.Updates;

public interface IUpdateAgentClient
{
    bool IsConfigured { get; }

    Task<Either<BaseError, UpdateSourcesResponse>> GetSources(CancellationToken cancellationToken);

    Task<Either<BaseError, UpdateCheckResponse>> Check(
        string sourceId,
        CancellationToken cancellationToken);

    Task<Either<BaseError, UpdateStatusResponse>> GetStatus(CancellationToken cancellationToken);

    Task<Either<BaseError, string>> GetLog(CancellationToken cancellationToken);

    Task<Either<BaseError, UpdateStartResponse>> StartUpdate(
        string sourceId,
        CancellationToken cancellationToken);
}
