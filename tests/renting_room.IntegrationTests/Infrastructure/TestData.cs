using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using renting_room.Domain.Common;

namespace renting_room.IntegrationTests.Infrastructure;

public sealed record IdResponse(Guid Id);

public sealed record Page<T>(List<T> Items, int PageNumber, int PageSize, int TotalCount)
{
    /// <summary>JSON trả về "page" — ánh xạ qua tên khác vì record không được trùng tên kiểu.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("page")] public int PageNumber { get; init; } = PageNumber;
}

/// <summary>Tạo nhanh dữ liệu nghiệp vụ qua API (khu, phòng, người thuê, hợp đồng) cho integration test.</summary>
public static class TestData
{
    public static DateOnly Today(ApiFactory factory) => factory.Clock.GetUtcNow().ToBusinessDate();

    public static string NewCitizenId() => $"0{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body, options: TestApi.Json) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> DeleteAsync(this HttpClient client, string url, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client.SendAsync(request);
    }

    public static async Task<Guid> ReadIdAsync(this HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<IdResponse>()).Id;
    }

    public static object Address() => new { streetAddress = "Số 5 ngõ 10 Trần Duy Hưng", communeName = "Phường Cầu Giấy", provinceName = "Hà Nội" };

    public static async Task<Guid> CreatePropertyAsync(this HttpClient client, string token, bool withLessor = true, int anchorDay = 5)
    {
        var id = await (await client.PostJsonAsync("/api/v1/properties", new
        {
            code = $"K{Random.Shared.Next(100_000, 999_999)}",
            name = "Khu trọ test",
            address = Address(),
            billingDefaults = new { anchorDay, chargeMode = "Prepaid", paymentDueDays = 5, prorationMode = "Daily", noticeDays = 30 }
        }, token)).ReadIdAsync();

        if (withLessor)
        {
            var lessor = await client.PutJsonAsync($"/api/v1/properties/{id}/lessor", new
            {
                type = "Individual",
                name = "Nguyễn Văn Chủ",
                address = "Số 1 Láng Hạ, Hà Nội",
                phone = "0911222333",
                idType = "CitizenId",
                idNumber = NewCitizenId(),
                dateOfBirth = "1980-05-20"
            }, token);
            lessor.StatusCode.Should().Be(HttpStatusCode.OK, await lessor.Content.ReadAsStringAsync());
        }

        return id;
    }

    public static async Task<Guid> CreateRoomAsync(
        this HttpClient client, string token, Guid propertyId, string? code = null, int maxOccupants = 2, decimal listedRent = 3_500_000) =>
        await (await client.PostJsonAsync($"/api/v1/properties/{propertyId}/rooms", new
        {
            code = code ?? $"P{Random.Shared.Next(100, 999_999)}",
            spec = new { maxOccupants, listedRent, areaM2 = 18.5m, amenities = new[] { "wc_private" } }
        }, token)).ReadIdAsync();

    public static async Task<Guid> CreateRenterAsync(
        this HttpClient client, string token, string? idNumber = null, string dateOfBirth = "2000-04-15", string? phone = "0987654321") =>
        await (await client.PostJsonAsync("/api/v1/renters", new
        {
            fullName = "Trần Thị Lan",
            dateOfBirth,
            gender = "Female",
            phone,
            nationality = "VN",
            idType = "CitizenId",
            idNumber = idNumber ?? NewCitizenId(),
            permanentAddress = "Nam Định"
        }, token)).ReadIdAsync();

    public static Task<HttpResponseMessage> PostContractAsync(
        this HttpClient client, string token, Guid roomId, Guid representativeId, DateOnly startDate, params Guid[] occupantIds) =>
        client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = representativeId,
                startDate,
                endDate = startDate.AddYears(1).AddDays(-1),
                monthlyRent = 3_500_000,
                depositAmount = 3_500_000,
                occupants = (occupantIds.Length == 0 ? [representativeId] : occupantIds)
                    .Select(id => new { renterId = id, relationshipType = id == representativeId ? null : "CoTenant" }).ToArray()
            }
        }, token);

    public static async Task<Guid> CreateContractAsync(
        this HttpClient client, string token, Guid roomId, Guid representativeId, DateOnly startDate, params Guid[] occupantIds) =>
        await (await client.PostContractAsync(token, roomId, representativeId, startDate, occupantIds)).ReadIdAsync();

    /// <summary>Khu + phòng + người thuê + hợp đồng đã kích hoạt.</summary>
    public static async Task<(Guid PropertyId, Guid RoomId, Guid RenterId, Guid ContractId)> CreateActiveContractAsync(
        this HttpClient client, string token, DateOnly startDate)
    {
        var propertyId = await client.CreatePropertyAsync(token);
        var roomId = await client.CreateRoomAsync(token, propertyId);
        var renterId = await client.CreateRenterAsync(token);
        var contractId = await client.CreateContractAsync(token, roomId, renterId, startDate);

        var activated = await client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token);
        activated.StatusCode.Should().Be(HttpStatusCode.NoContent, await activated.Content.ReadAsStringAsync());
        return (propertyId, roomId, renterId, contractId);
    }

    /// <summary>Id khoản thu theo tên trong khu (VD "Điện", "Nước" có sẵn khi tạo khu).</summary>
    public static async Task<Guid> FeeIdAsync(this HttpClient client, string token, Guid propertyId, string name)
    {
        var fees = await (await client.GetAsync($"/api/v1/properties/{propertyId}/fee-types", token)).ReadAsync<List<System.Text.Json.JsonElement>>();
        return fees.Single(f => f.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();
    }

    /// <summary>Lắp công tơ cho phòng (M06) — trả id công tơ.</summary>
    public static async Task<Guid> InstallMeterAsync(
        this HttpClient client, string token, Guid roomId, Guid feeTypeId, DateOnly installedDate, decimal initialValue = 0)
    {
        var response = await client.PostJsonAsync($"/api/v1/rooms/{roomId}/meters",
            new { feeTypeId, serialNo = (string?)null, installedDate, initialValue }, token);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadIdAsync();
    }
}
