using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal sealed class ContractTemplateConfiguration : IEntityTypeConfiguration<ContractTemplate>
{
    public void Configure(EntityTypeBuilder<ContractTemplate> builder)
    {
        builder.ToTable("contract_templates");
        builder.HasKey(t => t.Id);
        builder.HasAlternateKey(t => new { t.OrganizationId, t.Id }).HasName("ak_contract_templates_organization_id_id");
        builder.ConfigureAuditable();

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ContractType).HasConversion<string>().HasMaxLength(24);
        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.ClausesJson).HasColumnName("clauses").HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.FieldDefinitionsJson).HasColumnName("field_definitions").HasColumnType("jsonb").IsRequired();
        builder.Ignore(t => t.IsArchived);

        builder.HasIndex(t => new { t.OrganizationId, t.Name }).IsUnique().HasDatabaseName(DbConstraints.ContractTemplateNameUnique);
    }
}
