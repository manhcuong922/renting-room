using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("contracts", t =>
        {
            t.HasCheckConstraint("ck_contracts_end_after_start", "end_date IS NULL OR end_date > start_date");
            // M08 PM-BR-33: đã hoàn trả cọc phải có ngày và số tiền 0 … cọc thỏa thuận.
            t.HasCheckConstraint("ck_contracts_deposit_refund",
                "deposit_status <> 'Refunded' OR (deposit_refunded_on IS NOT NULL AND deposit_refunded_amount BETWEEN 0 AND deposit_amount)");
            t.HasCheckConstraint("ck_contracts_actual_end", "actual_end_date IS NULL OR actual_end_date >= start_date");
            // K5: "Tính tiền từ ngày" nằm trong thời gian HĐ.
            t.HasCheckConstraint("ck_contracts_billing_start", "billing_start_date >= start_date AND (end_date IS NULL OR billing_start_date <= end_date)");
            t.HasCheckConstraint("ck_contracts_liquidation_end",
                "status NOT IN ('Liquidating','Ended') OR actual_end_date IS NOT NULL");
            t.HasCheckConstraint("ck_contracts_settings",
                "deposit_amount >= 0 AND copies_count BETWEEN 1 AND 10");
            // CT-BR-20 (Điều 164): hiệu lực không trước ngày ký. Có thể sau ngày bàn giao (dọn vào trước, ký sau).
            t.HasCheckConstraint("ck_contracts_effective_date",
                "effective_date IS NULL OR signed_date IS NULL OR effective_date >= signed_date");
            // CT-BR-19/20: hợp đồng đã kích hoạt luôn có ngày hiệu lực và snapshot bên cho thuê.
            // Bản chụp lúc ký không bắt buộc (khu chưa khai bên cho thuê vẫn kích hoạt được — chỉ cảnh báo, chốt 09/10/2026).
            t.HasCheckConstraint("ck_contracts_activated_snapshot",
                "status NOT IN ('Active','Liquidating','Ended') OR effective_date IS NOT NULL");
            t.HasCheckConstraint("ck_contracts_termination_reason", "status <> 'Ended' OR termination_reason IS NOT NULL");
        });

        builder.HasKey(c => c.Id);
        builder.HasAlternateKey(c => new { c.OrganizationId, c.Id }).HasName("ak_contracts_organization_id_id");
        builder.HasAlternateKey(c => new { c.OrganizationId, c.PropertyId, c.Id }).HasName("ak_contracts_organization_property_id");
        builder.ConfigureAuditable();

        builder.HasOne<Room>()
            .WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.PropertyId, c.RoomId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.PropertyId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Renter>()
            .WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.RepresentativeRenterId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(c => c.ContractNo).HasMaxLength(30).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.SignedPlace).HasMaxLength(200);
        builder.Property(c => c.DepositAmount).HasColumnType("numeric(18,0)");
        builder.Property(c => c.DepositTerms).HasMaxLength(5000);
        builder.Property(c => c.DepositStatus).HasConversion<string>().HasMaxLength(12);
        builder.Property(c => c.DepositRefundedAmount).HasColumnType("numeric(18,0)");
        builder.Property(c => c.DepositNote).HasMaxLength(500);
        builder.Ignore(c => c.HasDeposit);
        builder.Property(c => c.PaymentMethods)
            .HasColumnType("text[]")
            .HasConversion(
                v => v.Select(m => m.ToString()).ToArray(),
                v => v.Select(m => Enum.Parse<PaymentMethod>(m)).ToArray(),
                new ValueComparer<PaymentMethod[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, m) => HashCode.Combine(hash, m)),
                    v => v.ToArray()));
        builder.Property(c => c.TermsText).HasMaxLength(50_000);
        builder.Property(c => c.Note).HasMaxLength(2000);
        builder.Property(c => c.SigningSnapshot).HasColumnType("jsonb");
        builder.Property(c => c.HouseRulesSnapshot).HasMaxLength(20_000);
        builder.Property(c => c.SignedDocumentNote).HasMaxLength(300);
        builder.Property(c => c.UtilityPriceSnapshot).HasColumnType("jsonb");
        builder.Property(c => c.TerminationReason).HasConversion<string>().HasMaxLength(24);
        builder.Property(c => c.TerminationGround).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.TerminationNote).HasMaxLength(1000);
        builder.Property(c => c.HoldoverNote).HasMaxLength(500);
        builder.Property(c => c.CancelReason).HasMaxLength(500);

        // CT-BR-25: văn bản hợp đồng theo mẫu (chép từ mẫu, bất biến sau kích hoạt).
        builder.Property(c => c.ContractType).HasConversion<string>().HasMaxLength(24).HasDefaultValue(ContractType.RoomRental)
            .HasSentinel((ContractType)(-1));
        builder.Property(c => c.Title).HasMaxLength(200);
        builder.Property(c => c.Clauses).HasColumnType("jsonb");
        builder.Property(c => c.CustomFieldDefinitions).HasColumnType("jsonb");
        builder.Property(c => c.CustomFieldValues).HasColumnType("jsonb");
        builder.HasOne<Renter>()
            .WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.HouseholdHeadRenterId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(c => c.ReferenceRenterId);
        builder.Ignore(c => c.RoomStays);
        builder.HasOne<ContractTemplate>()
            .WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.TemplateId })
            .HasPrincipalKey(t => new { t.OrganizationId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);

        ConfigureChildren(builder);

        builder.HasIndex(c => new { c.OrganizationId, c.ContractNo }).IsUnique().HasDatabaseName(DbConstraints.ContractNoUnique);
        builder.HasIndex(c => new { c.OrganizationId, c.PropertyId, c.Status }).HasDatabaseName("ix_contracts_property_status");
        builder.HasIndex(c => new { c.OrganizationId, c.RepresentativeRenterId }).HasDatabaseName("ix_contracts_representative");
        builder.HasIndex(c => c.RoomId).HasDatabaseName("ix_contracts_room_id");
    }

    /// <summary>Con của aggregate: FK composite (organization_id, contract_id) → contracts, xóa theo cha.</summary>
    private static void ConfigureChildren(EntityTypeBuilder<Contract> builder)
    {
        builder.HasMany(c => c.RentTerms).WithOne()
            .HasForeignKey(t => new { t.OrganizationId, t.ContractId }).HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Occupants).WithOne()
            .HasForeignKey(o => new { o.OrganizationId, o.ContractId }).HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Assets).WithOne()
            .HasForeignKey(a => new { a.OrganizationId, a.ContractId }).HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Vehicles).WithOne()
            .HasForeignKey(v => new { v.OrganizationId, v.ContractId }).HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);
        // Khoản thu: FK gồm property_id ⇒ khoản thu bắt buộc cùng khu với HĐ (CT-BR-06), chặn ở DB.
        builder.HasMany(c => c.Fees).WithOne()
            .HasForeignKey(f => new { f.OrganizationId, f.PropertyId, f.ContractId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.PropertyId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.RoomMoves).WithOne()
            .HasForeignKey(m => new { m.OrganizationId, m.ContractId }).HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.RepresentativeHistory).WithOne()
            .HasForeignKey(h => new { h.OrganizationId, h.ContractId }).HasPrincipalKey(c => new { c.OrganizationId, c.Id })
            .OnDelete(DeleteBehavior.Cascade);

        foreach (var navigation in new[]
                 {
                     nameof(Contract.RentTerms), nameof(Contract.Occupants), nameof(Contract.Assets), nameof(Contract.Vehicles), nameof(Contract.Fees),
                     nameof(Contract.RoomMoves), nameof(Contract.RepresentativeHistory)
                 })
            builder.Navigation(navigation).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
