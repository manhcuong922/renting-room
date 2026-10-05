using System.Net;
using Npgsql;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

public sealed record ContractDetail(
    Guid Id,
    string ContractNo,
    string Status,
    DateOnly? SignedDate,
    DateOnly? EffectiveDate,
    decimal? CurrentRent,
    LessorSnapshotDetail? Lessor,
    RoomSnapshotDetail? RoomAtSigning,
    PartySnapshotDetail? RepresentativeAtSigning,
    List<OccupantDetail> Occupants,
    List<RentTermDetail> RentTerms,
    string Version);

public sealed record LessorSnapshotDetail(string Name, string? IdNumberMasked, string PropertyName);

public sealed record RoomSnapshotDetail(string Code, decimal? AreaM2);

public sealed record PartySnapshotDetail(string FullName, string IdNumberMasked, string? PermanentAddress);

public sealed record OccupantDetail(Guid Id, Guid RenterId, DateOnly? MoveOutDate);

public sealed record RentTermDetail(DateOnly EffectiveFrom, decimal MonthlyRent);

public sealed record PeriodResponse(DateOnly Start, DateOnly End);

public sealed record NoticeResponse(bool ShorterThanNoticePeriod, int NoticeDays, int ActualDays);

[Collection(ApiCollection.Name)]
public sealed class ContractTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<ContractDetail> GetAsync(string token, Guid id) =>
        await (await _client.GetAsync($"/api/v1/contracts/{id}", token)).ReadAsync<ContractDetail>();

    [Fact]
    public async Task FullLifecycle_Draft_Activate_Liquidate_End()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, roomId, _, contractId) = await _client.CreateActiveContractAsync(token, today);

        var active = await GetAsync(token, contractId);
        active.Status.Should().Be("Active");
        active.ContractNo.Should().MatchRegex(@"^HD\d{4}-\d{4}$");
        active.SignedDate.Should().Be(today);
        active.EffectiveDate.Should().Be(today);
        active.CurrentRent.Should().Be(3_500_000);
        active.Lessor!.Name.Should().Be("Nguyễn Văn Chủ");
        active.Lessor.IdNumberMasked.Should().StartWith("********");

        // Báo trả phòng tháng sau: chưa hoàn tất thanh lý được trước ngày trả phòng.
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start",
            new { actualEndDate = today.AddMonths(1), reason = "MutualAgreement" }, token);
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/complete", null, token))
            .ReadProblemCodeAsync()).Should().Be("LIQUIDATION_BEFORE_END_DATE");
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/cancel", null, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Trả phòng hôm nay ⇒ hoàn tất được.
        var start = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start",
            new { actualEndDate = today, reason = "MutualAgreement" }, token);
        start.StatusCode.Should().Be(HttpStatusCode.NoContent, await start.Content.ReadAsStringAsync());
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/complete", null, token))
            .ReadProblemCodeAsync()).Should().Be("FINAL_INVOICE_REQUIRED", "phải lập và chốt phiếu quyết toán trước");
        await _client.SettleAndCompleteAsync(token, contractId);

        var ended = await GetAsync(token, contractId);
        ended.Status.Should().Be("Ended");
        ended.Occupants.Should().OnlyContain(o => o.MoveOutDate == today);

        var room = await (await _client.GetAsync($"/api/v1/rooms/{roomId}", token)).ReadAsync<RoomResponse>();
        room.Status.Should().Be("Occupied", "ngày trả phòng vẫn tính là người thuê còn ở");
    }

    [Fact]
    public async Task Activate_RequiresLessorInfo()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token, withLessor: false);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var renterId = await _client.CreateRenterAsync(token);
        var contractId = await _client.CreateContractAsync(token, roomId, renterId, TestData.Today(factory));

        var activate = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token);

        (await activate.ReadProblemCodeAsync()).Should().Be("LESSOR_INFO_INCOMPLETE");
    }

    [Fact]
    public async Task Activate_RejectsUnderageRepresentative()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var minor = await _client.CreateRenterAsync(token, dateOfBirth: TestData.Today(factory).AddYears(-16).ToString("yyyy-MM-dd"));
        var contractId = await _client.CreateContractAsync(token, roomId, minor, TestData.Today(factory));

        var activate = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token);

        (await activate.ReadProblemCodeAsync()).Should().Be("REPRESENTATIVE_UNDERAGE");
    }

    [Fact]
    public async Task TwoContracts_SameRoom_ActivatedConcurrently_OnlyOneSucceeds()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterA = await _client.CreateRenterAsync(token);
        var renterB = await _client.CreateRenterAsync(token);
        var today = TestData.Today(factory);
        var contractA = await _client.CreateContractAsync(token, roomId, renterA, today);
        var contractB = await _client.CreateContractAsync(token, roomId, renterB, today);

        var responses = await Task.WhenAll(
            factory.CreateClient().PostJsonAsync($"/api/v1/contracts/{contractA}/activate", null, token),
            factory.CreateClient().PostJsonAsync($"/api/v1/contracts/{contractB}/activate", null, token));

        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        var loser = responses.Single(r => r.StatusCode != HttpStatusCode.NoContent);
        (await loser.ReadProblemCodeAsync()).Should().Be("ROOM_PERIOD_OVERLAP");
    }

    [Fact]
    public async Task DatabaseExclusionConstraint_BlocksOverlappingContracts_EvenBypassingApi()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, roomId, renterId, _) = await _client.CreateActiveContractAsync(token, today);
        var other = await _client.CreateContractAsync(token, roomId, renterId, today);

        // Cố ý ghi thẳng DB (giả lập bug code) — EXCLUDE constraint vẫn chặn.
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE contracts SET status = 'Active', effective_date = start_date, signing_snapshot = '{}' WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", other);

        var act = () => command.ExecuteNonQueryAsync();
        (await act.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ex_contracts_room_period");
    }

    [Fact]
    public async Task NewContract_CanStartDayAfterPreviousEnds()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, roomId, renterId, first) = await _client.CreateActiveContractAsync(token, today.AddDays(-30));
        await _client.PostJsonAsync($"/api/v1/contracts/{first}/liquidation/start", new { actualEndDate = today.AddDays(-1), reason = "MutualAgreement" }, token);

        var next = await _client.CreateContractAsync(token, roomId, renterId, today);
        (await _client.PostJsonAsync($"/api/v1/contracts/{next}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Hủy thanh lý hợp đồng cũ ⇒ chồng lấn với hợp đồng mới ⇒ DB chặn.
        var cancel = await _client.PostJsonAsync($"/api/v1/contracts/{first}/liquidation/cancel", null, token);
        (await cancel.ReadProblemCodeAsync()).Should().Be("ROOM_PERIOD_OVERLAP");
    }

    [Fact]
    public async Task Occupants_CapacityAndDuplicateRules()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, renterId, contractId) = await _client.CreateActiveContractAsync(token, today); // phòng tối đa 2 người
        var second = await _client.CreateRenterAsync(token);
        var third = await _client.CreateRenterAsync(token);

        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/occupants", new { renterId = second, moveInDate = today, relationshipType = "CoTenant" }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/occupants", new { renterId = third, moveInDate = today, relationshipType = "CoTenant" }, token))
            .ReadProblemCodeAsync()).Should().Be("ROOM_CAPACITY_EXCEEDED");
        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/occupants", new { renterId, moveInDate = today }, token))
            .ReadProblemCodeAsync()).Should().Be("OCCUPANCY_OVERLAP");
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/occupants",
                new { renterId = third, moveInDate = today, relationshipType = "CoTenant", overrideCapacity = true }, token))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RentChange_OnlyFromPeriodStart()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, _, contractId) = await _client.CreateActiveContractAsync(token, today);
        var periods = await (await _client.GetAsync($"/api/v1/contracts/{contractId}/billing-periods", token)).ReadAsync<List<PeriodResponse>>();
        var nextPeriodStart = periods[1].Start;

        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/rent-terms",
                new { effectiveFrom = nextPeriodStart.AddDays(3), monthlyRent = 4_000_000 }, token))
            .ReadProblemCodeAsync()).Should().Be("NOT_PERIOD_START");

        var ok = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/rent-terms",
            new { effectiveFrom = nextPeriodStart, monthlyRent = 4_000_000, addendumNo = "PL01" }, token);
        ok.StatusCode.Should().Be(HttpStatusCode.NoContent, await ok.Content.ReadAsStringAsync());

        (await GetAsync(token, contractId)).RentTerms.Should().HaveCount(2);
    }

    [Fact]
    public async Task Notice_ShorterThan30Days_ReturnsWarning()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, _, contractId) = await _client.CreateActiveContractAsync(token, today);

        var notice = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/notice",
            new { noticeDate = today, plannedMoveOutDate = today.AddDays(10) }, token);

        var result = await notice.ReadAsync<NoticeResponse>();
        result.ShorterThanNoticePeriod.Should().BeTrue();
        result.NoticeDays.Should().Be(30);
    }

    [Fact]
    public async Task LessorUnilateralTermination_RequiresGround()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, _, contractId) = await _client.CreateActiveContractAsync(token, today);

        var missing = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start",
            new { actualEndDate = today.AddDays(30), reason = "LessorUnilateral" }, token);
        (await missing.ReadProblemCodeAsync()).Should().Be("TERMINATION_GROUND_REQUIRED");
    }

    [Fact]
    public async Task Vehicle_PlateCanOnlyBeRegisteredOnceInOrganization()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var (_, _, _, contractA) = await _client.CreateActiveContractAsync(token, today);
        var (_, _, _, contractB) = await _client.CreateActiveContractAsync(token, today);

        (await _client.PostJsonAsync($"/api/v1/contracts/{contractA}/vehicles",
            new { vehicleType = "Motorbike", plateNumber = "29-B1 123.45" }, token)).StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await _client.PostJsonAsync($"/api/v1/contracts/{contractB}/vehicles",
            new { vehicleType = "Motorbike", plateNumber = "29b112345" }, token);
        (await duplicate.ReadProblemCodeAsync()).Should().Be("PLATE_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task Assets_RecordedInDraft_ReturnRecordedWhenLiquidating()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterId = await _client.CreateRenterAsync(token);
        var contractId = await _client.CreateContractAsync(token, roomId, renterId, today);

        var assetId = await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/assets",
            new { name = "Điều hòa Daikin", quantity = 1, conditionAtHandover = "Mới 90%", valueEstimate = 6_000_000 }, token)).ReadIdAsync();
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token);

        (await (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/assets",
                new { name = "Giường", quantity = 1 }, token)).ReadProblemCodeAsync()).Should().Be("CONTRACT_NOT_DRAFT");

        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start", new { actualEndDate = today.AddDays(20), reason = "MutualAgreement" }, token);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/assets/{assetId}/return",
            new { conditionAtReturn = "Hỏng remote", compensationValue = 200_000 }, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ContractNumbers_AreUniqueUnderConcurrentCreation()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token);
        var renterId = await _client.CreateRenterAsync(token);
        var rooms = new List<Guid>();
        for (var i = 0; i < 6; i++)
            rooms.Add(await _client.CreateRoomAsync(token, propertyId));

        var ids = await Task.WhenAll(rooms.Select(roomId =>
            factory.CreateClient().CreateContractAsync(token, roomId, renterId, TestData.Today(factory))));

        var numbers = new List<string>();
        foreach (var id in ids)
            numbers.Add((await GetAsync(token, id)).ContractNo);
        numbers.Should().OnlyHaveUniqueItems().And.HaveCount(6);
    }

    [Fact]
    public async Task SigningSnapshot_KeepsTenantAndRoomAsSigned_AfterLaterEdits()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var (_, roomId, renterId, contractId) = await _client.CreateActiveContractAsync(token, TestData.Today(factory));
        var signed = await GetAsync(token, contractId);
        signed.RepresentativeAtSigning!.FullName.Should().Be("Trần Thị Lan");
        signed.RoomAtSigning!.AreaM2.Should().Be(18.5m);

        // Sửa hồ sơ người thuê và phòng SAU khi ký.
        var renter = await (await _client.GetAsync($"/api/v1/renters/{renterId}", token)).ReadAsync<RenterVersion>();
        (await _client.PutJsonAsync($"/api/v1/renters/{renterId}", new
        {
            renter = new { fullName = "Tên Đã Đổi", dateOfBirth = "2000-04-15", gender = "Female", idType = "CitizenId", permanentAddress = "Địa chỉ mới" },
            version = renter.Version
        }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var room = await (await _client.GetAsync($"/api/v1/rooms/{roomId}", token)).ReadAsync<RoomResponse>();
        (await _client.PutJsonAsync($"/api/v1/rooms/{roomId}", new { code = "MOI-01", spec = new { maxOccupants = 2, areaM2 = 25 }, version = room.Version }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await GetAsync(token, contractId);
        after.RepresentativeAtSigning!.FullName.Should().Be("Trần Thị Lan");
        after.RepresentativeAtSigning.PermanentAddress.Should().Be("Nam Định");
        after.RoomAtSigning!.Code.Should().Be(signed.RoomAtSigning.Code);
        after.RoomAtSigning.AreaM2.Should().Be(18.5m);
    }

    [Fact]
    public async Task CancellingDraft_ReleasesVehiclePlate()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterId = await _client.CreateRenterAsync(token);
        var draft = await _client.CreateContractAsync(token, roomId, renterId, today);
        (await _client.PostJsonAsync($"/api/v1/contracts/{draft}/vehicles", new { vehicleType = "Motorbike", plateNumber = "30A-999.99" }, token))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await _client.PostJsonAsync($"/api/v1/contracts/{draft}/cancel", new { reason = "Khách không đến" }, token);

        var (_, _, _, other) = await _client.CreateActiveContractAsync(token, today);
        (await _client.PostJsonAsync($"/api/v1/contracts/{other}/vehicles", new { vehicleType = "Motorbike", plateNumber = "30A99999" }, token))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ActiveContractStartingTomorrow_ShowsRoomAsReserved()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var (_, roomId, _, _) = await _client.CreateActiveContractAsync(token, TestData.Today(factory).AddDays(1));

        var room = await (await _client.GetAsync($"/api/v1/rooms/{roomId}", token)).ReadAsync<RoomResponse>();

        room.Status.Should().Be("Reserved");
    }

    [Fact]
    public async Task OverdueContract_BillingPeriodsContinuePastEndDate()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var roomId = await _client.CreateRoomAsync(token, await _client.CreatePropertyAsync(token));
        var renterId = await _client.CreateRenterAsync(token);
        var created = await _client.PostJsonAsync("/api/v1/contracts", new
        {
            roomId,
            contract = new
            {
                representativeRenterId = renterId, startDate = today.AddMonths(-3), endDate = today.AddMonths(-1),
                monthlyRent = 3_000_000, occupants = new[] { new { renterId } }
            }
        }, token);
        var contractId = await created.ReadIdAsync();
        await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token);

        var periods = await (await _client.GetAsync($"/api/v1/contracts/{contractId}/billing-periods?until={today:yyyy-MM-dd}", token))
            .ReadAsync<List<PeriodResponse>>();

        periods[^1].End.Should().BeOnOrAfter(today, "hết hạn mà chưa thanh lý thì vẫn tiếp tục thu tiền");
    }
}

public sealed record RenterVersion(Guid Id, string Version);
