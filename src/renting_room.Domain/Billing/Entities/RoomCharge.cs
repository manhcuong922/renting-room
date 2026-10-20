using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Domain.Billing;

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

    public bool IsSettled { get; private set; }
    public DateOnly? SettledOn { get; private set; }
    public PaymentMethod? SettledMethod { get; private set; }

    /// <summary>Phiếu (nháp hoặc đã chốt) đang chứa khoản; null = chờ vào phiếu.</summary>
    public Guid? InvoiceId { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }

    public Guid? MeterId { get; private set; }
    public Guid? StartReadingId { get; private set; }
    public Guid? EndReadingId { get; private set; }
    public Guid? FeeTypeId { get; private set; }
    public decimal? Quantity { get; private set; }
    public decimal? UnitPrice { get; private set; }

    public Guid? SourceInvoiceId { get; private set; }
    public DateOnly? SourceUsageStart { get; private set; }

    public bool IsReadingAdjustment => SourceInvoiceId is not null;

    public bool IsSystem => MeterId is not null || IsReadingAdjustment;

    public bool IsCancelled => CancelledAt is not null;
    public bool IsPending => InvoiceId is null && !IsCancelled;

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

    /// <summary>M06 MT-BR-19 "Đẩy sang hóa đơn sau": khoản điều chỉnh của dòng điện nước <paramref name="feeTypeId"/> kỳ <paramref name="usageStart"/>.</summary>
    public static RoomCharge CreateReadingAdjustment(
        Guid propertyId, Guid roomId, Guid contractId, RoomChargeKind kind, string description, decimal amount, DateOnly incurredOn, string reason,
        Guid sourceInvoiceId, Guid feeTypeId, DateOnly usageStart)
    {
        var charge = Create(propertyId, roomId, contractId, kind, description, amount, incurredOn, reason);
        charge.SourceInvoiceId = sourceInvoiceId;
        charge.FeeTypeId = feeTypeId;
        charge.SourceUsageStart = usageStart;
        return charge;
    }

    /// <summary>Điều chỉnh lại chỉ số: cùng khoản đổi loại / số tiền (người gọi gỡ khỏi nháp trước rồi gắn lại).</summary>
    public void ReviseAdjustment(RoomChargeKind kind, decimal amount, string description, string reason)
    {
        if (!IsReadingAdjustment)
            throw new InvalidOperationException("Only reading adjustments are revised.");
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 0) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        Kind = kind;
        Amount = amount;
        Description = description.Trim();
        Reason = reason.Trim();
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

    public void Update(decimal amount, string reason)
    {
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 0) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        Amount = amount;
        Reason = reason.Trim();
    }

    public void AttachTo(Guid invoiceId) => InvoiceId = invoiceId;

    public void Detach() => InvoiceId = null;
}
