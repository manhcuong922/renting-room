using renting_room.Application.Contracts;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.UnitTests.Domain;

public sealed class RenterAnonymizationTests
{
    private static Renter NewRenter() => Renter.Create(
        new RenterProfile("Trần Thị Lan", new DateOnly(2000, 4, 15), Gender.Female, "0911111111", "lan@example.com", "VN",
            new DateOnly(2020, 1, 1), "Cục CSQLHC", "Nam Định", "Sinh viên", "ĐH Bách Khoa", "Mẹ", "0922222222", "Ghi chú"),
        new ProtectedIdNumber(IdDocumentType.CitizenId, [1, 2, 3], new string('a', 64), "1234"));

    [Fact]
    public void Anonymize_ErasesIdentifyingData_KeepsIdGenderNationality()
    {
        var renter = NewRenter();
        var now = DateTimeOffset.UtcNow;

        renter.Anonymize(now);

        renter.IsAnonymized.Should().BeTrue();
        renter.AnonymizedAt.Should().Be(now);
        renter.ArchivedAt.Should().Be(now);
        renter.FullName.Should().Be(Renter.AnonymizedName(renter.Id)).And.StartWith("Đã ẩn danh #");
        renter.DateOfBirth.Should().Be(DateOnly.MinValue);
        new object?[] { renter.Phone, renter.Email, renter.IdIssueDate, renter.IdIssuePlace, renter.PermanentAddress, renter.Occupation,
            renter.Workplace, renter.EmergencyContactName, renter.EmergencyContactPhone, renter.Note }.Should().AllBeEquivalentTo((object?)null);
        new object?[] { renter.IdType, renter.IdNumberEncrypted, renter.IdNumberHash, renter.IdNumberLast4 }
            .Should().AllBeEquivalentTo((object?)null, "xóa hẳn giấy tờ — cùng CCCD đăng ký lại là hồ sơ mới");
        renter.Gender.Should().Be(Gender.Female);
        renter.Nationality.Should().Be("VN");
    }

    [Fact]
    public void AnonymizedRenter_CannotBeUpdated_AndAnonymizeIsIdempotent()
    {
        var renter = NewRenter();
        var first = DateTimeOffset.UtcNow;
        renter.Anonymize(first);
        renter.Anonymize(first.AddDays(1));

        renter.AnonymizedAt.Should().Be(first);
        var update = () => renter.Update(
            new RenterProfile("Tên mới", new DateOnly(2000, 1, 1), Gender.Female, null, null, "VN", null, null, null, null, null, null, null, null),
            new ProtectedIdNumber(IdDocumentType.CitizenId, [9], new string('b', 64), "9999"));
        update.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SigningSnapshot_AnonymizedRepresentative_KeepsOnlyName()
    {
        var snapshot = new SigningSnapshot(
            new LessorPart(default, "Chủ trọ", "Hà Nội", "0900000000", null, null, null, null, null, null, null, null, null, null, null, null),
            new PartyPart("Trần Thị Lan", new DateOnly(2000, 4, 15), IdDocumentType.CitizenId, "AQID", "1234",
                new DateOnly(2020, 1, 1), "Cục CSQLHC", "Nam Định", "0911111111"),
            null, null, "Khu A", "Hà Nội", DateTimeOffset.UtcNow);

        var anonymized = SigningSnapshot.FromJson(snapshot.WithAnonymizedRepresentative("Đã ẩn danh #ABCD").ToJson())!;

        anonymized.Representative.Should().BeEquivalentTo(new PartyPart(
            "Đã ẩn danh #ABCD", DateOnly.MinValue, null, null, null, null, null, null, null));
        anonymized.Lessor.Name.Should().Be("Chủ trọ", "chỉ xóa bên thuê");
    }
}
