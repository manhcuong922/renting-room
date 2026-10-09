using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Identity.Auth.ChangePassword;
using renting_room.Application.Identity.Organizations.CreateOrganization;
using renting_room.Application.Meters;
using renting_room.Application.Payments;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Identity;
using renting_room.Infrastructure.Jobs;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.Seeding;

/// <summary>Dữ liệu demo cho môi trường dev và server test (section <c>Seed:DemoData</c>). KHÔNG bật ở môi trường thật.</summary>
public sealed class DemoDataOptions
{
    public const string SectionName = "Seed:DemoData";

    public bool Enabled { get; set; }
    public string OrganizationCode { get; set; } = "DEMO";
    public string OwnerPhone { get; set; } = "0900000009";
    public string? OwnerPassword { get; set; }
    public string ManagerPhone { get; set; } = "0900000008";
    public string? ManagerPassword { get; set; }
}

/// <summary>Người thao tác khi seed — chủ trọ / phó quản lý demo hoặc SystemAdmin (tạo tổ chức).</summary>
internal sealed record SeedUser(Guid? UserId, Guid? OrganizationId, UserRole? Role) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public string? IpAddress => null;
}

/// <summary>
/// Tạo 1 tổ chức demo đủ các tình huống nghiệp vụ đã bàn (99-self-review, M05/M07/M08, RT-BR-06) — qua CHÍNH các lệnh nghiệp vụ
/// (Mediator) nên dữ liệu luôn đúng mọi quy tắc. Ngày tính theo hôm nay ⇒ chạy lúc nào cũng có phiếu quá hạn, HĐ hết hạn, HĐ cũ đủ 36 tháng…
/// Đã có tổ chức cùng mã ⇒ bỏ qua (chạy lại không nhân đôi). Một lệnh lỗi ⇒ dừng và báo rõ lệnh nào (dữ liệu dở dang: xóa DB dev rồi chạy lại).
/// Mỗi tính năng mới cần thêm kịch bản vào đây (xem docs/guides/demo-data.md).
/// </summary>
public sealed partial class DemoDataSeeder(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<DemoDataOptions> options,
    ILogger<DemoDataSeeder> logger)
{
    private readonly DemoDataOptions _options = options.Value;
    private readonly Dictionary<Guid, decimal> _meterValues = [];
    private readonly List<(Guid InvoiceId, DateOnly IssueDate, int DueDays)> _backdates = [];
    private CancellationToken _ct;
    private SeedUser _owner = null!;
    private DateOnly _today;

    /// <returns>true = đã seed; false = tổ chức demo đã có.</returns>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        _ct = cancellationToken;
        _today = clock.GetUtcNow().ToBusinessDate();
        if (string.IsNullOrWhiteSpace(_options.OwnerPassword) || string.IsNullOrWhiteSpace(_options.ManagerPassword))
            throw new InvalidOperationException("Seed:DemoData requires OwnerPassword and ManagerPassword.");

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var code = Organization.NormalizeCode(_options.OrganizationCode);
            if (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Organizations.AnyAsync(o => o.Code == code, _ct))
            {
                logger.LogInformation("Demo organization {Code} already exists — skip seeding", code);
                return false;
            }
        }

        await SeedAccountsAsync();
        await SeedOwnerLessorAsync();
        await SeedPostpaidPropertyAsync();
        await SeedPrepaidPropertyAsync();
        await SeedImportedPropertyAsync();
        await SeedBillingChangePropertyAsync();
        await ApplyBackdatesAsync();
        logger.LogInformation("Seeded demo organization {Code} (owner {Phone})", _options.OrganizationCode, _options.OwnerPhone);
        return true;
    }

    private async Task SeedAccountsAsync()
    {
        var admin = new SeedUser(null, null, UserRole.SystemAdmin);
        var organization = await Send(new CreateOrganizationCommand(_options.OrganizationCode, "Nhà trọ Demo", "Nguyễn Văn Chủ", _options.OwnerPhone,
            null, null, "Tổ chức dữ liệu mẫu để test tay", new CreateOrganizationOwner("Nguyễn Văn Chủ", _options.OwnerPhone, null),
            "Hà Nội"), admin);
        _owner = new SeedUser(organization.OwnerUserId, organization.OrganizationId, UserRole.OrgOwner);
        await Send(new ChangePasswordCommand(organization.TemporaryPassword, _options.OwnerPassword!, null), _owner);

        var manager = await Send(new Application.Identity.Members.CreateManagerCommand("Trần Phó Quản Lý", _options.ManagerPhone, null));
        await Send(new ChangePasswordCommand(manager.TemporaryPassword, _options.ManagerPassword!, null),
            new SeedUser(manager.UserId, organization.OrganizationId, UserRole.OrgManager));
    }

    // ------------------------------------------------------------------ gọi lệnh

    private async Task<TResponse> Raw<TResponse>(IRequest<TResponse> request, ICurrentUser? user = null)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().RunAs(user ?? _owner);
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, _ct);
    }

    private async Task<T> Send<T>(IRequest<Result<T>> request, ICurrentUser? user = null)
    {
        var result = await Raw(request, user);
        return result.IsSuccess ? result.Value! : throw Failed(request, result.Error!);
    }

    private async Task Send(IRequest<Result> request, ICurrentUser? user = null)
    {
        var result = await Raw(request, user);
        if (result.IsFailure)
            throw Failed(request, result.Error!);
    }

    private static InvalidOperationException Failed(object request, Error error) =>
        new($"Demo seed failed at {request.GetType().Name}: {error.Code} — {error.Message}");

    // ------------------------------------------------------------------ hợp đồng, chỉ số, phiếu

    private static string CitizenId(int n) => $"0{10_000_000_000L + n * 7_919L:D11}";

    private async Task<Guid> ContractAsync(
        Guid roomId, Guid representative, DateOnly start, decimal rent, IReadOnlyList<Guid> meters,
        IReadOnlyList<OccupantRequest>? others = null, IReadOnlyList<ContractFeeRequest>? fees = null, DateOnly? end = null, bool activate = true)
    {
        var occupants = new List<OccupantRequest> { new(representative, start, null, null, null) };
        occupants.AddRange(others ?? []);
        var input = new ContractInput(representative, start, end, null, null, null, rent, rent, "Hoàn cọc khi trả phòng đúng hạn, trừ hư hỏng",
            null, null, [PaymentMethod.Cash, PaymentMethod.BankTransfer], null, null, null, occupants, Fees: fees);
        var created = await Send(new CreateContractCommand(roomId, null, input));
        if (activate)
            await Send(new ActivateContractCommand(created.Id, meters.Select(m => new MeterReadingInput(m, null)).ToList()));
        return created.Id;
    }

    /// <summary>Chỉ số cuối kỳ: cộng thêm <paramref name="usage"/> vào số hiện tại của công tơ.</summary>
    private PeriodicReadingInput Reading(Guid meter, Guid contract, DateOnly closingPeriodStart, DateOnly readingDate, decimal usage)
    {
        _meterValues[meter] += usage;
        return new PeriodicReadingInput(meter, contract, closingPeriodStart, Min(readingDate, _today), _meterValues[meter]);
    }

    private MeterReadingInput FinalReading(Guid meter, decimal usage)
    {
        _meterValues[meter] += usage;
        return new MeterReadingInput(meter, _meterValues[meter]);
    }

    private Task SaveReadings(Guid propertyId, params PeriodicReadingInput[] readings) =>
        Send(new SaveMeterReadingsCommand(propertyId, readings));

    private async Task<InvoiceSummaryDto> InvoiceAsync(Guid contractId, DateOnly periodStart) =>
        (await Raw(new ListInvoicesQuery(null, $"{periodStart:yyyy-MM}", null, null, contractId, null))).Items.Single();

    /// <summary>Tạo phiếu nháp tháng của 1 phòng — không tạo được (thiếu chỉ số, chưa lập kỳ trước…) ⇒ dừng seed.</summary>
    private async Task<Guid> DraftAsync(Guid propertyId, Guid roomId, Guid contractId, DateOnly periodStart)
    {
        var result = await Send(new GenerateInvoicesCommand(propertyId, $"{periodStart:yyyy-MM}", [roomId], null, false));
        if (result.Created != 1)
            throw new InvalidOperationException($"Demo seed: invoice {periodStart:yyyy-MM} not created: {string.Join(", ", result.Skipped.Select(s => s.Reason))}");
        return (await InvoiceAsync(contractId, periodStart)).Id;
    }

    /// <summary>Chốt phiếu; <paramref name="issueDate"/> = ngày lập thực tế (lùi lại sau khi seed để có phiếu quá hạn / lịch sử thật).</summary>
    private async Task<InvoiceDetailDto> FinalizeAsync(Guid invoiceId, DateOnly issueDate, int dueDays = 5)
    {
        var finalized = await Send(new FinalizeInvoiceCommand(invoiceId));
        _backdates.Add((invoiceId, Min(issueDate, _today), dueDays));
        return finalized;
    }

    private Task PayAsync(Guid contractId, Guid invoiceId, decimal amount, DateOnly paidAt, PaymentMethod method = PaymentMethod.BankTransfer) =>
        Send(new RecordPaymentCommand(contractId, amount, method, Min(paidAt, _today), invoiceId, null,
            method == PaymentMethod.BankTransfer ? "FT" + invoiceId.ToString("N")[..10].ToUpperInvariant() : null, null));

    /// <summary>Tạo + chốt + (tùy) thu đủ phiếu tháng — lịch sử bình thường.</summary>
    private async Task<InvoiceDetailDto> BillAsync(
        Guid propertyId, Guid roomId, Guid contractId, DateOnly periodStart, DateOnly issueDate, decimal? paid = null, bool payInFull = true)
    {
        var invoice = await FinalizeAsync(await DraftAsync(propertyId, roomId, contractId, periodStart), issueDate);
        var amount = payInFull ? invoice.Summary.TotalAmount : paid ?? 0;
        if (amount > 0)
            await PayAsync(contractId, invoice.Summary.Id, amount, issueDate.AddDays(2));
        return invoice;
    }

    /// <summary>Lùi ngày lập / hạn thanh toán của phiếu đã chốt về đúng thời điểm thực tế (chỉ seed được phép).</summary>
    private async Task ApplyBackdatesAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (invoiceId, issueDate, dueDays) in _backdates)
        {
            var finalizedAt = new DateTimeOffset(issueDate.ToDateTime(new TimeOnly(9, 0)), VietnamTime.Offset).ToUniversalTime();
            var dueDate = issueDate.AddDays(dueDays);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE invoices SET issue_date = {issueDate}, due_date = {dueDate}, finalized_at = {finalizedAt} WHERE id = {invoiceId}", _ct);
        }
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
