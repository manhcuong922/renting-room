using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;
using renting_room.Infrastructure.Auditing;

namespace renting_room.UnitTests.Infrastructure;

public sealed class AuditPolicyTests
{
    [Theory]
    [InlineData(nameof(Renter.IdNumberEncrypted), typeof(byte[]))]
    [InlineData(nameof(Renter.IdNumberHash), typeof(string))]
    [InlineData(nameof(Property.LessorIdNumberEncrypted), typeof(byte[]))]
    [InlineData(nameof(User.PasswordHash), typeof(string))]
    [InlineData(nameof(User.SecurityStamp), typeof(Guid))]
    [InlineData(nameof(Contract.SigningSnapshot), typeof(string))]
    [InlineData("AnyBinaryColumn", typeof(byte[]))]
    public void IsSensitive_ReturnsTrue_ForIdNumbersSecretsAndBinaryColumns(string property, Type clrType) =>
        AuditPolicy.IsSensitive(property, clrType).Should().BeTrue();

    [Theory]
    [InlineData(nameof(Renter.Phone), typeof(string))]
    [InlineData(nameof(Renter.FullName), typeof(string))]
    [InlineData(nameof(Renter.IdNumberLast4), typeof(string))]
    [InlineData(nameof(Invoice.SnapshotRepresentativeName), typeof(string))]
    public void IsSensitive_ReturnsFalse_ForOrdinaryFields(string property, Type clrType) =>
        AuditPolicy.IsSensitive(property, clrType).Should().BeFalse();

    [Theory]
    [InlineData(typeof(Renter), true)]
    [InlineData(typeof(Contract), true)]
    [InlineData(typeof(User), true)]
    [InlineData(typeof(Organization), true)]
    [InlineData(typeof(RefreshToken), false)]
    [InlineData(typeof(AuditLog), false)]
    public void IsAudited_CoversDomainEntities_ExceptTechnicalOnes(Type entityType, bool expected) =>
        AuditPolicy.IsAudited(entityType).Should().Be(expected);

    [Theory]
    [InlineData("Id", true)]
    [InlineData("OrganizationId", true)]
    [InlineData("Version", true)]
    [InlineData("CreatedAt", true)]
    [InlineData("UpdatedBy", true)]
    [InlineData("Phone", false)]
    [InlineData("Status", false)]
    public void IsIgnored_SkipsColumnsAlreadyOnTheAuditRow(string property, bool expected) =>
        AuditPolicy.IsIgnored(property).Should().Be(expected);
}
