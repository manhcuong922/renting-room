using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Identity;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence;

/// <summary>
/// Tên constraint/index trong DB và lỗi nghiệp vụ tương ứng — khi DB chặn ghi (thường do 2 request song song vượt qua
/// bước kiểm tra trước), API vẫn trả đúng mã lỗi (VD PHONE_TAKEN, ROOM_PERIOD_OVERLAP) thay vì lỗi chung.
/// </summary>
public static class DbConstraints
{
    public const string OrganizationCodeUnique = "ux_organizations_code";
    public const string UserPhoneUnique = "ux_users_phone";
    public const string UserEmailUnique = "ux_users_email";
    public const string OrganizationOwnerUnique = "ux_users_organization_owner";
    public const string PropertyCodeUnique = "ux_properties_code";
    public const string RoomCodeUnique = "ux_rooms_code";
    public const string RenterIdNumberUnique = "ux_renters_id_number";
    public const string ContractNoUnique = "ux_contracts_contract_no";
    public const string RentTermUnique = "ux_contract_rent_terms_effective_from";
    public const string ActivePlateUnique = "ux_contract_vehicles_active_plate";
    public const string ContractTemplateNameUnique = "ux_contract_templates_name";
    public const string FeeNameUnique = "ux_fee_types_name";
    public const string FeeSystemCodeUnique = "ux_fee_types_system_code";
    public const string FeePriceDateUnique = "ux_fee_prices_effective_from";
    public const string ContractFeePeriodExclusion = "ex_contract_fees_period";
    public const string MeterActiveUnique = "ux_meters_active";
    public const string HandoverReadingUnique = "ux_meter_readings_handover";
    public const string InvoicePeriodUnique = "ux_invoices_period";
    public const string SegmentEndReadingUnique = "ux_invoice_meter_segments_end";

    /// <summary>EXCLUDE constraint tạo bằng SQL trong migration (EF không khai báo được).</summary>
    public const string ContractRoomPeriodExclusion = "ex_contracts_room_period";
    public const string OccupantPeriodExclusion = "ex_contract_occupants_period";
    public const string RoomMovePeriodExclusion = "ex_contract_room_moves_period";

    private static readonly Dictionary<string, Error> ViolationErrors = new(StringComparer.Ordinal)
    {
        [OrganizationCodeUnique] = IdentityErrors.OrganizationCodeTaken,
        [UserPhoneUnique] = IdentityErrors.PhoneTaken,
        [UserEmailUnique] = IdentityErrors.EmailTaken,
        [PropertyCodeUnique] = PropertyErrors.PropertyCodeTaken,
        [RoomCodeUnique] = PropertyErrors.RoomCodeTaken,
        [RenterIdNumberUnique] = RenterErrors.IdNumberExists,
        [ContractNoUnique] = ContractErrors.ContractNoTaken,
        [RentTermUnique] = ContractErrors.RentTermExists,
        [ActivePlateUnique] = ContractErrors.PlateAlreadyRegistered,
        [ContractTemplateNameUnique] = ContractTemplateErrors.NameTaken,
        [FeeNameUnique] = FeeErrors.NameTaken,
        [FeeSystemCodeUnique] = FeeErrors.SystemCodeTaken,
        [FeePriceDateUnique] = FeeErrors.PriceDateExists,
        [ContractFeePeriodExclusion] = ContractErrors.FeeLaterChangeExists,
        [MeterActiveUnique] = MeterErrors.AlreadyActive,
        [InvoicePeriodUnique] = Error.Conflict("INVOICE_EXISTS", "Kỳ này đã có phiếu — tải lại."),
        [SegmentEndReadingUnique] = BillingErrors.DraftStale,
        [ContractRoomPeriodExclusion] = ContractErrors.RoomPeriodOverlap,
        [OccupantPeriodExclusion] = ContractErrors.OccupantOverlap,
        [RoomMovePeriodExclusion] = ContractErrors.TransferRoomUnavailable
    };

    public static Error? FromViolation(string? constraintName) =>
        constraintName is not null && ViolationErrors.TryGetValue(constraintName, out var error) ? error : null;
}
