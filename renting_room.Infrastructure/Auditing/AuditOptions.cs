using System.ComponentModel.DataAnnotations;

namespace renting_room.Infrastructure.Auditing;

/// <summary>Cấu hình ghi nền audit của thao tác đọc (section <c>Audit</c>).</summary>
public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>Số bản ghi tối đa chờ trong RAM; đầy ⇒ bản ghi mới bị bỏ và ghi log lỗi (không chặn request).</summary>
    [Range(1, 1_000_000)]
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>Đủ số này thì ghi ngay, không chờ hết <see cref="FlushInterval"/>.</summary>
    [Range(1, 10_000)]
    public int MaxBatchSize { get; set; } = 500;

    /// <summary>Thời gian gom tối đa tính từ bản ghi đầu tiên của lô — cũng là lượng audit tối đa có thể mất khi app crash.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>S2: nhật ký cũ hơn số năm này bị job hằng ngày xóa (mặc định 5).</summary>
    [Range(1, 50)]
    public int RetentionYears { get; set; } = 5;
}
