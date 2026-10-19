using FluentValidation;
using Mediator;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

// RT-UC-05: ghi nhận đồng ý xử lý dữ liệu cá nhân
public sealed record ListRenterConsentsQuery(Guid RenterId) : IRequest<Result<IReadOnlyList<RenterConsentDto>>>;

public sealed class ListRenterConsentsHandler(IRenterQueries renters) : IRequestHandler<ListRenterConsentsQuery, Result<IReadOnlyList<RenterConsentDto>>>
{
    public async ValueTask<Result<IReadOnlyList<RenterConsentDto>>> Handle(ListRenterConsentsQuery request, CancellationToken cancellationToken)
    {
        if (!await renters.ExistsAsync(request.RenterId, cancellationToken))
            return RenterErrors.RenterNotFound;
        var consents = await renters.ListConsentsAsync(request.RenterId, cancellationToken);
        return consents.Select(c => c.ToDto()).ToList();
    }
}
