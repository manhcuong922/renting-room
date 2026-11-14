using System.Text.RegularExpressions;
using renting_room.Domain.Renters;

namespace renting_room.Domain.UnitTests;

/// <summary>RT-BR-06 (05/11/2026, L-17): che họ tên / SĐT / email / số giấy tờ của chính người được ẩn danh trong ghi chú tự do.</summary>
public sealed class PersonalDataMaskTests
{
    private static string Apply(string text, params string?[] identifiers) =>
        Regex.Replace(text, PersonalDataMask.Pattern(identifiers)!, PersonalDataMask.Mask, RegexOptions.IgnoreCase);

    [Theory]
    [InlineData("gọi 0901234567", "gọi ***")]
    [InlineData("gọi 0901.234.567", "gọi ***")]
    [InlineData("gọi 0901 234 567", "gọi ***")]
    [InlineData("gọi +84 901 234 567", "gọi ***")]
    [InlineData("gọi 84901234567", "gọi ***")]
    public void Phone_AnyCommonFormat_IsMasked(string text, string expected) => Apply(text, "0901234567").Should().Be(expected);

    [Fact]
    public void NameEmailAndIdNumber_AreMasked_RestOfReasonIsKept() =>
        Apply("Chị Trần Thị Lan (lan.tran@example.com, CCCD 001190 012345) trừ cọc vì vỡ kính",
                "Trần Thị Lan", "lan.tran@example.com", "001190012345")
            .Should().Be("Chị *** (***, CCCD ***) trừ cọc vì vỡ kính");

    [Fact]
    public void RegexCharactersInName_AreLiteral() =>
        Apply("Anh A.B (cũ) chuyển khoản", "A.B (cũ)").Should().Be("Anh *** chuyển khoản");

    [Fact]
    public void NothingToMatch_ReturnsNull() =>
        PersonalDataMask.Pattern([null, " ", "ab"]).Should().BeNull();

    [Fact]
    public void OtherPeoplesNumbers_AreNotMasked() =>
        Apply("Người ở cùng: 0912000111", "0901234567").Should().Be("Người ở cùng: 0912000111");
}
