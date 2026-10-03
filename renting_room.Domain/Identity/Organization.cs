using renting_room.Domain.Common;

namespace renting_room.Domain.Identity;

/// <summary>Khách hàng B2B (một chủ trọ / công ty quản lý) — đơn vị cô lập dữ liệu.</summary>
public sealed class Organization : AuditableEntity
{
    private Organization() { } // EF Core

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? ContactName { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? TaxCode { get; private set; }
    public OrganizationStatus Status { get; private set; }
    public string? SuspendedReason { get; private set; }
    public string? Note { get; private set; }
    public string? Address { get; private set; }

    /// <summary>ID-BR-15: số phó quản lý đang hoạt động tối đa.</summary>
    public int MaxManagers { get; private set; } = DefaultMaxManagers;

    public const int DefaultMaxManagers = 10;

    public static Organization Create(
        string code,
        string name,
        string? contactName,
        string? contactPhone,
        string? contactEmail,
        string? taxCode,
        string? note,
        string? address = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Organization code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Organization name is required.", nameof(name));

        return new Organization
        {
            Id = Guid.NewGuid(),
            Code = NormalizeCode(code),
            Name = name.Trim(),
            ContactName = contactName?.Trim(),
            ContactPhone = ContactNormalizer.NormalizePhone(contactPhone),
            ContactEmail = ContactNormalizer.NormalizeEmail(contactEmail),
            TaxCode = taxCode?.Trim(),
            Note = note,
            Address = TextNormalizer.TrimToNull(address),
            Status = OrganizationStatus.Active
        };
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public Result Suspend(string reason)
    {
        if (Status == OrganizationStatus.Suspended)
            return Result.Failure(IdentityErrors.OrganizationAlreadySuspended);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Suspension reason is required.", nameof(reason));

        Status = OrganizationStatus.Suspended;
        SuspendedReason = reason.Trim();
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (Status != OrganizationStatus.Suspended)
            return Result.Failure(IdentityErrors.OrganizationNotSuspended);

        Status = OrganizationStatus.Active;
        SuspendedReason = null;
        return Result.Success();
    }
}
