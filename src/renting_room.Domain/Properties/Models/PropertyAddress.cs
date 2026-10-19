namespace renting_room.Domain.Properties;

/// <summary>Địa chỉ theo mô hình 2 cấp (tỉnh – xã) từ 01/07/2025 (C-12). Mã ĐVHC tùy chọn cho tới khi seed bảng tham chiếu.</summary>
public sealed record PropertyAddress(
    string StreetAddress, string CommuneName, string ProvinceName, string? CommuneCode, string? ProvinceCode)
{
    public string FullText => $"{StreetAddress}, {CommuneName}, {ProvinceName}";
}
