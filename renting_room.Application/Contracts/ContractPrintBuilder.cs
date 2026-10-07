using System.Globalization;
using System.Text.Json;
using renting_room.Application.Common.Formatting;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Application.Contracts;

/// <summary>Dữ liệu đã giải mã / tra cứu sẵn để dựng văn bản hợp đồng (CT-UC-15).</summary>
internal sealed record ContractPrintData(
    Contract Contract,
    SigningSnapshot? Snapshot,
    string? LessorIdNumber,
    string? RepresentativeIdNumber,
    IReadOnlyList<(Renter Renter, string IdNumber, ContractOccupant Occupant)> Occupants,
    IReadOnlyList<UtilityPriceItem> AgreedFees,
    IReadOnlyDictionary<Guid, FeeType> FeeTypes);

/// <summary>
/// Dựng văn bản theo bố cục hợp đồng thuê thường dùng (Luật Nhà ở 2023 Điều 163): các bên, đối tượng thuê, thời hạn, giá & thanh toán,
/// điện nước dịch vụ, đặt cọc, điều khoản (từ mẫu), tài sản bàn giao, chữ ký; phụ lục nội quy.
/// </summary>
internal static class ContractPrintBuilder
{
    private const string Blank = "....................";

    public static PrintDocument Build(ContractPrintData d)
    {
        var c = d.Contract;
        var blocks = new List<PrintBlock>
        {
            Center("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", bold: true),
            Center("Độc lập – Tự do – Hạnh phúc", bold: true),
            Center("―――――――――――"),
            Center((c.Title ?? ContractTypes.DefaultTitle(c.ContractType)).ToUpper(CultureInfo.GetCultureInfo("vi-VN")), bold: true, size: 15),
            new PrintParagraph($"Số: {c.ContractNo}", PrintAlign.Center, Italic: true),
            Text($"Hôm nay, {DateWords(c.SignedDate)}, tại {c.SignedPlace ?? d.Snapshot?.PropertyAddress ?? Blank}, chúng tôi gồm:")
        };

        blocks.AddRange(LessorBlocks(d));
        blocks.AddRange(LesseeBlocks(d));
        blocks.Add(Text("Sau khi bàn bạc trên tinh thần tự nguyện, bình đẳng, hai bên thống nhất ký kết hợp đồng với các điều khoản sau:"));

        var article = 0;
        string Article(string title) => $"Điều {++article}. {title}";

        blocks.Add(Heading(Article(c.ContractType == ContractType.WholeHouseRental ? "Nhà cho thuê" : "Phòng cho thuê")));
        blocks.Add(Text(PremisesText(d)));

        blocks.Add(Heading(Article("Thời hạn thuê")));
        blocks.Add(Text(TermText(c)));

        blocks.Add(Heading(Article("Giá thuê và phương thức thanh toán")));
        blocks.Add(Text(RentText(c)));

        blocks.Add(Heading(Article("Tiền điện, nước và dịch vụ")));
        blocks.Add(Text(FeesText(d)));

        blocks.Add(Heading(Article("Tiền đặt cọc")));
        blocks.Add(Text(DepositText(c)));

        var customRows = CustomFieldRows(c);
        if (customRows.Count > 0)
        {
            blocks.Add(Heading(Article("Thỏa thuận bổ sung")));
            blocks.Add(new PrintTable(["Nội dung", "Thỏa thuận"], customRows));
        }

        foreach (var clause in ContractDocumentJson.Clauses(c.Clauses))
        {
            blocks.Add(Heading(Article(clause.Heading)));
            blocks.Add(Text(clause.Body));
        }

        if (c.Assets.Count > 0)
        {
            blocks.Add(Heading(Article("Tài sản, trang thiết bị bàn giao")));
            blocks.Add(new PrintTable(["STT", "Tên tài sản", "Số lượng", "Tình trạng khi bàn giao", "Giá trị ước tính"],
                c.Assets.Select((a, i) => (IReadOnlyList<string>)[
                    (i + 1).ToString(), a.Name, a.Quantity.ToString(), a.ConditionAtHandover ?? "",
                    a.ValueEstimate is { } v ? VietnameseMoney.Format(v) : ""]).ToList()));
        }

        var vehicles = c.Vehicles.Where(v => v.IsActive).ToList();
        if (vehicles.Count > 0)
        {
            blocks.Add(Heading(Article("Xe gửi")));
            blocks.Add(Text(string.Join('\n', vehicles.Select(v =>
                $"- {VehicleLabel(v.VehicleType)}{(v.PlateNumber is null ? "" : $" biển số {v.PlateNumber}")}{(v.BrandColor is null ? "" : $", {v.BrandColor}")}"))));
        }

        if (c.TermsText is not null)
        {
            blocks.Add(Heading(Article("Thỏa thuận khác")));
            blocks.Add(Text(c.TermsText));
        }

        blocks.Add(Heading(Article("Hiệu lực hợp đồng")));
        blocks.Add(Text($"Hợp đồng có hiệu lực kể từ {DateOrBlank(c.EffectiveDate ?? c.SignedDate)}. " +
            $"Hợp đồng được lập thành {c.CopiesCount:00} bản có giá trị pháp lý như nhau, mỗi bên giữ ít nhất 01 bản."));

        blocks.Add(new PrintSignatures("ĐẠI DIỆN BÊN B", "ĐẠI DIỆN BÊN A", d.Snapshot?.Representative?.FullName, d.Snapshot?.Lessor.Name));

        if (c.HouseRulesSnapshot is not null)
        {
            blocks.Add(new PrintPageBreak());
            blocks.Add(Center("PHỤ LỤC: NỘI QUY KHU TRỌ", bold: true));
            blocks.Add(Text(c.HouseRulesSnapshot));
        }

        var watermark = c.Status == ContractStatus.Draft
            ? "BẢN NHÁP — chưa kích hoạt, thông tin các bên lấy theo hồ sơ hiện tại"
            : c.Status == ContractStatus.Cancelled ? "HỢP ĐỒNG ĐÃ HỦY" : null;
        return new PrintDocument(watermark, blocks);
    }

    // ------------------------------------------------------------------ Các bên

    private static IEnumerable<PrintBlock> LessorBlocks(ContractPrintData d)
    {
        yield return Heading("BÊN CHO THUÊ (BÊN A):");
        var l = d.Snapshot?.Lessor;
        if (l is null)
        {
            yield return Text($"Ông/bà: {Blank}   (khu trọ chưa khai báo bên cho thuê)");
            yield break;
        }

        if (l.Type == LessorType.Organization)
        {
            yield return Text($"Tổ chức: {l.Name}\nMã số thuế: {l.TaxCode ?? Blank}\n" +
                $"Người đại diện: {l.RepresentativeName ?? Blank}   Chức vụ: {l.RepresentativeTitle ?? Blank}" +
                (l.AuthorizationDocNo is null ? "" : $"\nGiấy ủy quyền số: {l.AuthorizationDocNo}, ngày {DateOrBlank(l.AuthorizationDocDate)}"));
        }
        else
        {
            yield return Text($"Ông/bà: {l.Name}   Sinh ngày: {DateOrBlank(l.DateOfBirth)}\n" +
                $"{IdLabel(l.IdType)} số: {d.LessorIdNumber ?? Blank}, cấp ngày {DateOrBlank(l.IdIssueDate)} tại {l.IdIssuePlace ?? Blank}");
        }
        yield return Text($"Địa chỉ: {l.Address}\nĐiện thoại: {l.Phone}" +
            (d.Snapshot!.BankAccount is { } bank ? $"\nTài khoản nhận tiền: {bank.AccountNo} – {bank.BankName} – {bank.AccountName}" : ""));
    }

    private static IEnumerable<PrintBlock> LesseeBlocks(ContractPrintData d)
    {
        yield return Heading("BÊN THUÊ (BÊN B):");
        var r = d.Snapshot?.Representative;
        yield return r is null
            ? Text($"Ông/bà: {Blank}")
            : Text($"Ông/bà: {r.FullName}   Sinh ngày: {DateOrBlank(r.DateOfBirth)}\n" +
                $"{IdLabel(r.IdType)} số: {d.RepresentativeIdNumber ?? Blank}, cấp ngày {DateOrBlank(r.IdIssueDate)} tại {r.IdIssuePlace ?? Blank}\n" +
                $"Nơi thường trú: {r.PermanentAddress ?? Blank}\nĐiện thoại: {r.Phone ?? Blank}");

        var others = d.Occupants.Where(o => o.Renter.Id != d.Contract.RepresentativeRenterId).ToList();
        if (others.Count == 0)
            yield break;

        yield return Text("Những người cùng ở:");
        yield return new PrintTable(["STT", "Họ tên", "Ngày sinh", "Số giấy tờ", "Quan hệ với chủ hộ"],
            d.Occupants.Select((o, i) => (IReadOnlyList<string>)[
                (i + 1).ToString(), o.Renter.FullName, Date(o.Renter.DateOfBirth), o.IdNumber,
                o.Renter.Id == d.Contract.ReferenceRenterId ? "Chủ hộ" : OccupantRelationshipLabels.Describe(o.Occupant.RelationshipType, o.Occupant.Relationship) ?? ""])
                .ToList());
    }

    // ------------------------------------------------------------------ Điều khoản

    private static string PremisesText(ContractPrintData d)
    {
        var room = d.Snapshot?.Room;
        var address = d.Snapshot?.PropertyAddress ?? Blank;
        var details = room is null ? "" :
            $"phòng {room.Code}{(room.Floor is null ? "" : $", tầng {room.Floor}")}{(room.AreaM2 is { } a ? $", diện tích {Number(a)} m²" : "")}, " +
            $"số người ở tối đa {room.MaxOccupants} người, ";
        return d.Contract.ContractType == ContractType.WholeHouseRental
            ? $"Bên A cho bên B thuê nhà tại địa chỉ: {address} ({d.Snapshot?.PropertyName}). Mục đích: theo thỏa thuận tại hợp đồng này."
            : $"Bên A đồng ý cho bên B thuê {details}tại {d.Snapshot?.PropertyName}, địa chỉ: {address}. Mục đích thuê: để ở.";
    }

    private static string TermText(Contract c) =>
        (c.EndDate is { } end
            ? $"Thời hạn thuê từ ngày {Date(c.StartDate)} đến hết ngày {Date(end)}."
            : $"Thời hạn thuê từ ngày {Date(c.StartDate)}, không xác định thời hạn.") +
        $"\nBên muốn chấm dứt hợp đồng trước thời hạn phải báo trước cho bên kia ít nhất {c.NoticeDays} ngày.";

    private static string RentText(Contract c)
    {
        var terms = c.RentTerms.OrderBy(t => t.EffectiveFrom).ToList();
        var first = terms[0].MonthlyRent;
        var lines = new List<string>
        {
            $"- Giá thuê: {VietnameseMoney.Format(first)}/tháng (Bằng chữ: {VietnameseMoney.ToWords(first)})."
        };
        lines.AddRange(terms.Skip(1).Select(t =>
            $"- Từ ngày {Date(t.EffectiveFrom)}{(t.AddendumNo is null ? "" : $" (phụ lục {t.AddendumNo})")}: {VietnameseMoney.Format(t.MonthlyRent)}/tháng."));
        lines.Add($"- Kỳ thanh toán: hằng tháng, ngày chốt kỳ là ngày {c.BillingAnchorDay}; " +
            (c.ChargeMode == ChargeMode.Prepaid ? "tiền thuê trả trước vào đầu mỗi kỳ" : "tiền thuê trả vào cuối mỗi kỳ") +
            $"; hạn thanh toán trong {c.PaymentDueDays} ngày kể từ ngày nhận thông báo tiền phòng.");
        lines.Add($"- Hình thức thanh toán: {string.Join(" hoặc ", c.PaymentMethods.Select(PaymentLabel))}.");
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Thỏa thuận lúc ký (bản chụp đơn giá CT-BR-19) + các thay đổi sau đó theo kỳ (phụ lục khoản thu) — giống cách in giá thuê.
    /// Khoản lúc ký chưa có giá ⇒ lấy giá của khu có hiệu lực tại ngày bắt đầu.
    /// </summary>
    private static string FeesText(ContractPrintData d)
    {
        var c = d.Contract;
        var lines = d.AgreedFees.Select(item =>
        {
            var fallback = item.UnitPrice is null && d.FeeTypes.TryGetValue(item.FeeTypeId, out var type) ? type.ResolvePrice(c.StartDate) : null;
            return "- " + Describe(item.Name, item.Group, item.ChargeBasis, item.Unit, item.Quantity, item.UnitPrice ?? fallback?.UnitPrice,
                item.UnitPrice is null ? fallback?.Tiers : item.Tiers);
        }).ToList();

        var agreed = d.AgreedFees.Select(a => a.FeeTypeId).ToHashSet();
        // Công tơ lắp sau khi ký ⇒ khoản điện nước chưa có trong bản chụp: in theo giá khu tại ngày bắt đầu.
        lines.AddRange(d.FeeTypes.Values.Where(t => t.Group == FeeGroup.Metered && !agreed.Contains(t.Id)).OrderBy(t => t.SortOrder)
            .Select(t => "- " + Describe(t.Name, t.Group, null, t.Unit, 1, t.ResolvePrice(c.StartDate)?.UnitPrice, t.ResolvePrice(c.StartDate)?.Tiers)));
        foreach (var fee in c.Fees.Where(f => (f.EffectiveFrom > c.StartDate || !agreed.Contains(f.FeeTypeId)) && d.FeeTypes.ContainsKey(f.FeeTypeId))
                     .OrderBy(f => f.EffectiveFrom))
        {
            var type = d.FeeTypes[fee.FeeTypeId];
            var price = type.ResolvePrice(fee.EffectiveFrom);
            lines.Add($"- Từ ngày {Date(fee.EffectiveFrom)}: " + Describe(type.Name, type.Group, type.ChargeBasis, type.Unit, fee.Quantity,
                fee.UnitPriceOverride ?? price?.UnitPrice));
        }

        foreach (var ended in c.Fees.Where(f => f.EffectiveTo is { } to
                     && c.Fees.All(n => n.FeeTypeId != f.FeeTypeId || n.EffectiveFrom != to.AddDays(1)) && d.FeeTypes.ContainsKey(f.FeeTypeId)))
            lines.Add($"- Từ ngày {Date(ended.EffectiveTo!.Value.AddDays(1))}: thôi tính {d.FeeTypes[ended.FeeTypeId].Name}.");

        return lines.Count == 0 ? "Không phát sinh." : string.Join('\n', lines);
    }

    /// <summary>Mô tả cách tính 1 khoản thu bằng lời — đúng cách tính sẽ dùng khi lập phiếu (M07).</summary>
    private static string Describe(
        string name, FeeGroup group, ChargeBasis? basis, string unit, decimal quantity, decimal? unitPrice, IReadOnlyList<PriceTier>? tiers = null)
    {
        var price = unitPrice is { } p ? VietnameseMoney.Format(p) : "theo bảng giá của bên A";
        return group switch
        {
            FeeGroup.Metered when tiers is { Count: > 0 } =>
                $"{name}: theo chỉ số công tơ của phòng (tính từ chỉ số ngày nhận phòng), giá theo bậc ({string.Join("; ", TierTexts(tiers, unit))}).",
            FeeGroup.Metered => $"{name}: theo chỉ số công tơ của phòng (tính từ chỉ số ngày nhận phòng), {price}/{unit}.",
            _ when basis == ChargeBasis.PerOccupant => $"{name}: {price}/người/tháng (tính theo số người ở).",
            _ when basis == ChargeBasis.PerRoom => $"{name}: {price}/phòng/tháng.",
            _ => $"{name}: {price}/{unit}/tháng × {Number(quantity)} {unit}."
        };
    }

    private static IEnumerable<string> TierTexts(IReadOnlyList<PriceTier> tiers, string unit)
    {
        decimal lower = 0;
        foreach (var t in tiers)
        {
            yield return t.UpTo is { } up
                ? $"{Number(lower)}–{Number(up)} {unit}: {VietnameseMoney.Format(t.Price)}"
                : $"trên {Number(lower)} {unit}: {VietnameseMoney.Format(t.Price)}";
            lower = t.UpTo ?? lower;
        }
    }

    private static string DepositText(Contract c) =>
        (c.DepositAmount == 0
            ? "Hai bên thỏa thuận không đặt cọc."
            : $"Bên B đặt cọc cho bên A số tiền {VietnameseMoney.Format(c.DepositAmount)} (Bằng chữ: {VietnameseMoney.ToWords(c.DepositAmount)}).") +
        (c.DepositTerms is null ? "" : "\n" + c.DepositTerms);

    private static List<IReadOnlyList<string>> CustomFieldRows(Contract c)
    {
        var values = ContractDocumentJson.Values(c.CustomFieldValues);
        return ContractDocumentJson.Fields(c.CustomFieldDefinitions)
            .Where(f => values.ContainsKey(f.Key))
            .Select(f => (IReadOnlyList<string>)[f.Label, FieldValue(f, values[f.Key])])
            .ToList();
    }

    private static string FieldValue(CustomFieldDefinition f, JsonElement v)
    {
        var text = f.Type switch
        {
            CustomFieldType.Boolean => v.GetBoolean() ? "Có" : "Không",
            CustomFieldType.Money => VietnameseMoney.Format(v.GetDecimal()),
            CustomFieldType.Number => Number(v.GetDecimal()),
            CustomFieldType.Date => Date(DateOnly.Parse(v.GetString()!, CultureInfo.InvariantCulture)),
            _ => v.GetString() ?? ""
        };
        return f.Unit is null || f.Type is CustomFieldType.Boolean or CustomFieldType.Select ? text : $"{text} {f.Unit}";
    }

    // ------------------------------------------------------------------ Tiện ích

    private static PrintParagraph Center(string text, bool bold = false, int size = 13) => new(text, PrintAlign.Center, bold, FontSize: size);
    private static PrintParagraph Heading(string text) => new(text, PrintAlign.Left, Bold: true);
    private static PrintParagraph Text(string text) => new(text);

    private static string Number(decimal value) => value.ToString("#,##0.##", CultureInfo.GetCultureInfo("vi-VN"));

    private static string Date(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    private static string DateOrBlank(DateOnly? d) => d is { } v ? Date(v) : Blank;
    private static string DateWords(DateOnly? d) => d is { } v ? $"ngày {v.Day:00} tháng {v.Month:00} năm {v.Year}" : "ngày ..... tháng ..... năm ......";

    private static string IdLabel(IdDocumentType? type) => type switch
    {
        IdDocumentType.LegacyId => "CMND",
        IdDocumentType.Passport => "Hộ chiếu",
        _ => "CCCD"
    };

    private static string PaymentLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "tiền mặt",
        PaymentMethod.BankTransfer => "chuyển khoản",
        _ => "ví điện tử"
    };

    private static string VehicleLabel(VehicleType type) => type switch
    {
        VehicleType.Motorbike => "Xe máy",
        VehicleType.Bicycle => "Xe đạp",
        VehicleType.ElectricBike => "Xe điện",
        _ => "Ô tô"
    };
}
