using Mediator;
using renting_room.Domain.Common;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

public sealed record ListMeterReadingsQuery(Guid MeterId) : IRequest<Result<IReadOnlyList<MeterReadingDto>>>;

public sealed class ListMeterReadingsHandler(IMeterQueries meters) : IRequestHandler<ListMeterReadingsQuery, Result<IReadOnlyList<MeterReadingDto>>>
{
    public async ValueTask<Result<IReadOnlyList<MeterReadingDto>>> Handle(ListMeterReadingsQuery request, CancellationToken cancellationToken)
    {
        var meter = await meters.GetAsync(request.MeterId, cancellationToken);
        if (meter is null)
            return MeterErrors.NotFound;
        return meter.Ordered.Reverse().Select(r => r.ToDto()).ToList();
    }
}
