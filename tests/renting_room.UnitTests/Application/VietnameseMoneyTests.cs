using renting_room.Application.Common.Formatting;

namespace renting_room.UnitTests.Application;

public sealed class VietnameseMoneyTests
{
    [Theory]
    [InlineData(0, "Không đồng")]
    [InlineData(5, "Năm đồng")]
    [InlineData(15, "Mười lăm đồng")]
    [InlineData(21, "Hai mươi mốt đồng")]
    [InlineData(25, "Hai mươi lăm đồng")]
    [InlineData(105, "Một trăm lẻ năm đồng")]
    [InlineData(20_000, "Hai mươi nghìn đồng")]
    [InlineData(1_000_000, "Một triệu đồng")]
    [InlineData(1_050_000, "Một triệu không trăm năm mươi nghìn đồng")]
    [InlineData(3_500_000, "Ba triệu năm trăm nghìn đồng")]
    [InlineData(1_000_005, "Một triệu không trăm lẻ năm đồng")]
    [InlineData(2_000_000_000, "Hai tỷ đồng")]
    public void ToWords_ReadsLikeVietnameseContracts(decimal amount, string expected)
    {
        VietnameseMoney.ToWords(amount).Should().Be(expected);
    }

    [Fact]
    public void Format_UsesDotThousandsSeparator()
    {
        VietnameseMoney.Format(3_500_000).Should().Be("3.500.000 đ");
    }
}
