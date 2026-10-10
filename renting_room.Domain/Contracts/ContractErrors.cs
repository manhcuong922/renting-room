using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public static class ContractErrors
{
    public static readonly Error NotFound = Error.NotFound("CONTRACT_NOT_FOUND", "Không tìm thấy hợp đồng.");
    public static readonly Error ContractNoTaken = Error.Conflict("CONTRACT_NO_TAKEN", "Số hợp đồng đã tồn tại.");
    public static readonly Error NotDraft = Error.BusinessRule("CONTRACT_NOT_DRAFT", "Chỉ thao tác được khi hợp đồng còn là bản nháp.");
    public static readonly Error NotActive = Error.BusinessRule("CONTRACT_NOT_ACTIVE", "Hợp đồng không ở trạng thái đang hiệu lực.");
    public static readonly Error NotLiquidating = Error.BusinessRule("CONTRACT_NOT_LIQUIDATING", "Hợp đồng không ở trạng thái đang thanh lý.");
    public static readonly Error NotEditable = Error.BusinessRule("CONTRACT_NOT_EDITABLE", "Hợp đồng đã kết thúc hoặc đã hủy.");
    public static readonly Error NoDeposit = Error.BusinessRule("NO_DEPOSIT", "Hợp đồng không có cọc.");
    public static readonly Error DepositAlreadyRefunded = Error.BusinessRule("DEPOSIT_ALREADY_REFUNDED",
        "Cọc đã hoàn trả / đã chuyển sang HĐ mới — bỏ đánh dấu trước nếu nhập nhầm.");
    public static readonly Error DepositNotRefunded = Error.BusinessRule("DEPOSIT_NOT_REFUNDED", "Cọc chưa đánh dấu hoàn trả.");
    public static readonly Error InvalidRefundAmount = Error.Validation("INVALID_REFUND_AMOUNT",
        "Số tiền hoàn trả từ 0 đến số tiền cọc; trả ít hơn cọc thì ghi rõ lý do.");
    public static readonly Error InvalidRefundDate = Error.Validation("INVALID_REFUND_DATE", "Ngày hoàn trả không được ở tương lai.");
    public static readonly Error TransferSameRoom = Error.Validation("ROOM_TRANSFER_SAME_ROOM", "Phòng mới trùng phòng hiện tại.");
    public static readonly Error TransferOtherProperty = Error.BusinessRule("ROOM_TRANSFER_OTHER_PROPERTY",
        "Chỉ chuyển sang phòng cùng khu — khác khu thì thanh lý và lập HĐ mới (chuyển cọc).");
    public static readonly Error TransferRoomUnavailable = Error.BusinessRule("ROOM_TRANSFER_ROOM_UNAVAILABLE",
        "Phòng mới không trống từ ngày chuyển (đang thuê, có HĐ giữ chỗ, đang bảo trì hoặc ngừng dùng).");
    public static readonly Error TransferInvalidDate = Error.Validation("ROOM_TRANSFER_INVALID_DATE",
        "Ngày chuyển phải sau ngày vào phòng hiện tại và không ở tương lai.");
    public static readonly Error TransferAlreadyBilled = Error.BusinessRule("ROOM_TRANSFER_ALREADY_BILLED",
        "Điện nước phòng cũ đã lập phiếu tới sau ngày chuyển — hủy phiếu đó trước.");

    public static readonly Error RoomUnavailable = Error.BusinessRule("ROOM_UNAVAILABLE", "Phòng đã ngừng sử dụng hoặc đang bảo trì.");
    public static readonly Error RoomPeriodOverlap = Error.Conflict("ROOM_PERIOD_OVERLAP",
        "Phòng đã có hợp đồng khác trong khoảng thời gian này.");
    public static readonly Error StartDateTooFarInFuture = Error.BusinessRule("START_DATE_IN_FUTURE",
        "Chỉ kích hoạt khi bàn giao phòng thực tế (ngày bắt đầu không quá ngày mai).");
    public static readonly Error RepresentativeIdRequired = Error.BusinessRule("REPRESENTATIVE_ID_REQUIRED",
        "Người đứng tên hợp đồng phải có số giấy tờ (CCCD / CMND / hộ chiếu) — bổ sung ở hồ sơ người thuê.");
    public static readonly Error RepresentativeUnderage = Error.BusinessRule("REPRESENTATIVE_UNDERAGE",
        "Người đứng tên ký hợp đồng phải đủ 18 tuổi (BLDS 2015 Điều 117).");
    public static readonly Error NoOccupant = Error.BusinessRule("NO_OCCUPANT", "Hợp đồng phải có ít nhất 1 người ở.");
    public static readonly Error OccupantOverlap = Error.Conflict("OCCUPANCY_OVERLAP", "Người này đã đang ở trong hợp đồng.");
    // CT-BR-28..31: quan hệ người ở với người đứng tên, người chưa thành niên, ở 2 nơi cùng lúc.
    public static readonly Error RelationshipRequired = Error.Validation("RELATIONSHIP_REQUIRED",
        "Chọn quan hệ của người ở với người đứng tên hợp đồng.");
    public static readonly Error RelationshipNoteRequired = Error.Validation("RELATIONSHIP_NOTE_REQUIRED",
        "Quan hệ \"Khác\" cần ghi rõ.");
    public static readonly Error RelationshipGenderMismatch = Error.Validation("RELATIONSHIP_GENDER_MISMATCH",
        "Quan hệ không khớp giới tính của người ở (VD \"Vợ\" phải là nữ, \"Cha đẻ\" phải là nam).");
    public static readonly Error RelationshipAgeMismatch = Error.Validation("RELATIONSHIP_AGE_MISMATCH",
        "Quan hệ không khớp tuổi: cha/mẹ đẻ, ông bà phải lớn tuổi hơn; con đẻ, cháu, chắt phải nhỏ tuổi hơn người đứng tên.");
    public static readonly Error SpouseUnderMarriageAge = Error.Validation("SPOUSE_UNDER_MARRIAGE_AGE",
        "Vợ chồng phải đủ tuổi kết hôn (nam từ 20, nữ từ 18 — Luật Hôn nhân và gia đình 2014 Điều 8).");
    public static readonly Error MultipleSpouses = Error.Validation("MULTIPLE_SPOUSES",
        "Chỉ một người là vợ/chồng của người đứng tên trong cùng thời gian.");
    public static readonly Error GuardianConsentRequired = Error.Validation("GUARDIAN_CONSENT_REQUIRED",
        "Người chưa thành niên cần có ý kiến đồng ý của cha, mẹ hoặc người giám hộ (Luật Cư trú 2020 Điều 28).");
    public static readonly Error OccupantLivesElsewhere = Error.Conflict("OCCUPANT_LIVES_ELSEWHERE",
        "Người này đang ở phòng khác trong cùng thời gian — ghi nhận chuyển đi ở hợp đồng cũ trước.");
    public static readonly Error OccupantAlreadyMovedOut = Error.Conflict("OCCUPANT_ALREADY_MOVED_OUT",
        "Người này đã được ghi nhận chuyển đi — muốn ở lại thì thêm lại như người ở mới.");
    public static readonly Error DepositTooHigh = Error.Validation("DEPOSIT_TOO_HIGH",
        "Tiền cọc tối đa 12 tháng tiền thuê — kiểm tra lại số tiền (có thể thừa số 0).");
    public static readonly Error ExpiredExtendFirst = Error.BusinessRule("CONTRACT_EXPIRED_EXTEND_FIRST",
        "Ngày vào ở sau ngày hết hạn hợp đồng — gia hạn hợp đồng (phụ lục) trước khi thêm người ở.");
    public static readonly Error VehicleOwnerNotInContract = Error.BusinessRule("VEHICLE_OWNER_NOT_IN_CONTRACT",
        "Có xe đăng ký cho người không còn thuộc hợp đồng — kết thúc đăng ký xe đó trước.");
    public static readonly Error HouseholdHeadNotOccupant = Error.Validation("HOUSEHOLD_HEAD_NOT_OCCUPANT",
        "Chủ hộ phải là một trong những người ở.");
    public static readonly Error FeeNotRegistered = Error.NotFound("CONTRACT_FEE_NOT_FOUND",
        "Hợp đồng không có khoản thu này trong thời gian đó.");
    public static readonly Error FeeLaterChangeExists = Error.Conflict("CONTRACT_FEE_LATER_CHANGE_EXISTS",
        "Khoản thu đã có thay đổi từ kỳ sau — sửa / gỡ thay đổi đó trước.");
    public static readonly Error OccupantNotFound = Error.NotFound("OCCUPANT_NOT_FOUND", "Không tìm thấy người ở trong hợp đồng.");
    public static readonly Error DateOutsideContract = Error.BusinessRule("DATE_OUTSIDE_CONTRACT", "Ngày nằm ngoài thời gian hợp đồng.");

    public static readonly Error NotPeriodStart = Error.BusinessRule("NOT_PERIOD_START",
        "Giá mới chỉ được áp dụng từ ngày bắt đầu một kỳ thu.");
    public static readonly Error PeriodAlreadyBilled = Error.BusinessRule("PERIOD_ALREADY_BILLED",
        "Kỳ này đã lập phiếu — chỉ đổi giá từ kỳ chưa lập phiếu.");
    public static readonly Error RentTermExists = Error.Conflict("RENT_TERM_EXISTS", "Đã có giá thuê áp dụng từ ngày này.");
    public static readonly Error CannotExtendIndefinite = Error.BusinessRule("CANNOT_EXTEND_INDEFINITE", "Hợp đồng không thời hạn không cần gia hạn.");
    public static readonly Error InvalidEndDate = Error.BusinessRule("INVALID_END_DATE", "Ngày kết thúc không hợp lệ.");
    public static readonly Error LiquidationBeforeEndDate = Error.BusinessRule("LIQUIDATION_BEFORE_END_DATE",
        "Chỉ hoàn tất thanh lý từ ngày trả phòng thực tế trở đi.");
    public static readonly Error ExpiredReasonInvalid = Error.BusinessRule("EXPIRED_REASON_INVALID",
        "Chỉ chọn \"Hết hạn\" khi hợp đồng có thời hạn và ngày trả phòng từ ngày hết hạn trở đi — trả sớm hãy chọn lý do khác.");
    public static readonly Error IndefiniteGroundOnly = Error.BusinessRule("INDEFINITE_GROUND_ONLY",
        "Căn cứ \"thông báo chấm dứt hợp đồng không thời hạn\" chỉ dùng cho hợp đồng không thời hạn.");
    public static readonly Error InvoiceAfterEndDate = Error.BusinessRule("INVOICE_AFTER_END_DATE",
        "Đã có phiếu tiền phòng cho kỳ sau ngày trả phòng — hủy / xóa phiếu đó trước.");
    public static readonly Error NotExpired = Error.BusinessRule("CONTRACT_NOT_EXPIRED", "Hợp đồng chưa quá ngày hết hạn.");
    public static readonly Error HoldoverAlready = Error.Conflict("HOLDOVER_ALREADY", "Đã ghi nhận ở tiếp chưa ký lại.");
    public static readonly Error ResignNoOccupantLeft = Error.BusinessRule("RESIGN_NO_OCCUPANT_LEFT",
        "Sau ngày bàn giao không còn ai ở — thanh lý hợp đồng thay vì ký lại.");
    public static readonly Error ResignRepresentativeNotOccupant = Error.BusinessRule("RESIGN_REPRESENTATIVE_NOT_OCCUPANT",
        "Người đứng tên hợp đồng mới phải là một người còn ở trong phòng.");
    public static readonly Error AbandonedNoteRequired = Error.BusinessRule("ABANDONED_NOTE_REQUIRED",
        "Người thuê bỏ đi không báo: ghi rõ ngày phát hiện, tài sản để lại, người chứng kiến.");

    public static readonly Error AssetNotFound = Error.NotFound("ASSET_NOT_FOUND", "Không tìm thấy tài sản bàn giao.");
    public static readonly Error VehicleNotFound = Error.NotFound("VEHICLE_NOT_FOUND", "Không tìm thấy xe đăng ký.");
    public static readonly Error VehicleAlreadyEnded = Error.Conflict("VEHICLE_ALREADY_ENDED", "Xe đã kết thúc đăng ký.");
    public static readonly Error PlateAlreadyRegistered = Error.Conflict("PLATE_ALREADY_REGISTERED",
        "Biển số này đang được đăng ký giữ ở hợp đồng khác.");
}
