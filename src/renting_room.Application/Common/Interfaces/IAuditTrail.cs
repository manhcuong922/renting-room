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
    /// Đọc dữ liệu nhạy cảm (xem / in / xuất số giấy tờ đầy đủ — LEG-06): ghi thẳng 1 INSERT, gọi TRƯỚC khi trả dữ liệu —
    /// ghi lỗi ⇒ lệnh lỗi, không trả số / file (L-16, 05/11/2026; trước đó qua hàng đợi RAM, crash mất tới 2 giây nhật ký).
    /// </summary>
    Task RecordReadAsync(string action, string entityType, Guid? entityId, object? details, CancellationToken cancellationToken);
}

public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";

    public const string RevealIdNumber = "RevealIdNumber";
    public const string PrintWithIdNumbers = "PrintWithIdNumbers";
    public const string ExportWithIdNumbers = "ExportWithIdNumbers";
    /// <summary>M-15 (RP-BR-03): xuất danh sách người thuê không kèm số giấy tờ (vẫn có ngày sinh, SĐT, địa chỉ, liên hệ khẩn cấp).</summary>
    public const string ExportRenters = "ExportRenters";
    /// <summary>M-13: dò số giấy tờ — tìm theo số, hoặc tạo hồ sơ trùng số (409 trả họ tên). Không bao giờ ghi số đã nhập.</summary>
    public const string IdNumberLookup = "IdNumberLookup";
    /// <summary>M09 FS-BR-07: tải tệp nhạy cảm (ảnh giấy tờ…) — mỗi lần tải một dòng.</summary>
    public const string DownloadAttachment = "DownloadAttachment";
    public const string Anonymized = "Anonymized";
    public const string Import = "Import";
    /// <summary>M01 ID-BR-24: chủ trọ đổi khu được giao của quản lý khu (danh sách cũ → mới).</summary>
    public const string PropertiesAssigned = "PropertiesAssigned";
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

/// <param name="ClearContractIds">HĐ người đó đứng tên, ẩn danh tự động: xóa trắng mọi ô ghi chú tự do.</param>
/// <param name="MaskContractIds">HĐ còn lại (bằng tay: cả HĐ đứng tên): che <paramref name="Identifiers"/> bằng <c>***</c>.</param>
/// <param name="Identifiers">Họ tên (chỉ khi bằng tay), SĐT, email, số giấy tờ của chính người đó.</param>
public sealed record PersonalNoteErasure(
    Guid RenterId, IReadOnlyCollection<Guid> ClearContractIds, IReadOnlyCollection<Guid> MaskContractIds, IReadOnlyCollection<string> Identifiers);

/// <summary>
/// RT-BR-06 (05/11/2026, L-17): dữ liệu cá nhân trong ô ghi chú tự do (HĐ, người ở, phiếu, dòng phiếu, khoản phát sinh, phiếu thu…) và bản sao
/// trong nhật ký — gọi sau SaveChanges, trong cùng transaction. Ghi chú dòng người ở / đồng ý xử lý dữ liệu của chính người đó luôn xóa trắng.
/// </summary>
public interface IPersonalNoteEraser
{
    Task EraseAsync(PersonalNoteErasure erasure, CancellationToken cancellationToken);
}
