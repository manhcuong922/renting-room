using renting_room.Domain.Notifications;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Domain.UnitTests;

/// <summary>M11 §12 test 1–4, 6: chính sách thử lại, nhắc nợ, vòng đời outbox, mã liên kết Zalo, cài đặt thông báo của khu.</summary>
public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    private static NotificationOutbox NewOutbox(NotificationKind kind = NotificationKind.InvoiceIssued) =>
        NotificationOutbox.Create(Guid.NewGuid(), Guid.NewGuid(), NotificationChannel.Zalo, kind, 0, "chat-1", Now);

    // ---- RetryPolicy (test 1)

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 30)]
    [InlineData(4, 120)]
    public void RetryPolicy_NextDelay_Schedule(int attempts, int minutes) =>
        RetryPolicy.NextDelay(attempts).Should().Be(TimeSpan.FromMinutes(minutes));

    [Fact]
    public void RetryPolicy_FifthAttempt_HasNoMoreRetries() => RetryPolicy.NextDelay(5).Should().BeNull();

    // ---- OverdueReminderPolicy (test 2)

    private static readonly DateOnly Due = new(2026, 11, 10);

    [Fact]
    public void DueStep_NotYetDue_IsNull() =>
        OverdueReminderPolicy.DueStep(Due, Due, [1, 3, 7], []).Should().BeNull();

    [Fact]
    public void DueStep_OnDuePlusOne_IsOne() =>
        OverdueReminderPolicy.DueStep(Due, Due.AddDays(1), [1], []).Should().Be(1);

    [Fact]
    public void DueStep_CatchUp_OnlyLargestDueStep() =>
        OverdueReminderPolicy.DueStep(Due, Due.AddDays(5), [1, 3, 7], []).Should().Be(3, "job lỡ ngày chạy bù: bỏ bước 1, không dồn nhiều tin");

    [Fact]
    public void DueStep_AlreadySent_IsNull() =>
        OverdueReminderPolicy.DueStep(Due, Due.AddDays(5), [1, 3, 7], [3]).Should().BeNull();

    [Fact]
    public void DueStep_SmallerStepSent_StillGivesNewLargerStep() =>
        OverdueReminderPolicy.DueStep(Due, Due.AddDays(8), [1, 3, 7], [1]).Should().Be(7);

    [Fact]
    public void DueStep_NoConfiguration_IsNull() =>
        OverdueReminderPolicy.DueStep(Due, Due.AddDays(30), [], []).Should().BeNull();

    // ---- NotificationOutbox (test 3)

    [Fact]
    public void Create_StartsPending_WithNextAttemptNow()
    {
        var outbox = NewOutbox();
        outbox.Status.Should().Be(NotificationStatus.Pending);
        outbox.Attempts.Should().Be(0);
        outbox.NextAttemptAt.Should().Be(Now);
    }

    [Fact]
    public void MarkSending_IncrementsAttempts_AndSetsLease()
    {
        var outbox = NewOutbox();
        outbox.MarkSending(Now, TimeSpan.FromMinutes(5));
        outbox.Status.Should().Be(NotificationStatus.Sending);
        outbox.Attempts.Should().Be(1);
        outbox.LockedUntil.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void MarkSent_RecordsProviderIdAndTime()
    {
        var outbox = NewOutbox();
        outbox.MarkSending(Now, TimeSpan.FromMinutes(5));
        outbox.MarkSent(Now.AddSeconds(2), "msg-9");
        outbox.Status.Should().Be(NotificationStatus.Sent);
        outbox.ProviderMessageId.Should().Be("msg-9");
        outbox.SentAt.Should().Be(Now.AddSeconds(2));
        outbox.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void ScheduleRetry_BacksOffOneMinuteFirst()
    {
        var outbox = NewOutbox();
        outbox.MarkSending(Now, TimeSpan.FromMinutes(5));
        outbox.ScheduleRetry(Now, "TIMEOUT", retryAfter: null);
        outbox.Status.Should().Be(NotificationStatus.Pending);
        outbox.NextAttemptAt.Should().Be(Now.AddMinutes(1));
        outbox.LastError.Should().Be("TIMEOUT");
    }

    [Fact]
    public void ScheduleRetry_UsesProviderRetryAfter()
    {
        var outbox = NewOutbox();
        outbox.MarkSending(Now, TimeSpan.FromMinutes(5));
        outbox.ScheduleRetry(Now, "RATE_LIMITED", TimeSpan.FromSeconds(42));
        outbox.NextAttemptAt.Should().Be(Now.AddSeconds(42));
    }

    [Fact]
    public void ScheduleRetry_AfterFifthAttempt_Fails()
    {
        var outbox = NewOutbox();
        for (var i = 0; i < RetryPolicy.MaxAttempts; i++)
        {
            outbox.MarkSending(Now, TimeSpan.FromMinutes(5));
            outbox.ScheduleRetry(Now, "5XX", null);
        }
        outbox.Status.Should().Be(NotificationStatus.Failed);
        outbox.Attempts.Should().Be(RetryPolicy.MaxAttempts);
    }

    [Fact]
    public void TerminalStates_CannotChange()
    {
        var outbox = NewOutbox();
        outbox.MarkSending(Now, TimeSpan.FromMinutes(5));
        outbox.MarkFailed("BLOCKED");

        outbox.Invoking(o => o.MarkSending(Now, TimeSpan.FromMinutes(5))).Should().Throw<InvalidOperationException>();
        outbox.Invoking(o => o.MarkSent(Now, "x")).Should().Throw<InvalidOperationException>();
        outbox.Cancel().Should().BeFalse();
        outbox.Status.Should().Be(NotificationStatus.Failed);
    }

    [Fact]
    public void Cancel_OnlyWhilePending()
    {
        var pending = NewOutbox();
        pending.Cancel().Should().BeTrue();
        pending.Status.Should().Be(NotificationStatus.Cancelled);

        var sending = NewOutbox();
        sending.MarkSending(Now, TimeSpan.FromMinutes(5));
        sending.Cancel().Should().BeFalse("tin đang gửi không thu hồi được");
        sending.Status.Should().Be(NotificationStatus.Sending);
    }

    [Fact]
    public void MarkSkipped_FromSending()
    {
        var outbox = NewOutbox(NotificationKind.OverdueReminder);
        outbox.MarkSending(Now, TimeSpan.FromMinutes(5));
        outbox.MarkSkipped();
        outbox.Status.Should().Be(NotificationStatus.Skipped);
    }

    [Fact]
    public void ClearRecipient_RemovesPersonalData()
    {
        var outbox = NewOutbox();
        outbox.ClearRecipient();
        outbox.Recipient.Should().BeNull();
    }

    // ---- ZaloLinkCode (test 4)

    // Băm giả: đảo ngược mã (không còn chứa mã thô nhưng vẫn tất định).
    private static string Hash(string code) => "H:" + new string(ZaloLinkCode.Normalize(code).Reverse().ToArray());

    [Fact]
    public void LinkCode_Is8CharsFromUnambiguousAlphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            var (_, code) = ZaloLinkCode.Create(Guid.NewGuid(), Now, Hash);
            code.Should().HaveLength(8);
            code.Should().MatchRegex("^[A-HJ-KM-NP-Z2-9]{8}$", "bỏ 0 O 1 I L");
        }
    }

    [Fact]
    public void LinkCode_StoresHashOnly_NotRawCode()
    {
        var (entity, code) = ZaloLinkCode.Create(Guid.NewGuid(), Now, Hash);
        entity.CodeHash.Should().Be(Hash(code));
        entity.CodeHash.Should().NotContain(code);
    }

    [Fact]
    public void LinkCode_Normalize_IgnoresCaseSpacesAndDashes() =>
        ZaloLinkCode.Normalize(" k7xm-2qpd ").Should().Be("K7XM2QPD");

    [Fact]
    public void LinkCode_UsableUntilExpiry()
    {
        var (entity, _) = ZaloLinkCode.Create(Guid.NewGuid(), Now, Hash);
        entity.IsUsable(Now.AddHours(47)).Should().BeTrue();
        entity.IsUsable(entity.ExpiresAt).Should().BeFalse("hết hạn đúng thời điểm");
        entity.ExpiresAt.Should().Be(Now.Add(ZaloLinkCode.Lifetime));
    }

    [Fact]
    public void LinkCode_UsedOrRevoked_IsNotUsable()
    {
        var (used, _) = ZaloLinkCode.Create(Guid.NewGuid(), Now, Hash);
        used.MarkUsed(Now).Should().BeTrue();
        used.IsUsable(Now).Should().BeFalse();
        used.MarkUsed(Now).Should().BeFalse("dùng một lần");

        var (revoked, _) = ZaloLinkCode.Create(Guid.NewGuid(), Now, Hash);
        revoked.Revoke(Now);
        revoked.IsUsable(Now).Should().BeFalse();
    }

    // ---- Renter ↔ Zalo (NT-BR-15, NT-BR-16)

    private static Renter NewRenter() => Renter.Create(
        new RenterProfile("Nguyễn Văn A", new DateOnly(1995, 1, 1), Gender.Male, "0912345678", null, "VN", null, null, null, null, null, null, null, null), null);


    [Fact]
    public void Renter_LinkZalo_Overwrites_AndAnonymizeUnlinks()
    {
        var renter = NewRenter();
        renter.ZaloLinked.Should().BeFalse();
        renter.LinkZalo("chat-1", Now);
        renter.LinkZalo("chat-2", Now.AddDays(1));
        renter.ZaloChatId.Should().Be("chat-2");
        renter.ZaloLinkedAt.Should().Be(Now.AddDays(1));

        renter.Anonymize(Now.AddDays(2));
        renter.ZaloChatId.Should().BeNull();
        renter.ZaloLinkedAt.Should().BeNull();
    }

    [Fact]
    public void Renter_UnlinkZalo_Clears()
    {
        var renter = NewRenter();
        renter.LinkZalo("chat-1", Now);
        renter.UnlinkZalo();
        renter.ZaloLinked.Should().BeFalse();
    }

    // ---- Property.UpdateNotificationSettings (test 6)

    [Fact]
    public void Property_NotificationSettings_DefaultOff()
    {
        var property = TestProperty();
        property.NotifyOnFinalize.Should().BeFalse();
        property.OverdueReminderDays.Should().BeEmpty();
    }

    [Fact]
    public void Property_NotificationSettings_AcceptsValid()
    {
        var property = TestProperty();
        property.UpdateNotificationSettings(true, [1, 3, 7]).IsSuccess.Should().BeTrue();
        property.NotifyOnFinalize.Should().BeTrue();
        property.OverdueReminderDays.Should().Equal(1, 3, 7);
    }

    [Theory]
    [InlineData(1, 2, 3, 4)] // > 3 phần tử
    [InlineData(0)]          // ngoài 1–60
    [InlineData(61)]
    [InlineData(3, 3)]       // trùng
    [InlineData(7, 3)]       // không tăng dần
    public void Property_NotificationSettings_RejectsInvalid(params int[] days)
    {
        var property = TestProperty();
        var result = property.UpdateNotificationSettings(true, days);
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_REMINDER_DAYS");
        property.NotifyOnFinalize.Should().BeFalse("không đổi gì khi bị từ chối");
    }

    private static Property TestProperty() => Property.Create(
        "A", "Khu A", new PropertyAddress("1 Đường", "Phường", "Hà Nội", null, null), BillingSettings.Standard);
}
