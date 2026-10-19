using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

/// <summary>CT-BR-14: lịch sử chuyển phòng. EXCLUDE (room_id, daterange) tạo bằng SQL trong migration.</summary>
internal sealed class ContractRoomMoveConfiguration : IEntityTypeConfiguration<ContractRoomMove>
{
    public void Configure(EntityTypeBuilder<ContractRoomMove> builder)
    {
        builder.ToTable("contract_room_moves", t => t.HasCheckConstraint("ck_contract_room_moves_dates", "to_date >= from_date"));
        builder.HasKey(m => m.Id);
        builder.ConfigureAuditable();
        builder.HasOne<Room>().WithMany().HasForeignKey(m => m.RoomId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(m => m.UndoSnapshot).HasColumnType("jsonb");
        builder.HasIndex(m => new { m.OrganizationId, m.RoomId, m.ToDate }).HasDatabaseName("ix_contract_room_moves_room");
    }
}
