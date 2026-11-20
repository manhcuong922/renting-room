using renting_room.Application.Notifications;
using renting_room.Domain.Notifications;

namespace renting_room.Application.UnitTests;

/// <summary>M11 §17: nội dung tin thông báo chung (NT-BR-23) và kiểm đầu vào (NT-BR-21).</summary>
public sealed class AnnouncementFeatureTests
{
    [Fact]
    public void Message_HasPropertyTitleBodyAndUnsubscribeHint()
    {
        var text = AnnouncementMessageBuilder.Build("Khu A", "Tăng giá điện", "Từ 01/12/2026 giá điện 3.800đ/kWh.");

        text.Should().Be("[Khu A] Tăng giá điện\n\nTừ 01/12/2026 giá điện 3.800đ/kWh.\n\n" + InvoiceMessageBuilder.UnsubscribeHint);
        text.Length.Should().BeLessThanOrEqualTo(InvoiceMessageBuilder.MaxLength);
    }

    [Fact]
    public void Message_AtMaxLengths_FitsZaloLimit()
    {
        var text = AnnouncementMessageBuilder.Build(new string('k', 200), new string('t', Announcement.TitleMaxLength), new string('b', Announcement.BodyMaxLength));
        text.Length.Should().BeLessThanOrEqualTo(InvoiceMessageBuilder.MaxLength);
    }

    private static readonly SendAnnouncementCommandValidator Validator = new();

    [Fact]
    public void Validator_AcceptsAllRoomsAndSelectedRooms()
    {
        Validator.Validate(new SendAnnouncementCommand(Guid.NewGuid(), "Cắt nước", "Thứ 7 cắt nước 8h–11h.", null)).IsValid.Should().BeTrue();
        Validator.Validate(new SendAnnouncementCommand(Guid.NewGuid(), "Cắt nước", "Thứ 7 cắt nước 8h–11h.", [Guid.NewGuid(), Guid.NewGuid()]))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_RejectsEmptyOrTooLongText()
    {
        Validator.Validate(new SendAnnouncementCommand(Guid.NewGuid(), "  ", "Nội dung", null)).IsValid.Should().BeFalse();
        Validator.Validate(new SendAnnouncementCommand(Guid.NewGuid(), "Tiêu đề", "", null)).IsValid.Should().BeFalse();
        Validator.Validate(new SendAnnouncementCommand(Guid.NewGuid(), new string('a', 101), "Nội dung", null)).IsValid.Should().BeFalse();
        Validator.Validate(new SendAnnouncementCommand(Guid.NewGuid(), "Tiêu đề", new string('a', 1501), null)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsDuplicateOrTooManyRooms()
    {
        var room = Guid.NewGuid();
        Validator.Validate(new SendAnnouncementCommand(Guid.NewGuid(), "Tiêu đề", "Nội dung", [room, room])).IsValid.Should().BeFalse();
        var many = Enumerable.Range(0, SendAnnouncementCommandValidator.MaxRooms + 1).Select(_ => Guid.NewGuid()).ToList();
        Validator.Validate(new SendAnnouncementCommand(Guid.NewGuid(), "Tiêu đề", "Nội dung", many)).IsValid.Should().BeFalse();
    }
}
