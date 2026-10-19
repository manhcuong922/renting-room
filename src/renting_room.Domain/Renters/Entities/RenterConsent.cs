using renting_room.Domain.Common;

namespace renting_room.Domain.Renters;

/// <summary>
/// RT-UC-05: ghi nhận người thuê đồng ý cho chủ trọ thu thập / xử lý dữ liệu cá nhân theo một mục đích. Rút lại thì giữ dòng, ghi ngày rút.
/// Chỉ là ghi nhận — không chặn nghiệp vụ nào.
/// </summary>
public sealed class RenterConsent : TenantEntity
{
    private RenterConsent() { } // EF Core

    public Guid RenterId { get; private set; }
    public string Purpose { get; private set; } = null!;
    public ConsentMethod Method { get; private set; }
    public DateOnly GivenAt { get; private set; }
    public DateOnly? WithdrawnAt { get; private set; }
    public string? Note { get; private set; }

    public static RenterConsent Create(Guid renterId, string purpose, ConsentMethod method, DateOnly givenAt, string? note)
    {
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("Purpose is required.", nameof(purpose));

        return new RenterConsent
        {
            Id = Guid.CreateVersion7(), RenterId = renterId, Purpose = purpose.Trim(), Method = method, GivenAt = givenAt,
            Note = TextNormalizer.TrimToNull(note)
        };
    }

    public Result Withdraw(DateOnly withdrawnOn)
    {
        if (WithdrawnAt is not null)
            return Result.Failure(RenterConsentErrors.AlreadyWithdrawn);
        if (withdrawnOn < GivenAt)
            return Result.Failure(RenterConsentErrors.InvalidWithdrawDate);

        WithdrawnAt = withdrawnOn;
        return Result.Success();
    }
}
