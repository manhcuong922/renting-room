using renting_room.Application.Contracts;
using renting_room.Domain.Common;

namespace renting_room.Application.UnitTests;

public sealed class SigningSnapshotTests
{
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
