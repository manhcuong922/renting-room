namespace renting_room.Domain.Contracts;

public enum ContractStatus
{
    Draft,
    Active,
    Liquidating,
    Ended,
    Cancelled
}

/// <summary>CT-BR-21: lý do kết thúc hợp đồng.</summary>
public enum TerminationReason
{
    /// <summary>Hết hạn — chỉ cho HĐ có thời hạn, ngày trả phòng từ ngày hết hạn trở đi (CT-BR-21).</summary>
    Expired,
    MutualAgreement,
    LesseeUnilateral,
    LessorUnilateral,
    RoomTransfer,

    /// <summary>Bên thuê tự ý bỏ đi không báo (trả phòng bất chợt) — ngày trả phòng = ngày phát hiện / ngày cuối còn ở (CT-BR-41).</summary>
    Abandoned
}

/// <summary>Cờ dẫn xuất theo ngày của HĐ đang hiệu lực — cần chủ trọ xử lý (CT-BR-44, CT-BR-45).</summary>
public enum ContractFlag
{
    /// <summary>Người đứng tên đã chuyển đi, vẫn còn người ở — cần ký lại.</summary>
    RepresentativeMovedOut,

    /// <summary>Không còn ai ở mà HĐ vẫn hiệu lực — thanh lý?</summary>
    NoOccupantLeft,

    /// <summary>Đã quá ngày hết hạn, chủ trọ chưa quyết định (gia hạn / ở tiếp / thu lại phòng).</summary>
    ExpiredAwaitingDecision,

    /// <summary>Quá hạn, chủ trọ cho ở tiếp chưa ký lại.</summary>
    Holdover
}

/// <summary>Căn cứ bên cho thuê chấm dứt — Luật Nhà ở 2023 Điều 172 khoản 2; HĐ không thời hạn: báo trước 90 ngày (CT-BR-42).</summary>
public enum TerminationGround
{
    RentArrears3Months,
    WrongPurpose,
    UnauthorizedRenovation,
    Other,

    /// <summary>HĐ không thời hạn: bên cho thuê thông báo chấm dứt, HĐ chấm dứt sau 90 ngày (CT-BR-42).</summary>
    IndefiniteTermNotice
}

public enum PaymentMethod
{
    Cash,
    BankTransfer,
    EWallet
}

public enum VehicleType
{
    Motorbike,
    Bicycle,
    ElectricBike,
    Car
}

/// <param name="Relationship">Ghi chú quan hệ (bắt buộc khi <see cref="RelationshipType"/> = Other).</param>
/// <param name="GuardianConsent">Người chưa thành niên: đã có ý kiến đồng ý của cha, mẹ hoặc người giám hộ (Luật Cư trú Điều 28).</param>
public sealed record OccupantInput(
    Guid RenterId,
    DateOnly MoveInDate,
    DateOnly? ExpectedEndDate,
    string? Relationship,
    string? Note,
    OccupantRelationship? RelationshipType = null,
    bool GuardianConsent = false);

/// <summary>Toàn bộ nội dung hợp đồng nháp (tạo mới hoặc sửa nháp).</summary>
public sealed record ContractDraftData(
    Guid RepresentativeRenterId,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? SignedDate,
    string? SignedPlace,
    DateOnly? EffectiveDate,
    decimal MonthlyRent,
    decimal DepositAmount,
    string? DepositTerms,
    int BillingAnchorDay,
    Properties.ChargeMode ChargeMode,
    Properties.ProrationMode ProrationMode,
    int PaymentDueDays,
    int NoticeDays,
    IReadOnlyCollection<PaymentMethod> PaymentMethods,
    int CopiesCount,
    string? TermsText,
    string? Note,
    IReadOnlyCollection<OccupantInput> Occupants,
    ContractDocument? Document = null,
    Guid? HouseholdHeadRenterId = null,
    IReadOnlyCollection<ContractFeeInput>? Fees = null,
    int RentCycleMonths = 1);

/// <summary>Khoản thu gắn vào HĐ (CT-UC-06): số lượng (nhóm Quantity), giá riêng của HĐ (null = theo bảng giá của khu).</summary>
public sealed record ContractFeeInput(Guid FeeTypeId, decimal Quantity, decimal? UnitPriceOverride);

/// <summary>
/// Phần văn bản của hợp đồng (CT-BR-25): loại, tiêu đề, điều khoản, trường tùy biến — chép từ mẫu lúc tạo/sửa nháp.
/// Các chuỗi JSON đã được Application kiểm tra (định nghĩa trường + giá trị khớp định nghĩa).
/// </summary>
public sealed record ContractDocument(
    Guid? TemplateId,
    ContractType ContractType,
    string Title,
    string? ClausesJson,
    string? FieldDefinitionsJson,
    string? FieldValuesJson)
{
    public static ContractDocument Default(ContractType type = ContractType.RoomRental) =>
        new(null, type, ContractTypes.DefaultTitle(type), null, null, null);
}

/// <summary>Dữ liệu Application thu thập để kiểm tra & snapshot khi kích hoạt (CT-BR-02, 18, 19).</summary>
public sealed record ActivationContext(
    DateOnly Today,
    DateTimeOffset Now,
    bool RoomAvailable,
    int RoomMaxOccupants,
    bool LessorComplete,
    string SigningSnapshotJson,
    string? HouseRulesSnapshot,
    DateOnly RepresentativeDateOfBirth,
    bool RepresentativeHasPhone,
    bool OverrideCapacity = false,
    string? UtilityPriceSnapshotJson = null);

public sealed record NoticeResult(bool ShorterThanNoticePeriod, int NoticeDays, int ActualDays);
