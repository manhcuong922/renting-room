using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

/// <summary>
/// CT-BR-51: lịch sử người đứng tên — <see cref="RenterId"/> đã đứng tên HĐ trong [<see cref="FromDate"/>, <see cref="ToDate"/>]. Người đứng tên
/// hiện tại nằm trên HĐ (<c>representative_renter_id</c>, <c>representative_since</c>). Bất biến sau khi ghi.
/// </summary>
public sealed class ContractRepresentative : TenantEntity
{
    private ContractRepresentative() { } // EF Core

    internal ContractRepresentative(Guid contractId, Guid renterId, DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
            throw new ArgumentOutOfRangeException(nameof(toDate));
        Id = Guid.CreateVersion7();
        ContractId = contractId;
        RenterId = renterId;
        FromDate = fromDate;
        ToDate = toDate;
    }

    public Guid ContractId { get; private set; }
    public Guid RenterId { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
}
