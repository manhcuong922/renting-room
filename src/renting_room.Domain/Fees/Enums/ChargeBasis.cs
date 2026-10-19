using renting_room.Domain.Contracts;

namespace renting_room.Domain.Fees;

/// <summary>Cách tính của nhóm <see cref="FeeGroup.Service"/>.</summary>
public enum ChargeBasis
{
    PerRoom,      // × 1 phòng (mạng, rác)
    PerOccupant,  // × số người ở (nước theo đầu người)
    PerUnit       // × số gói đăng ký (giữ xe: 2 xe = 2 gói)
}
