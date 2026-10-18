using renting_room;
using renting_room.Application;
using renting_room.Endpoints;
using renting_room.Infrastructure;
using renting_room.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    // API chỉ nhận JSON nhỏ — chặn body khổng lồ (mặc định Kestrel 30 MB). Upload file sẽ nâng riêng theo endpoint.
    options.Limits.MaxRequestBodySize = builder.Configuration.GetValue("Limits:MaxRequestBodyBytes", 1_048_576L);
});

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

app.UseApi();
app.MapAuthEndpoints();
app.MapAdminOrganizationEndpoints();
app.MapMemberEndpoints();
app.MapPropertyEndpoints();
app.MapOrganizationLessorEndpoints();
app.MapRoomEndpoints();
app.MapRenterEndpoints();
app.MapContractEndpoints();
app.MapContractTemplateEndpoints();
app.MapExportEndpoints();
app.MapImportEndpoints();
app.MapFeeEndpoints();
app.MapMeterEndpoints();
app.MapBillingEndpoints();
app.MapRoomChargeEndpoints();
app.MapAuditEndpoints();
app.MapDataRetentionEndpoints();

await app.Services.InitializeDatabaseAsync();
await app.RunAsync();

/// <summary>Cho phép integration test dùng WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program
{
    protected Program() { }
}
