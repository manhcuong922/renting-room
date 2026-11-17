using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Requests;

public sealed record UpdatePropertyRequest(
    string Name,
    AddressInput Address,
    string? Description,
    string? EvnCustomerCode,
    LandParcelInput? Land,
    uint Version);

public sealed record UpdatePropertyBillingRequest(
    int AnchorDay, ChargeMode ChargeMode, int PaymentDueDays, ProrationMode ProrationMode, int NoticeDays, int? TransitionAdjustDays,
    bool? RoundInvoiceTotal = null);

public sealed record UpdateLessorRequest(
    LessorType Type,
    string Name,
    string Address,
    string Phone,
    string? Email,
    IdDocumentType? IdType,
    string? IdNumber,
    DateOnly? IdIssueDate,
    string? IdIssuePlace,
    DateOnly? DateOfBirth,
    string? TaxCode,
    string? RepresentativeName,
    string? RepresentativeTitle,
    string? AuthorizationDocNo,
    DateOnly? AuthorizationDocDate);

/// <param name="CurrentPassword">Mật khẩu đăng nhập — bắt buộc khi đổi / xóa tài khoản (H-06).</param>
public sealed record UpdateBankAccountRequest(string? BankName, string? AccountNo, string? AccountName, string? CurrentPassword = null);

public sealed record UpdateHouseRulesRequest(string? Text);

public sealed record RevealedIdNumber(string IdNumber);

public sealed record UpdateNotificationSettingsRequest(bool NotifyOnFinalize, IReadOnlyList<int>? OverdueReminderDays);

public sealed record ResendNotificationRequest(IReadOnlyList<string>? Channels);
