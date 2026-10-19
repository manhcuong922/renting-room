namespace renting_room.Domain.Contracts;

/// <summary>
/// CT-BR-49: vai trò của người ở trên HĐ. Đồng thuê chỉ mang tính giấy tờ — có tên trên HĐ cùng người đứng tên, cùng chịu trách nhiệm khi
/// tranh chấp; không chia tiền (vẫn 1 phiếu / phòng). Người đứng tên xác định bằng <c>Contract.RepresentativeRenterId</c>.
/// </summary>
public enum OccupantRole
{
    Resident,
    CoTenant
}
