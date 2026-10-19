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

/// <param name="TransitionAdjustDays">K4: số ngày tiền phòng cộng (+) / trừ (−) ở kỳ chuyển tiếp — null = theo gợi ý.</param>
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

public sealed record UpdateBankAccountRequest(string? BankName, string? AccountNo, string? AccountName);

public sealed record UpdateHouseRulesRequest(string? Text);

public sealed record RevealedIdNumber(string IdNumber);
