using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;
using renting_room.Domain.Identity;
using renting_room.Domain.Notifications;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class NotificationOutboxConfiguration : IEntityTypeConfiguration<NotificationOutbox>
{
    public void Configure(EntityTypeBuilder<NotificationOutbox> builder)
    {
        builder.ToTable("notification_outbox", t =>
        {
            t.HasCheckConstraint("ck_notification_outbox_status", "status IN ('Pending','Sending','Sent','Failed','Skipped','Cancelled')");
            t.HasCheckConstraint("ck_notification_outbox_channel", "channel IN ('Zalo','Email')");
            t.HasCheckConstraint("ck_notification_outbox_kind", "kind IN ('InvoiceIssued','OverdueReminder','ManualResend')");
            t.HasCheckConstraint("ck_notification_outbox_attempts", "attempts BETWEEN 0 AND 5");
        });
        builder.HasKey(n => n.Id);
        builder.HasAlternateKey(n => new { n.OrganizationId, n.Id }).HasName("ak_notification_outbox_organization_id_id");
        builder.ConfigureAuditable();

        builder.HasOne<Organization>().WithMany().HasForeignKey(n => n.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(n => new { n.OrganizationId, n.InvoiceId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Renter>().WithMany().HasForeignKey(n => new { n.OrganizationId, n.RenterId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id }).OnDelete(DeleteBehavior.Restrict);

        builder.Property(n => n.Channel).HasConversion<string>().HasMaxLength(10);
        builder.Property(n => n.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.Step).HasColumnType("smallint");
        builder.Property(n => n.Attempts).HasColumnType("smallint");
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(n => n.Recipient).HasMaxLength(320);
        builder.Property(n => n.LastError).HasMaxLength(500);
        builder.Property(n => n.ProviderMessageId).HasMaxLength(100);
        builder.Ignore(n => n.IsTerminal);

        // NT-BR-05: không gửi trùng.
        builder.HasIndex(n => new { n.InvoiceId, n.Channel, n.Kind, n.Step }).IsUnique().HasDatabaseName("ux_notification_outbox_dedupe");
        // Dispatcher chỉ quét tin đang chờ.
        builder.HasIndex(n => n.NextAttemptAt).HasDatabaseName("ix_notification_outbox_pending").HasFilter("status = 'Pending'");
        builder.HasIndex(n => new { n.OrganizationId, n.InvoiceId }).HasDatabaseName("ix_notification_outbox_invoice");
        builder.HasIndex(n => new { n.OrganizationId, n.RenterId }).HasDatabaseName("ix_notification_outbox_renter");
    }
}

internal sealed class ZaloLinkCodeConfiguration : IEntityTypeConfiguration<ZaloLinkCode>
{
    public void Configure(EntityTypeBuilder<ZaloLinkCode> builder)
    {
        builder.ToTable("zalo_link_codes");
        builder.HasKey(c => c.Id);
        builder.ConfigureAuditable();

        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Renter>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.RenterId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id }).OnDelete(DeleteBehavior.Restrict);

        builder.Property(c => c.CodeHash).HasMaxLength(64).IsFixedLength().IsRequired();

        // Bot không biết tổ chức ⇒ hash là duy nhất trên toàn hệ thống.
        builder.HasIndex(c => c.CodeHash).IsUnique().HasDatabaseName("ux_zalo_link_codes_hash");
        builder.HasIndex(c => new { c.OrganizationId, c.RenterId }).HasDatabaseName("ix_zalo_link_codes_renter");
    }
}
