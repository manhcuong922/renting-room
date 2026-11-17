namespace renting_room.Domain.Attachments;

/// <summary>M09 §3.1: đối tượng nghiệp vụ mà tệp đính kèm thuộc về. Lưu dạng chữ (đã đặt tên ổn định) — không đổi tên sau khi đã có dữ liệu.</summary>
public enum AttachmentOwnerType
{
    Property,
    Room,
    Renter,
    MeterReading,
    Payment,

    /// <summary>⏸ Tạm hoãn (16/11/2026): chưa mở upload cho hợp đồng — mọi category bị từ chối.</summary>
    Contract
}
