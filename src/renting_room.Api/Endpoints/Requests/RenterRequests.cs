using renting_room.Application.Renters;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Endpoints;

public sealed record UpdateRenterRequest(RenterInput Renter, uint Version);

public sealed record AnonymizeRenterRequest(string Reason);

/// <summary>Tìm kiếm có số giấy tờ — đi trong body để số CCCD không nằm trên URL (log proxy, lịch sử trình duyệt, Referer).</summary>
public sealed record SearchRentersRequest(string? Q, string? IdNumber, IdDocumentType? IdType, int? Page, int? PageSize);

public sealed record AddConsentRequest(string Purpose, ConsentMethod Method, DateOnly GivenAt, string? Note);

public sealed record WithdrawConsentRequest(DateOnly WithdrawnOn);
