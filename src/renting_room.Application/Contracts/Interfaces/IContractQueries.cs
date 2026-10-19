using renting_room.Application.Common.Models;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>Đọc hợp đồng (chỉ đọc, không dùng để sửa) cho các use case cần tính toán trên aggregate.</summary>
public interface IContractQueries
{
    /// <summary>CT-BR-44/45: cờ của các HĐ đang hiệu lực — tính bằng <see cref="Contract.Flags"/> của domain.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ContractFlag>>> GetFlagsAsync(
        IEnumerable<Guid> contractIds, DateOnly today, CancellationToken cancellationToken);

    /// <summary>HĐ đã từng ở phòng (hiệu lực / thanh lý / kết thúc), kể cả qua lịch sử chuyển phòng; kèm người ở, chuyển phòng, đứng tên.</summary>
    Task<IReadOnlyList<Contract>> ListLivedInRoomAsync(Guid roomId, CancellationToken cancellationToken);

    /// <summary>HĐ đã ở (hiệu lực / thanh lý / kết thúc) có một trong các người thuê là người đứng tên / người ở / từng đứng tên.</summary>
    Task<IReadOnlyList<Contract>> ListLivedByRentersAsync(
        IReadOnlyCollection<Guid> renterIds, Guid? propertyId, CancellationToken cancellationToken);

    /// <summary>HĐ hiệu lực / thanh lý của khu có ngày tính tiền trong [<paramref name="from"/>, <paramref name="until"/>]; kèm giá thuê.</summary>
    Task<IReadOnlyList<Contract>> ListForBillingTransitionAsync(
        Guid propertyId, DateOnly from, DateOnly until, CancellationToken cancellationToken);

    /// <summary>HĐ (chỉ đọc, không kèm phần con).</summary>
    Task<Contract?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ContractStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken);

    Task<ContractHeader?> GetHeaderAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>HĐ (chỉ đọc) kèm giá thuê.</summary>
    Task<Contract?> GetWithRentTermsAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Contract>> ListWithRentTermsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>HĐ (chỉ đọc) kèm lịch sử chuyển phòng.</summary>
    Task<Contract?> GetWithRoomMovesAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// BL-UC-01: HĐ hiệu lực / thanh lý / kết thúc ở các phòng, tính tiền từ trước hết tháng sau và chưa trả phòng trước tháng — theo số HĐ.
    /// </summary>
    Task<IReadOnlyList<BillingCandidate>> ListBillingCandidatesAsync(
        IReadOnlyCollection<Guid> roomIds, DateOnly month, DateOnly monthEnd, Guid? contractId, CancellationToken cancellationToken);

    /// <summary>MT-UC-04: HĐ hiệu lực / thanh lý / kết thúc ở các phòng, tính tiền từ trước hết tháng sau; kèm giá thuê, người ở, chuyển phòng.</summary>
    Task<IReadOnlyList<Contract>> ListForMeterSheetAsync(IReadOnlyCollection<Guid> roomIds, DateOnly monthEnd, CancellationToken cancellationToken);

    /// <summary>HĐ đang ở phòng tại ngày <paramref name="date"/> — kể cả HĐ đã chuyển sang phòng khác sau đó (CT-BR-14); mới nhất trước.</summary>
    Task<ContractTenancy?> FindTenancyAsync(Guid roomId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Phòng đang có HĐ hiệu lực / thanh lý.</summary>
    Task<bool> AnyActiveInRoomAsync(Guid roomId, CancellationToken cancellationToken);

    /// <summary>PM-BR-31: HĐ đứng trước trong chuỗi ký lại (CT-UC-21).</summary>
    Task<Guid?> GetPreviousContractIdAsync(Guid id, CancellationToken cancellationToken);

    Task<ContractChainLink?> GetChainLinkAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>HĐ (chỉ đọc) kèm đủ phần con.</summary>
    Task<Contract?> GetDetailsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Danh sách HĐ (chưa kèm cờ), mới bắt đầu trước.</summary>
    Task<PagedResult<ContractSummaryDto>> ListAsync(ListContractsQuery request, DateOnly today, CancellationToken cancellationToken);

    Task<Guid?> GetRoomIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> IsContractNoTakenAsync(string contractNo, CancellationToken cancellationToken);

    /// <summary>CT-BR-14: HĐ khác đã chuyển khỏi phòng này mà lịch sử ở còn tới / sau ngày <paramref name="date"/>.</summary>
    Task<bool> AnyRoomMoveFromAsync(Guid roomId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Phòng có HĐ khác chiếm từ ngày <paramref name="date"/> (nháp / hiệu lực / thanh lý / kết thúc chưa trả, lịch sử chuyển phòng).</summary>
    Task<bool> IsRoomTakenAsync(Guid exceptContractId, Guid roomId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>HĐ đang hiệu lực đã bắt đầu của phòng.</summary>
    Task<Guid?> GetActiveIdInRoomAsync(Guid roomId, DateOnly today, CancellationToken cancellationToken);

    /// <summary>HĐ hiệu lực / thanh lý của khu (chưa trả phòng trước hôm nay), kèm khoản thu.</summary>
    Task<IReadOnlyList<Contract>> ListActiveWithFeesAsync(Guid propertyId, DateOnly today, CancellationToken cancellationToken);

    /// <summary>HĐ hiệu lực / thanh lý (lọc khu), kèm người ở và khoản thu.</summary>
    Task<IReadOnlyList<Contract>> ListOpenForReviewAsync(Guid? propertyId, CancellationToken cancellationToken);

    /// <summary>Null ⇒ không có HĐ; <see cref="OccupantRef.RenterId"/> null ⇒ HĐ không có người ở này.</summary>
    Task<OccupantRef?> FindOccupantAsync(Guid contractId, Guid occupantId, CancellationToken cancellationToken);

    /// <summary>Xe đang đăng ký (chưa kết thúc) của người thuê trên HĐ.</summary>
    Task<IReadOnlyList<VehicleRow>> ListOpenVehiclesAsync(Guid contractId, Guid renterId, CancellationToken cancellationToken);

    /// <summary>Biển số đang đăng ký giữ ở bất kỳ HĐ nào của tổ chức.</summary>
    Task<bool> IsPlateRegisteredAsync(string plate, CancellationToken cancellationToken);

    /// <summary>CT-BR-31: các lần ở (HĐ đã bàn giao, phòng khác <paramref name="roomId"/>) của những người thuê này.</summary>
    Task<IReadOnlyList<StayRow>> ListStaysElsewhereAsync(Guid roomId, IReadOnlyCollection<Guid> renterIds, CancellationToken cancellationToken);

    /// <summary>CT-BR-50: người đứng tên đang là người ở thường (chưa chuyển đi trước <paramref name="start"/>) của HĐ khác đang chạy.</summary>
    Task<ContractRoomRef?> FindResidentElsewhereAsync(Guid contractId, Guid representativeId, DateOnly start, CancellationToken cancellationToken);

    /// <summary>HĐ khác đang chạy do những người này đứng tên.</summary>
    Task<IReadOnlyList<SignedContractRow>> ListRunningSignedByAsync(
        Guid exceptContractId, IReadOnlyCollection<Guid> renterIds, CancellationToken cancellationToken);

    /// <summary>Người (trong danh sách) đang ở HĐ nháp / hiệu lực / thanh lý chưa chuyển đi trước hôm nay.</summary>
    Task<IReadOnlyList<RenterStayRow>> ListOpenStaysAsync(IReadOnlyCollection<Guid> renterIds, DateOnly today, CancellationToken cancellationToken);

    /// <summary>HĐ nháp / hiệu lực / thanh lý của các phòng.</summary>
    Task<IReadOnlyList<RoomContractRow>> ListOpenInRoomsAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

    /// <summary>Phòng đã có HĐ khác ở tới ngày nào: HĐ đã kết thúc (ngày trả phòng) / đã chuyển đi (ngày chuyển — CT-BR-14).</summary>
    Task<IReadOnlyList<RoomOccupiedRow>> ListOccupiedUntilAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

    /// <summary>CT-BR-51: HĐ người này từng đứng tên (sau đó đã đổi người đứng tên).</summary>
    Task<IReadOnlyList<Guid>> ListIdsByFormerRepresentativeAsync(Guid renterId, CancellationToken cancellationToken);
}

public sealed record ContractHeader(Guid Id, string ContractNo, ContractStatus Status);

public sealed record BillingCandidate(Guid Id, Guid RoomId, string ContractNo);

public sealed record ContractTenancy(Guid Id, ContractStatus Status);

public sealed record ContractChainLink(string ContractNo, Guid? PreviousContractId);

public sealed record OccupantRef(Guid? RenterId);

public sealed record VehicleRow(Guid Id, VehicleType VehicleType, string? PlateNumber, string? BrandColor, string? Note);

public sealed record StayRow(Guid RenterId, DateOnly MoveInDate, DateOnly? MoveOutDate, string ContractNo, string RoomCode);

public sealed record ContractRoomRef(string ContractNo, string RoomCode);

public sealed record RenterStayRow(Guid RenterId, string ContractNo, string RoomCode);

public sealed record RoomContractRow(Guid RoomId, string ContractNo, ContractStatus Status, string Name);

public sealed record RoomOccupiedRow(Guid RoomId, DateOnly Until);

public sealed record SignedContractRow(Guid RepresentativeRenterId, DateOnly? ActualEndDate);
