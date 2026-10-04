using renting_room.Domain.Common;
using System.Text.RegularExpressions;

namespace renting_room.Domain.Contracts;

public static partial class ContractWarnings
{
    public const int DepositWarningMonths = 3;

    public static readonly Warning DepositAboveThreeMonths = new("DEPOSIT_ABOVE_THREE_MONTHS",
        $"Tiền cọc lớn hơn {DepositWarningMonths} tháng tiền thuê — kiểm tra lại số tiền.");

    /// <summary>
    /// Biển số VN sau chuẩn hóa (bỏ khoảng trắng, dấu): 2 số mã tỉnh + 1–2 chữ cái sê-ri (+ 1 số) + 4–5 số.
    /// VD 29B112345 (xe máy 29-B1 123.45), 30A12345 (ô tô 30A-123.45). Biển cũ / nước ngoài / ngoại giao có thể không khớp ⇒ chỉ cảnh báo.
    /// </summary>
    [GeneratedRegex("^[0-9]{2}[A-Z]{1,2}[0-9]?[0-9]{4,5}$")]
    private static partial Regex VietnamPlate();

    public static bool IsUsualPlate(string normalizedPlate) => VietnamPlate().IsMatch(normalizedPlate);

    public static Warning UnusualPlate(string plate) => new("PLATE_FORMAT_UNUSUAL",
        $"Biển số \"{plate}\" không giống định dạng biển số Việt Nam — kiểm tra lại (biển cũ / nước ngoài thì bỏ qua).");

    public const int LessorNoticeDays = 30;

    /// <summary>HĐ không thời hạn: chấm dứt sau 90 ngày kể từ khi bên cho thuê thông báo (CT-BR-42).</summary>
    public const int IndefiniteNoticeDays = 90;

    /// <param name="parkingFees">Khoản thu giữ xe của khu (có <c>VehicleType</c>) — null ⇒ bỏ qua đối chiếu số xe (CT-BR-22).</param>
    public static IReadOnlyList<Warning> For(Contract contract, IReadOnlyCollection<Fees.FeeType>? parkingFees = null, DateOnly? today = null)
    {
        var warnings = new List<Warning>();
        if (parkingFees is { Count: > 0 } && today is { } date)
            warnings.AddRange(ParkingMismatch(contract, parkingFees, date));
        if (today is { } day)
            warnings.AddRange(contract.Flags(day).Select(f => FlagWarning(contract, f)));

        warnings.AddRange(TerminationWarnings(contract));

        var initialRent = contract.RentTerms.MinBy(t => t.EffectiveFrom)?.MonthlyRent;
        if (initialRent is { } rent && contract.DepositAmount > rent * DepositWarningMonths)
            warnings.Add(DepositAboveThreeMonths);

        warnings.AddRange(contract.Vehicles
            .Where(v => v.IsActive && v.PlateNumber is not null && !IsUsualPlate(v.PlateNumber))
            .Select(v => UnusualPlate(v.PlateNumber!)));
        return warnings;
    }

    /// <summary>CT-BR-44/45: cờ cần chủ trọ xử lý → câu cảnh báo.</summary>
    private static Warning FlagWarning(Contract contract, ContractFlag flag) => flag switch
    {
        ContractFlag.RepresentativeMovedOut => new Warning("REPRESENTATIVE_MOVED_OUT",
            "Phòng còn người ở nhưng người ký hợp đồng đã rời đi — cần ký hợp đồng mới cho người còn ở."),
        ContractFlag.NoOccupantLeft => new Warning("NO_OCCUPANT_LEFT",
            "Hợp đồng còn hiệu lực nhưng không còn ai ở — thanh lý để trả phòng?"),
        ContractFlag.ExpiredAwaitingDecision => new Warning("CONTRACT_EXPIRED_DECISION_NEEDED",
            $"Hợp đồng đã hết hạn ngày {contract.EndDate:dd/MM/yyyy} mà người thuê vẫn ở — chọn: gia hạn, cho ở tiếp chưa ký lại, hoặc thu lại phòng."),
        _ => new Warning("HOLDOVER_SIGN_ADDENDUM",
            $"Đang ở tiếp từ {contract.HoldoverSince:dd/MM/yyyy} khi hợp đồng đã hết hạn, chưa ký lại — nên ký phụ lục gia hạn.")
    };

    /// <summary>CT-BR-21, CT-BR-41, CT-BR-42: báo trước không đủ / bỏ đi không báo — chỉ cảnh báo, không chặn.</summary>
    private static IEnumerable<Warning> TerminationWarnings(Contract contract)
    {
        if (contract.ActualEndDate is not { } end)
            yield break;

        if (contract.TerminationReason == TerminationReason.LessorUnilateral && contract.LiquidationStartedOn is { } started)
        {
            var days = end.DayNumber - started.DayNumber;
            var indefinite = contract.TerminationGround == TerminationGround.IndefiniteTermNotice;
            var required = indefinite ? IndefiniteNoticeDays : LessorNoticeDays;
            if (days < required)
                yield return new Warning("LESSOR_TERMINATION_SHORT_NOTICE", indefinite
                    ? $"Hợp đồng không thời hạn chấm dứt sau {IndefiniteNoticeDays} ngày kể từ khi bên cho thuê thông báo (Luật Nhà ở 2023 Điều 171) — mới {days} ngày."
                    : $"Bên cho thuê đơn phương chấm dứt nhưng chỉ báo trước {days} ngày — luật yêu cầu ít nhất " +
                      $"{LessorNoticeDays} ngày (Luật Nhà ở 2023 Điều 172), trừ khi hai bên có thỏa thuận khác.");
        }

        if (contract.TerminationReason == TerminationReason.LesseeUnilateral
            && (contract.NoticeGivenDate is not { } notice || end.DayNumber - notice.DayNumber < contract.NoticeDays))
            yield return new Warning("LESSEE_TERMINATION_SHORT_NOTICE",
                $"Bên thuê trả phòng khi chưa báo trước đủ {contract.NoticeDays} ngày như hợp đồng — xử lý tiền cọc theo điều khoản đã ký.");

        if (contract.TerminationReason == TerminationReason.Abandoned)
            yield return new Warning("LESSEE_ABANDONED",
                "Người thuê bỏ đi không báo: lập biên bản kiểm kê đồ để lại (có người làm chứng), chốt chỉ số điện nước ngày phát hiện, " +
                "khai báo xóa tạm trú cho người ở; tiền cọc xử lý theo điều khoản đã ký.");
    }

    /// <summary>CT-BR-22: số xe đang đăng ký theo loại ≠ tổng số lượng phí giữ xe loại đó đang tính cho HĐ ⇒ cảnh báo.</summary>
    private static IEnumerable<Warning> ParkingMismatch(Contract contract, IReadOnlyCollection<Fees.FeeType> parkingFees, DateOnly today)
    {
        foreach (var group in parkingFees.Where(f => f.VehicleType is not null).GroupBy(f => f.VehicleType!.Value))
        {
            var feeIds = group.Select(f => f.Id).ToHashSet();
            var charged = contract.Fees.Where(f => feeIds.Contains(f.FeeTypeId) && f.Covers(today)).Sum(f => f.Quantity);
            var registered = contract.Vehicles.Count(v => v.IsActive && v.VehicleType == group.Key);
            if (charged != registered)
                yield return new Warning("PARKING_QUANTITY_MISMATCH",
                    $"Đăng ký {registered} {VehicleLabel(group.Key)} nhưng phí giữ xe đang tính {charged:0.##} — kiểm tra số lượng phí giữ xe.");
        }
    }

    private static string VehicleLabel(VehicleType type) => type switch
    {
        VehicleType.Motorbike => "xe máy",
        VehicleType.Bicycle => "xe đạp",
        VehicleType.ElectricBike => "xe điện",
        _ => "ô tô"
    };
}
