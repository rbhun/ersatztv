namespace ErsatzTV.Application.Playouts;

public record GetMediaItemIdByPlayoutItemId(int PlayoutItemId) : IRequest<Option<int>>;
