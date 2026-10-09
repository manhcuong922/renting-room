using renting_room.Domain.Common;
using renting_room.Domain.Properties;

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

    /// <summary>
    /// RT-BR-06: giữ dữ liệu cá nhân người thuê bao nhiêu tháng sau lần cuối gắn với HĐ rồi tự ẩn danh. Tối thiểu 36 tháng = thời hiệu
    /// khởi kiện tranh chấp hợp đồng 3 năm (BLDS 2015 Điều 429) — giữ chứng cứ; tối đa 120 tháng để không giữ vô thời hạn.
    /// </summary>
    public int PersonalDataRetentionMonths { get; private set; } = DefaultRetentionMonths;

    /// <summary>Tắt ⇒ job không ẩn danh tổ chức này; chủ trọ tự chịu trách nhiệm lưu giữ (audit ghi ai tắt).</summary>
    public bool AutoAnonymizeEnabled { get; private set; } = true;

    /// <summary>
    /// PR-BR-17: bên cho thuê mặc định = thông tin của chính chủ trọ, khai một lần. Khu không khai bên cho thuê riêng (công ty, người được
    /// ủy quyền…) thì dùng thông tin này khi kích hoạt / in hợp đồng.
    /// </summary>
    public LessorDetails? DefaultLessor { get; private set; }

    public const int DefaultRetentionMonths = 36;
    public const int MinRetentionMonths = 36;
    public const int MaxRetentionMonths = 120;

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
            Id = Guid.CreateVersion7(),
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

    /// <summary>Sửa không ảnh hưởng hợp đồng đã kích hoạt — hợp đồng giữ bản chụp (PR-BR-15).</summary>
    public void UpdateDefaultLessor(LessorDetails lessor) => DefaultLessor = lessor.Normalized();

    public void UpdateDataRetention(int retentionMonths, bool autoAnonymize)
    {
        if (retentionMonths is < MinRetentionMonths or > MaxRetentionMonths)
            throw new ArgumentOutOfRangeException(nameof(retentionMonths));
        PersonalDataRetentionMonths = retentionMonths;
        AutoAnonymizeEnabled = autoAnonymize;
    }

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
