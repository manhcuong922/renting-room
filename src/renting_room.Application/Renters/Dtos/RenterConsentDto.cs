using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

// RT-UC-05: ghi nhận đồng ý xử lý dữ liệu cá nhân
public sealed record RenterConsentDto(
    Guid Id, Guid RenterId, string Purpose, ConsentMethod Method, DateOnly GivenAt, DateOnly? WithdrawnAt, string? Note, DateTimeOffset CreatedAt);

internal static class RenterConsentMapping
{
    public static RenterConsentDto ToDto(this RenterConsent c) =>
        new(c.Id, c.RenterId, c.Purpose, c.Method, c.GivenAt, c.WithdrawnAt, c.Note, c.CreatedAt);
}
