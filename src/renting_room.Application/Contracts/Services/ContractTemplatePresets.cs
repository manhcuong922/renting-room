using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

public static class ContractTemplatePresets
{
    private static readonly CustomFieldDefinition[] LivingRules =
    [
        new("overnight_guest_policy", "Khách ở qua đêm", CustomFieldType.Select, true,
            ["Phải báo và được chủ nhà đồng ý", "Không cho phép", "Không hạn chế"], null, "Bên B chịu trách nhiệm về hành vi của khách"),
        new("gate_closing_time", "Giờ đóng cổng", CustomFieldType.Text, false, null, null, "VD 23:00, hoặc \"Tự do (có chìa khóa)\""),
        new("pets_allowed", "Cho phép nuôi thú cưng", CustomFieldType.Boolean, false, null, null, null)
    ];

    private static readonly ContractClause DisputeClause = new("Giải quyết tranh chấp",
        "- Tranh chấp phát sinh được hai bên thương lượng, hòa giải trên tinh thần hợp tác.\n" +
        "- Không thương lượng được thì một trong hai bên có quyền yêu cầu Tòa án có thẩm quyền giải quyết theo quy định của pháp luật.");

    public static readonly ContractTemplateInput RoomRental = new(
        "Thuê phòng trọ (mẫu chuẩn)",
        ContractType.RoomRental,
        ContractTypes.DefaultTitle(ContractType.RoomRental),
        [
            new("Trách nhiệm của bên A",
                "- Bàn giao phòng và trang thiết bị cho bên B đúng thời hạn, đúng hiện trạng đã thỏa thuận.\n" +
                "- Tạo mọi điều kiện thuận lợi để bên B thực hiện theo hợp đồng.\n" +
                "- Cung cấp nguồn điện, nước (và các dịch vụ đã thỏa thuận) cho bên B sử dụng."),
            new("Trách nhiệm của bên B",
                "- Thanh toán đầy đủ các khoản tiền theo đúng thỏa thuận.\n" +
                "- Bảo quản các trang thiết bị và cơ sở vật chất bên A trang bị ban đầu (làm hỏng phải sửa, mất phải đền).\n" +
                "- Không được tự ý sửa chữa, cải tạo cơ sở vật chất khi chưa được sự đồng ý của bên A.\n" +
                "- Giữ gìn vệ sinh trong và ngoài khuôn viên phòng trọ.\n" +
                "- Chấp hành quy định của pháp luật và của địa phương; thực hiện đăng ký tạm trú theo quy định.\n" +
                "- Cho khách ở qua đêm phải báo và được bên A đồng ý, đồng thời chịu trách nhiệm về hành vi vi phạm pháp luật của khách."),
            new("Trách nhiệm chung",
                "- Hai bên tạo điều kiện cho nhau thực hiện hợp đồng.\n" +
                "- Bên nào vi phạm điều khoản đã thỏa thuận thì bên còn lại có quyền đơn phương chấm dứt hợp đồng; vi phạm gây tổn thất thì bên vi phạm phải bồi thường.\n" +
                "- Bên muốn chấm dứt hợp đồng trước thời hạn phải báo trước cho bên kia theo số ngày đã thỏa thuận.\n" +
                "- Khi chấm dứt hợp đồng, bên A hoàn trả tiền đặt cọc cho bên B sau khi trừ các khoản bên B còn nợ hoặc phải bồi thường (nếu có)."),
            DisputeClause
        ],
        LivingRules);

    public static readonly ContractTemplateInput WholeHouseRental = new(
        "Thuê nhà nguyên căn (mẫu chuẩn)",
        ContractType.WholeHouseRental,
        ContractTypes.DefaultTitle(ContractType.WholeHouseRental),
        [
            new("Đặc điểm nhà cho thuê",
                "Bên A cho bên B thuê toàn bộ ngôi nhà tại địa chỉ ghi trong hợp đồng, gồm diện tích, số tầng, " +
                "giấy tờ sở hữu và mục đích sử dụng như các thông tin bổ sung của hợp đồng này."),
            new("Trách nhiệm của bên A",
                "- Bàn giao nhà và trang thiết bị đúng thời hạn, đúng hiện trạng.\n" +
                "- Bảo đảm quyền sử dụng ổn định của bên B trong thời hạn thuê.\n" +
                "- Sửa chữa hư hỏng thuộc kết cấu chính của ngôi nhà không do lỗi của bên B."),
            new("Trách nhiệm của bên B",
                "- Sử dụng nhà đúng mục đích đã thỏa thuận; thanh toán tiền thuê và chi phí điện, nước, dịch vụ đúng hạn.\n" +
                "- Không cho thuê lại, chuyển nhượng hợp đồng khi chưa có sự đồng ý bằng văn bản của bên A.\n" +
                "- Không tự ý đục phá, cải tạo, phá dỡ nhà.\n" +
                "- Chấp hành quy định về phòng cháy chữa cháy, an ninh trật tự, vệ sinh môi trường; đăng ký tạm trú theo quy định.\n" +
                "- Trả lại nhà và trang thiết bị khi hết hạn hợp đồng."),
            new("Chấm dứt hợp đồng",
                "- Hợp đồng chấm dứt khi hết hạn mà không gia hạn, hoặc hai bên thỏa thuận chấm dứt.\n" +
                "- Một bên đơn phương chấm dứt phải báo trước cho bên kia theo số ngày đã thỏa thuận; trường hợp đơn phương chấm dứt theo Luật Nhà ở 2023 Điều 172.\n" +
                "- Bên vi phạm gây thiệt hại phải bồi thường."),
            DisputeClause
        ],
        [
            new("land_area_m2", "Diện tích đất", CustomFieldType.Number, false, null, "m²", null),
            new("floor_area_m2", "Tổng diện tích sàn sử dụng", CustomFieldType.Number, true, null, "m²", null),
            new("floor_count", "Số tầng", CustomFieldType.Number, true, null, "tầng", null),
            new("ownership_document", "Giấy tờ sở hữu nhà", CustomFieldType.Text, false, null, null, "Số giấy chứng nhận, ngày cấp"),
            new("usage_purpose", "Mục đích sử dụng", CustomFieldType.Select, true, ["Để ở", "Kinh doanh", "Để ở kết hợp kinh doanh"], null, null),
            new("rent_payment_cycle", "Kỳ trả tiền thuê", CustomFieldType.Select, true, ["Hằng tháng", "3 tháng/lần", "6 tháng/lần", "12 tháng/lần"], null, null),
            new("sublease_allowed", "Cho phép cho thuê lại", CustomFieldType.Boolean, false, null, null, null),
            .. LivingRules
        ]);

    public static readonly ContractTemplateInput RoomRentalNoDeposit = new(
        "Thuê phòng trọ không cọc (mẫu chuẩn)",
        ContractType.RoomRental,
        ContractTypes.DefaultTitle(ContractType.RoomRental),
        [
            RoomRental.Clauses![0],
            RoomRental.Clauses![1],
            new("Thanh toán (không đặt cọc)",
                "- Hai bên thỏa thuận không đặt cọc.\n" +
                "- Bên B thanh toán tiền thuê trước, vào đầu mỗi kỳ thu.\n" +
                "- Bên B chậm thanh toán quá số ngày đã thỏa thuận thì bên A có quyền đơn phương chấm dứt hợp đồng; " +
                "bên B phải thanh toán đủ các khoản còn nợ và bồi thường thiệt hại đối với tài sản (nếu có)."),
            new("Trách nhiệm chung",
                "- Hai bên tạo điều kiện cho nhau thực hiện hợp đồng.\n" +
                "- Bên nào vi phạm điều khoản đã thỏa thuận thì bên còn lại có quyền đơn phương chấm dứt hợp đồng; vi phạm gây tổn thất thì bên vi phạm phải bồi thường.\n" +
                "- Bên muốn chấm dứt hợp đồng trước thời hạn phải báo trước cho bên kia theo số ngày đã thỏa thuận."),
            DisputeClause
        ],
        [
            new("late_payment_days", "Số ngày chậm trả tối đa", CustomFieldType.Number, true, null, "ngày",
                "Quá số ngày này bên A được chấm dứt hợp đồng"),
            .. LivingRules
        ],
        NoDeposit: true);

    public static readonly IReadOnlyList<ContractTemplateInput> All = [RoomRental, RoomRentalNoDeposit, WholeHouseRental];
}
