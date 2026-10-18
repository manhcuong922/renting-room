using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using renting_room.IntegrationTests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;

namespace renting_room.IntegrationTests;

/// <summary>
/// Hợp đồng API (đường dẫn, tham số, kiểu request) phải khớp <c>docs/api/openapi.json</c> — frontend sinh code từ file này.
/// Đổi API có chủ đích ⇒ cập nhật file: chạy test với biến môi trường <c>UPDATE_OPENAPI_SNAPSHOT=1</c> rồi commit kèm thay đổi.
/// Refactor (dời endpoint, đổi namespace…) mà test đỏ ⇒ đã vô tình đổi hợp đồng API.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OpenApiSnapshotTests(ApiFactory factory)
{
    private const string UpdateVariable = "UPDATE_OPENAPI_SNAPSHOT";

    [Fact]
    public async Task OpenApiDocument_MatchesDocsSnapshot()
    {
        var document = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var actual = JsonNode.Parse(await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0))!;
        var snapshotPath = SnapshotPath();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(snapshotPath, Format(actual));
            return;
        }

        var expected = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath))!;
        if (JsonNode.DeepEquals(expected, actual))
            return;

        var actualPath = Path.Combine(AppContext.BaseDirectory, "openapi.actual.json");
        await File.WriteAllTextAsync(actualPath, Format(actual));
        var differences = Differences(expected, actual);
        differences.Should().BeEmpty(
            "API sinh ra phải khớp {0} (bản sinh ra: {1}). Đổi API có chủ đích ⇒ chạy lại với {2}=1. {3} chỗ lệch:\n{4}",
            snapshotPath, actualPath, UpdateVariable, differences.Count, string.Join("\n", differences));
    }

    /// <summary>Cùng định dạng file trong docs (System.Text.Json, giữ nguyên tiếng Việt, LF kể cả trên Windows, xuống dòng cuối file) ⇒ cập nhật không sinh diff thừa.</summary>
    private static string Format(JsonNode document) =>
        document.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";

    /// <summary>Liệt kê endpoint / schema bị thêm (+), bỏ (-), đổi (~) để biết ngay chỗ lệch.</summary>
    private static List<string> Differences(JsonNode expected, JsonNode actual)
    {
        var differences = new List<string>();
        Compare("paths", expected["paths"], actual["paths"]);
        Compare("schemas", expected["components"]?["schemas"], actual["components"]?["schemas"]);
        if (differences.Count == 0)
            differences.Add("~ khác ngoài paths / schemas (info, security, tags…)");
        return differences;

        void Compare(string section, JsonNode? before, JsonNode? after)
        {
            var old = before?.AsObject() ?? [];
            var now = after?.AsObject() ?? [];
            foreach (var key in old.Select(p => p.Key).Union(now.Select(p => p.Key)).Order(StringComparer.Ordinal))
            {
                if (!now.ContainsKey(key))
                    differences.Add($"- {section} {key}");
                else if (!old.ContainsKey(key))
                    differences.Add($"+ {section} {key}");
                else if (!JsonNode.DeepEquals(old[key], now[key]))
                    differences.Add($"~ {section} {key}");
            }
        }
    }

    private static string SnapshotPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "renting_room.sln")))
            directory = directory.Parent;
        directory.Should().NotBeNull("test chạy bên trong thư mục repo");
        return Path.Combine(directory.FullName, "docs", "api", "openapi.json");
    }
}
