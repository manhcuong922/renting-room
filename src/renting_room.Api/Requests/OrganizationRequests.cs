namespace renting_room.Requests;

/// <summary>ID-UC-03: chủ trọ tự sửa thông tin tổ chức (mã tổ chức không đổi).</summary>
public sealed record UpdateOrganizationInfoRequest(
    string Name, string? ContactName, string? ContactPhone, string? ContactEmail, string? TaxCode, string? Address);
