using renting_room.Domain.Notifications;

namespace renting_room.Domain.UnitTests;

/// <summary>M11 §17 (NT-BR-21, NT-BR-23): thông báo chung của khu và dòng outbox không gắn phiếu.</summary>
public sealed class AnnouncementTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TrimsTitleAndBody_AndKeepsCounts()
    {
        var propertyId = Guid.NewGuid();

        var announcement = Announcement.Create(propertyId, "  Tăng giá điện  ", "\nTừ 01/12/2026 giá điện 3.800đ/kWh.\n ", allRooms: true, roomCount: 2, recipientCount: 1);

        announcement.PropertyId.Should().Be(propertyId);
        announcement.Title.Should().Be("Tăng giá điện");
        announcement.Body.Should().Be("Từ 01/12/2026 giá điện 3.800đ/kWh.");
        announcement.AllRooms.Should().BeTrue();
        announcement.RoomCount.Should().Be(2);
        announcement.RecipientCount.Should().Be(1);
    }

    [Theory]
    [InlineData("", "Nội dung")]
    [InlineData("   ", "Nội dung")]
    [InlineData("Tiêu đề", "")]
    public void Create_RejectsEmptyTitleOrBody(string title, string body) =>
        FluentActions.Invoking(() => Announcement.Create(Guid.NewGuid(), title, body, true, 1, 1)).Should().Throw<ArgumentException>();

    [Fact]
    public void Create_RejectsTooLongTitleOrBody()
    {
        FluentActions.Invoking(() => Announcement.Create(Guid.NewGuid(), new string('a', Announcement.TitleMaxLength + 1), "x", true, 1, 1))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Announcement.Create(Guid.NewGuid(), "x", new string('a', Announcement.BodyMaxLength + 1), true, 1, 1))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Announcement.Create(Guid.NewGuid(), new string('a', 100), new string('b', 1500), true, 1, 1))
            .Should().NotThrow();
    }

    [Fact]
    public void Outbox_ForAnnouncement_HasNoInvoice()
    {
        var announcementId = Guid.NewGuid();
        var renterId = Guid.NewGuid();

        var outbox = NotificationOutbox.CreateForAnnouncement(announcementId, renterId, "chat-1", Now);

        outbox.AnnouncementId.Should().Be(announcementId);
        outbox.InvoiceId.Should().BeNull();
        outbox.RenterId.Should().Be(renterId);
        outbox.Channel.Should().Be(NotificationChannel.Zalo);
        outbox.Kind.Should().Be(NotificationKind.Announcement);
        outbox.Step.Should().Be(0);
        outbox.Status.Should().Be(NotificationStatus.Pending);
        outbox.NextAttemptAt.Should().Be(Now);
    }
}
