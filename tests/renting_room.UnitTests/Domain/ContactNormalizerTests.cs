using renting_room.Domain.Identity;

namespace renting_room.UnitTests.Domain;

public sealed class ContactNormalizerTests
{
    [Theory]
    [InlineData("0912345678", "0912345678")]
    [InlineData("+84 912 345 678", "0912345678")]
    [InlineData("84912345678", "0912345678")]
    [InlineData("0912.345.678", "0912345678")]
    [InlineData("(091) 234-5678", "0912345678")]
    [InlineData("0387654321", "0387654321")]
    public void NormalizePhone_ReturnsCanonicalForm_ForValidVietnameseMobile(string raw, string expected)
    {
        ContactNormalizer.NormalizePhone(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789")]   // đầu số 01x không còn là di động
    [InlineData("091234567")]    // thiếu 1 số
    [InlineData("09123456789")]  // thừa 1 số
    [InlineData("abc")]
    public void NormalizePhone_ReturnsNull_ForInvalidInput(string? raw)
    {
        ContactNormalizer.NormalizePhone(raw).Should().BeNull();
    }

    [Theory]
    [InlineData("  Minh@Example.COM ", "minh@example.com")]
    [InlineData("a.b+c@sub.domain.vn", "a.b+c@sub.domain.vn")]
    public void NormalizeEmail_TrimsAndLowercases(string raw, string expected)
    {
        ContactNormalizer.NormalizeEmail(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a @b.com")]
    public void NormalizeEmail_ReturnsNull_ForInvalidInput(string? raw)
    {
        ContactNormalizer.NormalizeEmail(raw).Should().BeNull();
    }
}
