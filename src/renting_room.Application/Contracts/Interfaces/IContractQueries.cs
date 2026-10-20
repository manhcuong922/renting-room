using renting_room.Application.Common.Models;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public interface IContractQueries
{
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ContractFlag>>> GetFlagsAsync(
        IEnumerable<Guid> contractIds, DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<Contract>> ListLivedInRoomAsync(Guid roomId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Contract>> ListLivedByRentersAsync(
        IReadOnlyCollection<Guid> renterIds, Guid? propertyId, CancellationToken cancellationToken);

    /// <summary>HĐ hiệu lực / thanh lý của khu có ngày tính tiền trong [<paramref name="from"/>, <paramref name="until"/>]; kèm giá thuê.</summary>
    Task<IReadOnlyList<Contract>> ListForBillingTransitionAsync(
        Guid propertyId, DateOnly from, DateOnly until, CancellationToken cancellationToken);

    Task<Contract?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ContractStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken);

    Task<ContractHeader?> GetHeaderAsync(Guid id, CancellationToken cancellationToken);

    Task<Contract?> GetWithRentTermsAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Contract>> ListWithRentTermsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<Contract?> GetWithRoomMovesAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillingCandidate>> ListBillingCandidatesAsync(
        IReadOnlyCollection<Guid> roomIds, DateOnly month, DateOnly monthEnd, Guid? contractId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Contract>> ListForMeterSheetAsync(IReadOnlyCollection<Guid> roomIds, DateOnly monthEnd, CancellationToken cancellationToken);

    Task<ContractTenancy?> FindTenancyAsync(Guid roomId, DateOnly date, CancellationToken cancellationToken);

    Task<bool> AnyActiveInRoomAsync(Guid roomId, CancellationToken cancellationToken);

    Task<Guid?> GetPreviousContractIdAsync(Guid id, CancellationToken cancellationToken);

    Task<ContractChainLink?> GetChainLinkAsync(Guid id, CancellationToken cancellationToken);

    Task<Contract?> GetDetailsAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<ContractSummaryDto>> ListAsync(ListContractsQuery request, DateOnly today, CancellationToken cancellationToken);

    Task<Guid?> GetRoomIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> IsContractNoTakenAsync(string contractNo, CancellationToken cancellationToken);

    Task<bool> AnyRoomMoveFromAsync(Guid roomId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Phòng có HĐ khác chiếm từ ngày <paramref name="date"/> (nháp / hiệu lực / thanh lý / kết thúc chưa trả, lịch sử chuyển phòng).</summary>
    Task<bool> IsRoomTakenAsync(Guid exceptContractId, Guid roomId, DateOnly date, CancellationToken cancellationToken);

    Task<Guid?> GetActiveIdInRoomAsync(Guid roomId, DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<Contract>> ListActiveWithFeesAsync(Guid propertyId, DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<Contract>> ListOpenForReviewAsync(Guid? propertyId, CancellationToken cancellationToken);

    /// <summary>Null ⇒ không có HĐ; <see cref="OccupantRef.RenterId"/> null ⇒ HĐ không có người ở này.</summary>
    Task<OccupantRef?> FindOccupantAsync(Guid contractId, Guid occupantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<VehicleRow>> ListOpenVehiclesAsync(Guid contractId, Guid renterId, CancellationToken cancellationToken);

    Task<bool> IsPlateRegisteredAsync(string plate, CancellationToken cancellationToken);

    Task<IReadOnlyList<StayRow>> ListStaysElsewhereAsync(Guid roomId, IReadOnlyCollection<Guid> renterIds, CancellationToken cancellationToken);

    Task<ContractRoomRef?> FindResidentElsewhereAsync(Guid contractId, Guid representativeId, DateOnly start, CancellationToken cancellationToken);

    Task<IReadOnlyList<SignedContractRow>> ListRunningSignedByAsync(
        Guid exceptContractId, IReadOnlyCollection<Guid> renterIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<RenterStayRow>> ListOpenStaysAsync(IReadOnlyCollection<Guid> renterIds, DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomContractRow>> ListOpenInRoomsAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoomOccupiedRow>> ListOccupiedUntilAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);

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
