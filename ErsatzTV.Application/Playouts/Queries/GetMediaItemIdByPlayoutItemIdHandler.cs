using ErsatzTV.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ErsatzTV.Application.Playouts;

public class GetMediaItemIdByPlayoutItemIdHandler(IDbContextFactory<TvContext> dbContextFactory)
    : IRequestHandler<GetMediaItemIdByPlayoutItemId, Option<int>>
{
    public async Task<Option<int>> Handle(GetMediaItemIdByPlayoutItemId request, CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.PlayoutItems
            .AsNoTracking()
            .Filter(pi => pi.Id == request.PlayoutItemId)
            .Map(pi => pi.MediaItemId)
            .ToListAsync(cancellationToken)
            .Map(list => list.HeadOrNone());
    }
}
