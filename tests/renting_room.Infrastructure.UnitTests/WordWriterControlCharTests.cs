using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using renting_room.Application.Common.Interfaces;

namespace renting_room.Infrastructure.UnitTests;

/// <summary>
/// M-25 (07/11/2026): ký tự điều khiển (Shift+Enter của Word = U+000B) trong dữ liệu đã lưu không được làm bản in HĐ ném lỗi ⇒ 500 vĩnh viễn.
/// </summary>
public sealed class WordWriterControlCharTests
{
    private static IWordDocumentWriter Writer()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=x" })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider().GetRequiredService<IWordDocumentWriter>();
    }

    [Theory]
    [InlineData("Dòng một\u000BDòng hai")]
    [InlineData("Có ký tự null\u0000ở giữa")]
    [InlineData("Form feed\u000Ctrong chữ")]
    public void Write_ControlCharactersInText_DoesNotThrow(string text)
    {
        var document = new PrintDocument(null,
        [
            new PrintParagraph(text),
            new PrintTable([text], [[text]])
        ]);

        var act = () => Writer().Write(document);

        act.Should().NotThrow();
        act().Should().NotBeEmpty();
    }
}
