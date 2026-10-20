namespace renting_room.Domain.Contracts;

/// <summary>M05 CT-BR-11: tiền phòng + dịch vụ của kỳ cuối khi trả phòng — điện nước luôn theo công tơ.</summary>
public enum FinalPeriodCharge
{
    /// <summary>Thu trọn kỳ (mặc định).</summary>
    FullPeriod,

    /// <summary>Tính theo số ngày ở.</summary>
    Daily,

    /// <summary>Không thu — người thuê có ở, chủ trọ miễn.</summary>
    None
}
