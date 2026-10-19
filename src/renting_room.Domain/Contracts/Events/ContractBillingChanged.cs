using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

/// <summary>Dữ liệu tính tiền của HĐ đổi: kỳ, phòng, ngày trả phòng, giá thuê, người ở, dịch vụ, lịch sử chuyển phòng.</summary>
public sealed record ContractBillingChanged(Guid ContractId) : IDomainEvent;
