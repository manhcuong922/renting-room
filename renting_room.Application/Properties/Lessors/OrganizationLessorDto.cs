namespace renting_room.Application.Properties;

/// <param name="Prefill">Gợi ý điền form lần đầu từ thông tin liên hệ của tổ chức (tên, SĐT, địa chỉ chủ trọ).</param>
public sealed record OrganizationLessorDto(LessorDto? Lessor, LessorPrefillDto Prefill);

public sealed record LessorPrefillDto(string? Name, string? Phone, string? Address);
