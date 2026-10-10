using System.Text.Json;
using renting_room.Application.Common.Models;
using System.Text.Json.Serialization;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Application.Contracts;

/// <summary>Cài đặt kỳ thu của khu (chỉ đọc trên HĐ — PR-BR-09, CT-BR-04).</summary>
public sealed record BillingSettingsInput(int AnchorDay, ChargeMode ChargeMode, ProrationMode ProrationMode, int PaymentDueDays);

/// <summary>
/// Nội dung hợp đồng (tạo nháp / sửa nháp). Trường null ⇒ lấy mặc định từ khu / phòng / mẫu.
/// <c>BillingStartDate</c> = "Tính tiền từ ngày" (K5, null = ngày bắt đầu); ngày chốt / thu trước–thu sau là của khu (CT-BR-04).
/// Có <c>TemplateId</c> ⇒ loại, tiêu đề, điều khoản lấy từ mẫu (ghi đè được tiêu đề / điều khoản), <c>CustomFields</c> theo trường của mẫu.
/// </summary>
public sealed record ContractInput(
    Guid RepresentativeRenterId,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? SignedDate,
    string? SignedPlace,
    DateOnly? EffectiveDate,
    decimal? MonthlyRent,
    decimal? DepositAmount,
    string? DepositTerms,
    DateOnly? BillingStartDate,
    int? NoticeDays,
    IReadOnlyCollection<PaymentMethod>? PaymentMethods,
    int? CopiesCount,
    string? TermsText,
    string? Note,
    IReadOnlyList<OccupantRequest>? Occupants,
    Guid? TemplateId = null,
    ContractType? ContractType = null,
    string? Title = null,
    IReadOnlyList<ContractClause>? Clauses = null,
    IReadOnlyDictionary<string, JsonElement>? CustomFields = null,
    Guid? HouseholdHeadRenterId = null,
    IReadOnlyList<ContractFeeRequest>? Fees = null);

/// <param name="RelationshipType">Quan hệ với người đứng tên — bắt buộc với người không đứng tên (CT-BR-28).</param>
/// <param name="GuardianConsent">Người chưa thành niên: đã có đồng ý của cha, mẹ hoặc người giám hộ (CT-BR-30).</param>
public sealed record OccupantRequest(
    Guid RenterId, DateOnly? MoveInDate, DateOnly? ExpectedEndDate, string? Relationship, string? Note,
    OccupantRelationship? RelationshipType = null, bool? GuardianConsent = null);

public sealed record ContractSummaryDto(
    Guid Id,
    string ContractNo,
    ContractStatus Status,
    Guid PropertyId,
    string PropertyCode,
    Guid RoomId,
    string RoomCode,
    Guid RepresentativeRenterId,
    string RepresentativeName,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? ActualEndDate,
    decimal? CurrentRent,
    int OccupantCount,
    bool IsOverdue,
    ContractType ContractType,
    decimal DepositAmount,
    IReadOnlyList<ContractFlag>? Flags = null);

public sealed record RentTermDto(Guid Id, DateOnly EffectiveFrom, decimal MonthlyRent, string? AddendumNo, string? Note);

public sealed record OccupantDto(
    Guid Id, Guid RenterId, string FullName, DateOnly MoveInDate, DateOnly? MoveOutDate, DateOnly? ExpectedEndDate,
    string? Relationship, string? Note, bool IsRepresentative, OccupantRelationship? RelationshipType, bool GuardianConsent,
    bool IsHouseholdHead);

public sealed record AssetDto(
    Guid Id, string Name, int Quantity, string? ConditionAtHandover, string? ConditionAtReturn, decimal? ValueEstimate, string? Note);

public sealed record VehicleDto(
    Guid Id, Guid? RenterId, VehicleType VehicleType, string? PlateNumber, string? BrandColor, DateOnly RegisteredFrom,
    DateOnly? RegisteredTo, string? Note);

public sealed record ContractDetailDto(
    Guid Id,
    string ContractNo,
    ContractStatus Status,
    Guid PropertyId,
    string PropertyCode,
    Guid RoomId,
    string RoomCode,
    Guid RepresentativeRenterId,
    string RepresentativeName,
    DateOnly? SignedDate,
    string? SignedPlace,
    DateOnly? EffectiveDate,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? ActualEndDate,
    DateOnly? NoticeGivenDate,
    DateOnly? PlannedMoveOutDate,
    int NoticeDays,
    decimal DepositAmount,
    string? DepositTerms,
    BillingSettingsInput Billing,
    PaymentMethod[] PaymentMethods,
    int CopiesCount,
    string? TermsText,
    string? Note,
    decimal? CurrentRent,
    bool IsOverdue,
    LessorSnapshotDto? Lessor,
    RoomSnapshotDto? RoomAtSigning,
    PartySnapshotDto? RepresentativeAtSigning,
    string? HouseRulesSnapshot,
    TerminationReason? TerminationReason,
    TerminationGround? TerminationGround,
    string? TerminationNote,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    IReadOnlyList<RentTermDto> RentTerms,
    IReadOnlyList<OccupantDto> Occupants,
    IReadOnlyList<AssetDto> Assets,
    IReadOnlyList<VehicleDto> Vehicles,
    ContractDocumentDto Document,
    IReadOnlyList<Warning> Warnings,
    Guid? HouseholdHeadRenterId,
    IReadOnlyList<ContractFeeDto> Fees,
    IReadOnlyList<UtilityPriceItem> UtilityPrices,
    IReadOnlyList<ContractFlag> Flags,
    DateOnly? HoldoverSince,
    string? HoldoverNote,
    Guid? PreviousContractId,
    string Version,
    bool HasSignedDocument = false,
    string? SignedDocumentNote = null,
    DateOnly? BillingStartDate = null,
    ContractDepositDto? Deposit = null,
    DateOnly? RoomSince = null,
    IReadOnlyList<RoomMoveDto>? RoomMoves = null);

/// <summary>
/// M08 PM-BR-32..35: cọc chỉ theo dõi. <paramref name="Status"/> null = HĐ không cọc; <paramref name="NotRefundedReminder"/> = HĐ đã kết thúc mà
/// chưa đánh dấu hoàn cọc ("Chưa hoàn cọc").
/// </summary>
public sealed record ContractDepositDto(
    decimal Amount, DepositStatus? Status, DateOnly? RefundedOn, decimal? RefundedAmount, string? Note, bool NotRefundedReminder);

/// <summary>CT-BR-14: phòng HĐ đã ở trước khi chuyển (ngày chuyển đi vẫn tính là ở).</summary>
public sealed record RoomMoveDto(Guid RoomId, string RoomCode, DateOnly FromDate, DateOnly ToDate);

/// <summary>Văn bản hợp đồng (CT-BR-25): loại, tiêu đề, điều khoản, định nghĩa trường (chụp từ mẫu) và giá trị đã nhập.</summary>
public sealed record ContractDocumentDto(
    Guid? TemplateId,
    ContractType ContractType,
    string Title,
    IReadOnlyList<ContractClause> Clauses,
    IReadOnlyList<CustomFieldDefinition> CustomFieldDefinitions,
    IReadOnlyDictionary<string, JsonElement> CustomFields)
{
    public static ContractDocumentDto From(Contract contract) => new(
        contract.TemplateId,
        contract.ContractType,
        contract.Title ?? ContractTypes.DefaultTitle(contract.ContractType),
        ContractDocumentJson.Clauses(contract.Clauses),
        ContractDocumentJson.Fields(contract.CustomFieldDefinitions),
        ContractDocumentJson.Values(contract.CustomFieldValues));
}

/// <summary>
/// CT-BR-19 / LEG-01: bản chụp lúc ký — đủ thông tin để in lại hợp đồng đúng như đã ký (Luật Nhà ở 2023 Điều 163):
/// bên cho thuê, bên thuê (người đứng tên), mô tả phòng, địa chỉ khu, tài khoản nhận tiền. Lưu jsonb, BẤT BIẾN.
/// Số giấy tờ giữ dạng mã hóa (base64), không bao giờ trả ra API.
/// </summary>
public sealed record SigningSnapshot(
    LessorPart Lessor,
    PartyPart? Representative,   // null: hợp đồng kích hoạt trước khi có bản chụp bên thuê
    RoomPart? Room,
    BankAccount? BankAccount,
    string PropertyName,
    string PropertyAddress,
    DateTimeOffset CapturedAt)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static SigningSnapshot From(Property property, LessorDetails lessor, Room room, Renter representative, DateTimeOffset now) => new(
        new LessorPart(
            lessor.Type, lessor.Name, lessor.Address, lessor.Phone, lessor.Email, lessor.IdType,
            lessor.IdNumberEncrypted is null ? null : Convert.ToBase64String(lessor.IdNumberEncrypted),
            lessor.IdNumberLast4, lessor.IdIssueDate, lessor.IdIssuePlace, lessor.DateOfBirth, lessor.TaxCode,
            lessor.RepresentativeName, lessor.RepresentativeTitle, lessor.AuthorizationDocNo, lessor.AuthorizationDocDate),
        new PartyPart(
            representative.FullName, representative.DateOfBirth, representative.IdType,
            representative.IdNumberEncrypted is null ? null : Convert.ToBase64String(representative.IdNumberEncrypted), representative.IdNumberLast4,
            representative.IdIssueDate, representative.IdIssuePlace, representative.PermanentAddress, representative.Phone),
        new RoomPart(room.Code, room.Floor, room.AreaM2, room.MaxOccupants),
        property.BankAccount,
        property.Name,
        property.Address.FullText,
        now);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>RT-BR-06: bên thuê chỉ còn tên ẩn danh — xóa ngày sinh, giấy tờ, nơi cấp, địa chỉ, SĐT.</summary>
    public SigningSnapshot WithAnonymizedRepresentative(string anonymizedName) => this with
    {
        Representative = Representative is null ? null
            : new PartyPart(anonymizedName, DateOnly.MinValue, null, null, null, null, null, null, null)
    };

    public static SigningSnapshot? FromJson(string? json) => json is null ? null : JsonSerializer.Deserialize<SigningSnapshot>(json, Json);

    public LessorSnapshotDto ToLessorDto() => new(
        Lessor.Type, Lessor.Name, Lessor.Address, Lessor.Phone, Lessor.Email, Lessor.IdType, Mask(Lessor.IdNumberLast4),
        Lessor.IdIssueDate, Lessor.IdIssuePlace, Lessor.DateOfBirth, Lessor.TaxCode, Lessor.RepresentativeName,
        Lessor.RepresentativeTitle, Lessor.AuthorizationDocNo, Lessor.AuthorizationDocDate, BankAccount, PropertyName,
        PropertyAddress, CapturedAt);

    public PartySnapshotDto? ToRepresentativeDto() => Representative is null ? null : new(
        Representative.FullName, Representative.DateOfBirth, Representative.IdType, Mask(Representative.IdNumberLast4),
        Representative.IdIssueDate, Representative.IdIssuePlace, Representative.PermanentAddress, Representative.Phone);

    public RoomSnapshotDto? ToRoomDto() => Room is null ? null : new(Room.Code, Room.Floor, Room.AreaM2, Room.MaxOccupants);

    private static string? Mask(string? last4) => last4 is null ? null : $"********{last4}";
}

public sealed record LessorPart(
    LessorType Type, string Name, string Address, string Phone, string? Email, IdDocumentType? IdType, string? IdNumberEncrypted,
    string? IdNumberLast4, DateOnly? IdIssueDate, string? IdIssuePlace, DateOnly? DateOfBirth, string? TaxCode,
    string? RepresentativeName, string? RepresentativeTitle, string? AuthorizationDocNo, DateOnly? AuthorizationDocDate);

public sealed record PartyPart(
    string FullName, DateOnly DateOfBirth, IdDocumentType? IdType, string? IdNumberEncrypted, string? IdNumberLast4,
    DateOnly? IdIssueDate, string? IdIssuePlace, string? PermanentAddress, string? Phone);

public sealed record RoomPart(string Code, string? Floor, decimal? AreaM2, int? MaxOccupants);

public sealed record LessorSnapshotDto(
    LessorType Type,
    string Name,
    string Address,
    string Phone,
    string? Email,
    IdDocumentType? IdType,
    string? IdNumberMasked,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    DateOnly? DateOfBirth,
    string? TaxCode,
    string? RepresentativeName,
    string? RepresentativeTitle,
    string? AuthorizationDocNo,
    DateOnly? AuthorizationDocDate,
    BankAccount? BankAccount,
    string PropertyName,
    string PropertyAddress,
    DateTimeOffset CapturedAt);

public sealed record PartySnapshotDto(
    string FullName, DateOnly DateOfBirth, IdDocumentType? IdType, string? IdNumberMasked, DateOnly? IdIssueDate,
    string? IdIssuePlace, string? PermanentAddress, string? Phone);

public sealed record RoomSnapshotDto(string Code, string? Floor, decimal? AreaM2, int? MaxOccupants);
