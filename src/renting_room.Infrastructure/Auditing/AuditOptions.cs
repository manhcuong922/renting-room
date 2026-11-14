using System.ComponentModel.DataAnnotations;

namespace renting_room.Infrastructure.Auditing;

/// <summary>Cấu hình nhật ký (section <c>Audit</c>).</summary>
public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>S2: nhật ký cũ hơn số năm này bị job hằng ngày xóa (mặc định 5).</summary>
    [Range(1, 50)]
    public int RetentionYears { get; set; } = 5;
}
