using Microsoft.Extensions.Logging;
using renting_room.Application.Attachments;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Domain.Attachments;

namespace renting_room.Infrastructure.Seeding;

public sealed partial class DemoDataSeeder
{
    /// <summary>
    /// M09 (16/11/2026): ảnh / tài liệu mẫu để thử màn hình ngay — CCCD mặt trước + chân dung của Lê Thị Mai (nhạy cảm: không có thumbnail, mỗi lần xem ghi nhật ký),
    /// 2 ảnh phòng 101 (có thumbnail), giấy PCCC (PDF) của khu A. Lưu bằng kho đang cấu hình (Local, hoặc MinIO nếu bật).
    /// Kho không dùng được (MinIO chưa chạy) ⇒ chỉ cảnh báo và bỏ qua — dữ liệu demo còn lại vẫn đủ, app vẫn khởi động.
    /// </summary>
    private async Task SeedAttachmentsAsync()
    {
        var renter = (await Raw(new SearchRentersQuery("Le Thi Mai", null, null, 1, 1))).Items.FirstOrDefault();
        var room = (await Raw(new ListRoomsQuery(_propertyIds[0], null, null, null, "101", 1, 1))).Items.FirstOrDefault();
        if (renter is null || room is null)
            throw new InvalidOperationException("Demo seed: sample attachments need renter 'Lê Thị Mai' and room 101 of property A.");

        (AttachmentOwnerType Owner, Guid OwnerId, AttachmentCategory Category, string FileName, byte[] Content)[] samples =
        [
            (AttachmentOwnerType.Renter, renter.Id, AttachmentCategory.IdCardFront, "cccd-mat-truoc-mau.png", DemoSampleFiles.Png(480, 300, 70, 130, 180)),
            (AttachmentOwnerType.Renter, renter.Id, AttachmentCategory.Portrait, "chan-dung-mau.png", DemoSampleFiles.Png(300, 400, 160, 110, 120)),
            (AttachmentOwnerType.Room, room.Id, AttachmentCategory.RoomPhoto, "phong-101-giuong.png", DemoSampleFiles.Png(640, 480, 200, 170, 110)),
            (AttachmentOwnerType.Room, room.Id, AttachmentCategory.RoomPhoto, "phong-101-nha-tam.png", DemoSampleFiles.Png(640, 480, 90, 160, 150)),
            (AttachmentOwnerType.Property, _propertyIds[0], AttachmentCategory.ComplianceDoc, "giay-pccc-mau.pdf", DemoSampleFiles.Pdf("Giay chung nhan PCCC (mau)"))
        ];
        foreach (var (owner, ownerId, category, fileName, content) in samples)
        {
            var result = await Raw(new UploadAttachmentsCommand(owner, ownerId, category, null, [new UploadedFile(fileName, content.Length, () => new MemoryStream(content))]));
            if (result.IsSuccess)
                continue;
            if (result.Error!.Code == AttachmentErrors.StorageUnavailable.Code)
            {
                logger.LogWarning("Demo seed: file storage is unavailable ({Storage}) — sample attachments skipped", "Storage:Provider");
                return;
            }

            throw Failed(new UploadAttachmentsCommand(owner, ownerId, category, null, []), result.Error);
        }
    }
}
