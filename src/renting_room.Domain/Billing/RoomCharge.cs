using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

/// <summary>Phụ thu (người thuê trả thêm) hoặc bù (giảm trừ cho người thuê).</summary>
public enum RoomChargeKind
{
    Surcharge,
    Credit
}

/// <summary>
/// Khoản phát sinh của phòng đang có người thuê (M07 BL-BR-30..37): tạo ngay lúc xảy ra (hỏng khóa, mất nước…), cuối tháng tự vào phiếu của
/// HĐ đang ở phòng ngày đó. Có trạng thái đã thanh toán / đã hoàn ngay — khoản đó vẫn hiện trên phiếu nhưng không tính vào tổng.
/// Khóa theo phiếu: đã nằm trên phiếu đã chốt thì không sửa / hủy (Application kiểm vì cần trạng thái phiếu).
/// </summary>
public sealed class RoomCharge : TenantEntity
{
    public const decimal MaxAmount = 100_000_000;

    private RoomCharge() { } // EF Core

    public Guid PropertyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid ContractId { get; private set; }
    public RoomChargeKind Kind { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public DateOnly IncurredOn { get; private set; }
    public string Reason { get; private set; } = null!;

    /// <summary>Phụ thu: người thuê đã trả ngay; bù: chủ trọ đã trả ngay (BL-BR-31).</summary>
    public bool IsSettled { get; private set; }
    public DateOnly? SettledOn { get; private set; }
    public PaymentMethod? SettledMethod { get; private set; }

    /// <summary>Phiếu (nháp hoặc đã chốt) đang chứa khoản; null = chờ vào phiếu.</summary>
    public Guid? InvoiceId { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }

    /// <summary>
    /// BL-BR-38: khoản chốt điện nước khi chuyển phòng (M05 CT-BR-47) — hệ thống tạo từ công tơ phòng cũ: chỉ số đầu / cuối, sản lượng,
    /// đơn giá tại ngày chuyển. Null với khoản nhập tay.
    /// </summary>
    public Guid? MeterId { get; private set; }
    public Guid? StartReadingId { get; private set; }
    public Guid? EndReadingId { get; private set; }
    public Guid? FeeTypeId { get; private set; }
    public decimal? Quantity { get; private set; }
    public decimal? UnitPrice { get; private set; }

    /// <summary>Khoản hệ thống: không sửa / hủy tay — chỉ đánh dấu đã thanh toán, hoặc hủy cùng lần chuyển phòng.</summary>
    public bool IsSystem => MeterId is not null;

    public bool IsCancelled => CancelledAt is not null;
    public bool IsPending => InvoiceId is null && !IsCancelled;

    /// <summary>Loại dòng trên phiếu: phụ thu hoặc giảm trừ.</summary>
    public InvoiceLineType LineType => Kind == RoomChargeKind.Surcharge ? InvoiceLineType.Surcharge : InvoiceLineType.ManualDiscount;

    public static RoomCharge Create(
        Guid propertyId, Guid roomId, Guid contractId, RoomChargeKind kind, string description, decimal amount, DateOnly incurredOn, string reason)
    {
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 0) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Description and reason are required.", nameof(description));

        return new RoomCharge
        {
            Id = Guid.CreateVersion7(),
            PropertyId = propertyId,
            RoomId = roomId,
            ContractId = contractId,
            Kind = kind,
            Description = description.Trim(),
            Amount = amount,
            IncurredOn = incurredOn,
            Reason = reason.Trim()
        };
    }

    /// <summary>BL-BR-38: phụ thu điện nước phòng cũ chốt tại ngày chuyển phòng.</summary>
    public static RoomCharge CreateMeterClosing(
        Guid propertyId, Guid roomId, Guid contractId, string description, decimal amount, DateOnly incurredOn, string reason, MeterClosing closing)
    {
        var charge = Create(propertyId, roomId, contractId, RoomChargeKind.Surcharge, description, amount, incurredOn, reason);
        charge.MeterId = closing.MeterId;
        charge.StartReadingId = closing.StartReadingId;
        charge.EndReadingId = closing.EndReadingId;
        charge.FeeTypeId = closing.FeeTypeId;
        charge.Quantity = closing.Quantity;
        charge.UnitPrice = closing.UnitPrice;
        return charge;
    }

    public Result Settle(DateOnly settledOn, PaymentMethod method, DateOnly today)
    {
        if (IsCancelled)
            return Result.Failure(RoomChargeErrors.Cancelled);
        if (settledOn > today)
            return Result.Failure(RoomChargeErrors.InvalidSettledDate);
        IsSettled = true;
        SettledOn = settledOn;
        SettledMethod = method;
        return Result.Success();
    }

    public Result Unsettle()
    {
        if (IsCancelled)
            return Result.Failure(RoomChargeErrors.Cancelled);
        if (!IsSettled)
            return Result.Failure(RoomChargeErrors.NotSettled);
        IsSettled = false;
        SettledOn = null;
        SettledMethod = null;
        return Result.Success();
    }

    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (IsCancelled)
            return Result.Failure(RoomChargeErrors.Cancelled);
        CancelledAt = now;
        CancelReason = reason.Trim();
        InvoiceId = null;
        return Result.Success();
    }

    /// <summary>Sửa dòng trên nháp ⇒ sửa khoản (BL-BR-34).</summary>
    public void Update(decimal amount, string reason)
    {
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 0) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        Amount = amount;
        Reason = reason.Trim();
    }

    public void AttachTo(Guid invoiceId) => InvoiceId = invoiceId;

    /// <summary>Nháp bị xóa / phiếu bị hủy ⇒ khoản quay về chờ (BL-BR-35).</summary>
    public void Detach() => InvoiceId = null;
}

/// <summary>Đoạn đo của khoản chốt điện nước khi chuyển phòng (BL-BR-38).</summary>
public sealed record MeterClosing(Guid MeterId, Guid StartReadingId, Guid EndReadingId, Guid FeeTypeId, decimal Quantity, decimal UnitPrice);

public static class RoomChargeErrors
{
    public static readonly Error NotFound = Error.NotFound("ROOM_CHARGE_NOT_FOUND", "Không tìm thấy khoản phát sinh.");
    public static readonly Error NoTenant = Error.BusinessRule("ROOM_CHARGE_NO_TENANT",
        "Phòng không có người thuê tại ngày phát sinh — khoản phụ thu / bù chỉ ghi cho phòng đang có người thuê.");
    public static readonly Error NoOpenInvoice = Error.BusinessRule("ROOM_CHARGE_NO_OPEN_INVOICE",
        "Hợp đồng đã chốt phiếu quyết toán / đã kết thúc — không còn phiếu để đưa khoản này vào.");
    public static readonly Error Locked = Error.BusinessRule("ROOM_CHARGE_LOCKED",
        "Khoản đã nằm trên phiếu đã chốt — không sửa / hủy được (hủy phiếu trước nếu cần).");
    public static readonly Error Cancelled = Error.BusinessRule("ROOM_CHARGE_CANCELLED", "Khoản phát sinh đã hủy.");

    public static Error CreditTooHigh(decimal limit) => Error.BusinessRule("ROOM_CHARGE_CREDIT_TOO_HIGH",
            $"Các khoản bù chưa trả của phòng không vượt một tháng tiền phòng ({limit:N0}đ) — bù nhiều hơn thì trả trực tiếp rồi đánh dấu đã trả.")
        .WithDetail("limit", limit);
    public static readonly Error System = Error.BusinessRule("ROOM_CHARGE_SYSTEM",
        "Khoản chốt điện nước khi chuyển phòng do hệ thống tạo — không sửa / hủy tay; muốn đổi thì hủy chuyển phòng rồi chuyển lại.");
    public static readonly Error NotSettled = Error.BusinessRule("ROOM_CHARGE_NOT_SETTLED", "Khoản chưa đánh dấu đã thanh toán.");
    public static readonly Error InvalidSettledDate = Error.Validation("INVALID_SETTLED_DATE", "Ngày thanh toán không được ở tương lai.");
    public static readonly Error InvalidIncurredDate = Error.Validation("INVALID_INCURRED_DATE", "Ngày phát sinh không được ở tương lai.");
}
