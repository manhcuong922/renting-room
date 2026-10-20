namespace renting_room.Domain.Meters;

/// <summary>Loại chỉ số (M06). Cùng ngày xếp theo thứ tự khai báo: lắp → nhận phòng → định kỳ → cuối HĐ → tháo.</summary>
public enum ReadingKind
{
    Initial,   // lắp công tơ
    Handover,
    Periodic,
    Adhoc,     // kiểm tra, không dùng tính tiền
    Final,     // cuối HĐ (thanh lý)
    Removal    // tháo công tơ
}
