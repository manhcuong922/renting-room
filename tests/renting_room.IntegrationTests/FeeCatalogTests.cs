using System.Net;
using System.Text.Json;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class FeeCatalogTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<List<JsonElement>> FeesAsync(string token, Guid propertyId, bool includeArchived = false) =>
        await (await _client.GetAsync($"/api/v1/properties/{propertyId}/fee-types?includeArchived={includeArchived}", token))
            .ReadAsync<List<JsonElement>>();

    private static Guid IdOf(IEnumerable<JsonElement> fees, string name) =>
        fees.Single(f => f.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private async Task<Guid> CreateFeeAsync(string token, Guid propertyId, object body) =>
        await (await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types", body, token)).ReadIdAsync();

    private async Task<JsonElement> ContractAsync(string token, Guid id) =>
        await (await _client.GetAsync($"/api/v1/contracts/{id}", token)).ReadAsync<JsonElement>();

    [Fact]
    public async Task NewProperty_HasElectricityAndWater_ByMeter_WithoutPrice()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var fees = await FeesAsync(owner.Tokens.AccessToken, await _client.CreatePropertyAsync(owner.Tokens.AccessToken));

        fees.Select(f => (f.GetProperty("name").GetString(), f.GetProperty("group").GetString(), f.GetProperty("systemCode").GetString()))
            .Should().Equal(("Điện", "Metered", "ELECTRICITY"), ("Nước", "Metered", "WATER"));
        fees.Should().OnlyContain(f => f.GetProperty("currentPrice").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task MeteredFeesFollowRoom_ContractsAttachOtherFees()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var fees = await FeesAsync(token, propertyId);
        var electricity = IdOf(fees, "Điện");
        var waterByMeter = IdOf(fees, "Nước");
        await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = today, unitPrice = 3500 }, token);
        await _client.PostJsonAsync($"/api/v1/fee-types/{waterByMeter}/prices", new { effectiveFrom = today, unitPrice = 15000 }, token);

        var waterPerPerson = await CreateFeeAsync(token, propertyId, new
        {
            name = "Nước theo người", group = "Service", chargeBasis = "PerOccupant", unit = "người", autoAttach = false,
            initialPrice = new { effectiveFrom = today, unitPrice = 20000 }
        });
        var parking = await CreateFeeAsync(token, propertyId, new
        {
            name = "Giữ xe máy", group = "Service", chargeBasis = "PerUnit", unit = "xe", autoAttach = false, defaultQuantity = 1,
            initialPrice = new { effectiveFrom = today, unitPrice = 100000 }
        });
        await CreateFeeAsync(token, propertyId, new
        {
            name = "Rác", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = true,
            initialPrice = new { effectiveFrom = today, unitPrice = 10000 }
        });

        // Phòng A: không gửi fees ⇒ chỉ tự gắn "Rác"; Điện / Nước theo công tơ của phòng, không nằm trong HĐ.
        var roomA = await _client.CreateContractAsync(token, await _client.CreateRoomAsync(token, propertyId), await _client.CreateRenterAsync(token), today);
        (await ContractAsync(token, roomA)).GetProperty("fees").EnumerateArray().Select(f => f.GetProperty("name").GetString())
            .Should().Equal("Rác");

        // Gắn điện theo công tơ vào HĐ ⇒ bị từ chối.
        var renterB = await _client.CreateRenterAsync(token);
        var metered = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = await _client.CreateRoomAsync(token, propertyId),
            contract = new { representativeRenterId = renterB, startDate = today, fees = new[] { new { feeTypeId = electricity } } }
        }, token);
        (await metered.ReadProblemCodeAsync()).Should().Be("FEE_METERED_FOLLOWS_ROOM");

        // Phòng B: nước theo người thay cho nước theo công tơ, 2 xe.
        var roomB = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = await _client.CreateRoomAsync(token, propertyId),
            contract = new
            {
                representativeRenterId = renterB, startDate = today, occupants = new[] { new { renterId = renterB } },
                fees = new object[]
                {
                    new { feeTypeId = waterPerPerson },
                    new { feeTypeId = parking, quantity = 2, unitPriceOverride = 80000 }
                }
            }
        }, token)).ReadIdAsync();

        var detailB = (await ContractAsync(token, roomB)).GetProperty("fees").EnumerateArray().ToList();
        detailB.Select(f => (f.GetProperty("name").GetString(), f.GetProperty("quantity").GetDecimal(), f.GetProperty("currentUnitPrice").GetDecimal()))
            .Should().BeEquivalentTo([("Nước theo người", 1m, 20000m), ("Giữ xe máy", 2m, 80000m)]);
        detailB.Single(f => f.GetProperty("name").GetString() == "Nước theo người").GetProperty("chargeBasis").GetString().Should().Be("PerOccupant");
    }

    [Fact]
    public async Task ActiveContract_ChangesFeesFromPeriodStart()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (propertyId, _, _, contractId) = await _client.CreateActiveContractAsync(token, today);
        var water = await CreateFeeAsync(token, propertyId, new
        {
            name = "Nước theo người", group = "Service", chargeBasis = "PerOccupant", unit = "người", autoAttach = false,
            initialPrice = new { effectiveFrom = today, unitPrice = 20000 }
        });
        var periods = await (await _client.GetAsync($"/api/v1/contracts/{contractId}/billing-periods", token)).ReadAsync<List<JsonElement>>();
        var nextPeriod = DateOnly.Parse(periods[1].GetProperty("start").GetString()!);
        (await _client.PutJsonAsync($"/api/v1/contracts/{contractId}/fees/{water}", new { effectiveFrom = today }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await (await _client.PutJsonAsync($"/api/v1/contracts/{contractId}/fees/{water}",
            new { unitPriceOverride = 18000, effectiveFrom = nextPeriod.AddDays(1) }, token)).ReadProblemCodeAsync()).Should().Be("NOT_PERIOD_START");
        (await _client.PutJsonAsync($"/api/v1/contracts/{contractId}/fees/{water}", new { unitPriceOverride = 18000, effectiveFrom = nextPeriod }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var fees = (await ContractAsync(token, contractId)).GetProperty("fees").EnumerateArray()
            .Where(f => f.GetProperty("feeTypeId").GetGuid() == water).ToList();
        fees.Should().HaveCount(2);
        fees[0].GetProperty("effectiveTo").GetString().Should().Be(nextPeriod.AddDays(-1).ToString("yyyy-MM-dd"));

        (await _client.DeleteAsync($"/api/v1/contracts/{contractId}/fees/{water}?effectiveFrom={nextPeriod:yyyy-MM-dd}", token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ContractAsync(token, contractId)).GetProperty("fees").EnumerateArray()
            .Count(f => f.GetProperty("feeTypeId").GetGuid() == water).Should().Be(1);
    }

    [Fact]
    public async Task Rules_NameUnique_ElectricityWarning_PriceRequired_ArchiveInUse_OtherProperty()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var electricity = IdOf(await FeesAsync(token, propertyId), "Điện");

        var duplicate = await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types",
            new { name = "  điện ", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = false }, token);
        (await duplicate.ReadProblemCodeAsync()).Should().Be("FEE_NAME_TAKEN");

        var high = await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = today, unitPrice = 5000 }, token);
        high.StatusCode.Should().Be(HttpStatusCode.Created);
        (await high.ReadAsync<JsonElement>()).GetProperty("warnings")[0].GetProperty("code").GetString().Should().Be("ELECTRICITY_PRICE_ABOVE_THRESHOLD");

        var noPrice = await _client.PostJsonAsync($"/api/v1/fee-types/{electricity}/prices", new { effectiveFrom = today.AddDays(1) }, token);
        (await noPrice.ReadProblemCodeAsync()).Should().Be("VALIDATION_FAILED");

        var garbage = await CreateFeeAsync(token, propertyId, new { name = "Rác", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = true });
        await _client.CreateContractAsync(token, await _client.CreateRoomAsync(token, propertyId), await _client.CreateRenterAsync(token), today);
        (await (await _client.PostJsonAsync($"/api/v1/fee-types/{garbage}/archive", null, token)).ReadProblemCodeAsync()).Should().Be("FEE_IN_USE");

        var otherProperty = await _client.CreatePropertyAsync(token);
        var foreignFee = IdOf(await FeesAsync(token, otherProperty), "Điện");
        var wrong = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = await _client.CreateRoomAsync(token, propertyId),
            contract = new { representativeRenterId = await _client.CreateRenterAsync(token), startDate = today, fees = new[] { new { feeTypeId = foreignFee } } }
        }, token);
        (await wrong.ReadProblemCodeAsync()).Should().Be("FEE_NOT_IN_PROPERTY");
    }

    [Fact]
    public async Task ParkingFeeQuantity_IsComparedWithRegisteredVehicles()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var propertyId = await _client.CreatePropertyAsync(token);
        var parking = await CreateFeeAsync(token, propertyId, new
        {
            name = "Giữ xe máy", group = "Service", chargeBasis = "PerUnit", unit = "xe", autoAttach = false, defaultQuantity = 1, vehicleType = "Motorbike",
            initialPrice = new { effectiveFrom = today, unitPrice = 100000 }
        });
        var renterId = await _client.CreateRenterAsync(token);
        var contractId = await (await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId = await _client.CreateRoomAsync(token, propertyId),
            contract = new
            {
                representativeRenterId = renterId, startDate = today, occupants = new[] { new { renterId } },
                fees = new[] { new { feeTypeId = parking, quantity = (decimal?)1 } }
            }
        }, token)).ReadIdAsync();

        var first = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/vehicles", new { vehicleType = "Motorbike", plateNumber = "29B112345" }, token);
        (await first.ReadAsync<JsonElement>()).GetProperty("warnings").GetArrayLength().Should().Be(0, "1 xe = phí 1 xe");

        var second = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/vehicles", new { vehicleType = "Motorbike", plateNumber = "29B154321" }, token);
        (await second.ReadAsync<JsonElement>()).GetProperty("warnings")[0].GetProperty("code").GetString().Should().Be("PARKING_QUANTITY_MISMATCH");
        (await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>()).GetProperty("warnings")
            .EnumerateArray().Select(w => w.GetProperty("code").GetString()).Should().Contain("PARKING_QUANTITY_MISMATCH");

        var notQuantity = await _client.PostJsonAsync($"/api/v1/properties/{propertyId}/fee-types",
            new { name = "Wifi", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = false, vehicleType = "Motorbike" }, token);
        (await notQuantity.ReadProblemCodeAsync()).Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task LessorUnilateralTermination_WithLessThan30DaysNotice_IsWarned()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, _, contractId) = await _client.CreateActiveContractAsync(token, today);

        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start",
            new { actualEndDate = today.AddDays(10), reason = "LessorUnilateral", ground = "RentArrears3Months" }, token);

        (await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>()).GetProperty("warnings")
            .EnumerateArray().Select(w => w.GetProperty("code").GetString()).Should().Contain("LESSOR_TERMINATION_SHORT_NOTICE");

        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/cancel", null, token);
        (await (await _client.GetAsync($"/api/v1/contracts/{contractId}", token)).ReadAsync<JsonElement>()).GetProperty("warnings")
            .EnumerateArray().Select(w => w.GetProperty("code").GetString()).Should().NotContain("LESSOR_TERMINATION_SHORT_NOTICE");
    }

    [Fact]
    public async Task CopyCatalog_CopiesCustomFees_AndFillsDefaultPrices()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var source = await _client.CreatePropertyAsync(token);
        var target = await _client.CreatePropertyAsync(token);
        await _client.PostJsonAsync($"/api/v1/fee-types/{IdOf(await FeesAsync(token, source), "Điện")}/prices", new { effectiveFrom = today, unitPrice = 3500 }, token);
        await CreateFeeAsync(token, source, new
        {
            name = "Rác", group = "Service", chargeBasis = "PerRoom", unit = "phòng", autoAttach = true, initialPrice = new { effectiveFrom = today, unitPrice = 30000 }
        });

        var copy = await (await _client.PostJsonAsync($"/api/v1/properties/{target}/fee-types/copy-from/{source}", new { includePrices = true }, token))
            .ReadAsync<JsonElement>();

        copy.GetProperty("copied").GetInt32().Should().Be(2);
        copy.GetProperty("skipped").EnumerateArray().Select(s => s.GetString()).Should().Equal("Nước");
        var fees = await FeesAsync(token, target);
        fees.Select(f => f.GetProperty("name").GetString()).Should().BeEquivalentTo("Điện", "Nước", "Rác");
        fees.Single(f => f.GetProperty("name").GetString() == "Điện").GetProperty("currentPrice").GetProperty("unitPrice").GetDecimal().Should().Be(3500);
    }
}
