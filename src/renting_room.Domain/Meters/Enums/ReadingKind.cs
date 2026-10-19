namespace renting_room.Domain.Meters;

/// <summary>Loại chỉ số (M06). Cùng ngày xếp theo thứ tự khai báo: lắp → nhận phòng → định kỳ → cuối HĐ → tháo.</summary>
public enum ReadingKind
{
    Initial,   // lắp công tơ
    Handover,  // nhận phòng — mốc bắt đầu tính của HĐ (MT-BR-13)
    Periodic,  // cuối kỳ (đợt 2, cùng M07)
    Adhoc,     // kiểm tra, không dùng tính tiền
    Final,     // cuối HĐ (thanh lý)
    Removal    // tháo công tơ
}
