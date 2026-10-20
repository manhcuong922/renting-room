namespace renting_room.Domain.Properties;

public sealed record PropertyAddress(
    string StreetAddress, string CommuneName, string ProvinceName, string? CommuneCode, string? ProvinceCode)
{
    public string FullText => $"{StreetAddress}, {CommuneName}, {ProvinceName}";
}
