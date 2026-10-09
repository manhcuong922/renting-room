namespace renting_room.Application.Common.Interfaces;

/// <summary>
/// Nhật ký kiểm toán (C-10). Thay đổi entity (tạo / sửa / xóa) được ghi TỰ ĐỘNG khi SaveChanges — không cần gọi interface này.
/// Chỉ dùng cho sự kiện mà diff entity không thể hiện được: vượt sức chứa có chủ ý, xem / in / xuất dữ liệu nhạy cảm.
/// </summary>
public interface IAuditTrail
{
    /// <summary>
    /// Trong lệnh ghi: bản ghi đi kèm lần SaveChanges kế tiếp — cùng transaction, không thêm lượt gọi DB;
    /// lệnh rollback thì audit cũng không có.
    /// </summary>
    void Record(string action, string entityType, Guid? entityId, object? details = null);

    /// <summary>
    /// Thao tác chỉ đọc (không có SaveChanges để đi nhờ): đưa vào hàng đợi, luồng nền ghi theo lô.
    /// App crash đột ngột có thể mất các bản ghi chưa kịp ghi (tối đa một cửa sổ gom).
    /// </summary>
    void RecordRead(string action, string entityType, Guid? entityId, object? details = null);
}

public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";

    public const string RevealIdNumber = "RevealIdNumber";
    public const string PrintWithIdNumbers = "PrintWithIdNumbers";
    public const string ExportWithIdNumbers = "ExportWithIdNumbers";
    public const string Anonymized = "Anonymized";
    /// <summary>PR-UC-11 / RT-UC-10: một dòng cho cả lần import (số dòng) — từng bản ghi tạo ra vẫn có dòng "Created" riêng.</summary>
    public const string Import = "Import";
}

/// <summary>RT-BR-06: xóa dữ liệu cá nhân của người được ẩn danh khỏi nhật ký — gọi sau SaveChanges, trong cùng transaction.</summary>
public interface IAuditLogEraser
{
    /// <summary>
    /// Dòng audit của hồ sơ (trừ dòng "Anonymized"): bỏ toàn bộ <c>changes</c>; dòng của phiếu / phiếu thu thuộc HĐ người đó đứng tên:
    /// bỏ tên đã chụp (<c>snapshotRepresentativeName</c>, <c>payerName</c>). Bản chụp lúc ký trên HĐ vốn đã bị che trong audit.
    /// </summary>
    Task EraseRenterAsync(
        Guid renterId, IReadOnlyCollection<Guid> invoiceIds, IReadOnlyCollection<Guid> paymentIds, IReadOnlyCollection<Guid> vehicleIds,
        CancellationToken cancellationToken);
}
