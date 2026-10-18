using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Exports;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Imports;

public sealed record GetTenancyImportTemplateQuery(Guid PropertyId) : IRequest<Result<ExportFile>>;

public sealed class GetTenancyImportTemplateHandler(IAppDbContext db, ISpreadsheetWriter writer)
    : IRequestHandler<GetTenancyImportTemplateQuery, Result<ExportFile>>
{
    public async ValueTask<Result<ExportFile>> Handle(GetTenancyImportTemplateQuery request, CancellationToken cancellationToken)
    {
        var code = await db.Properties.Where(p => p.Id == request.PropertyId).Select(p => p.Code).FirstOrDefaultAsync(cancellationToken);
        if (code is null)
            return PropertyErrors.PropertyNotFound;

        var sheet = new SpreadsheetSheet(TenancyImportTemplate.Sheet, $"Người thuê đang ở — khu {code}",
            "Mỗi dòng 1 người; các dòng cùng mã phòng = 1 hợp đồng. Vai trò: Đứng tên / Đứng tên (không ở) / Ở cùng — mỗi phòng đúng 1 người đứng tên. " +
            "Giấy tờ bắt buộc từ 14 tuổi và với người đứng tên. Giá thuê, cọc chỉ ghi ở dòng đứng tên. Chỉ nhập phòng đang trống; dịch vụ, xe thêm sau.",
            TenancyImportTemplate.Columns.Select(c => new SpreadsheetColumn(c.Header, c.Type, c.Width)).ToList(),
            [
                ["101", "Đứng tên", "Phạm Văn Hùng", new DateOnly(1985, 4, 2), "Nam", "0912345678", "CCCD", "001085012345", "VN", "Nam Định", "Kỹ sư", null,
                    new DateOnly(2024, 3, 1), 3_500_000, 3_500_000],
                ["101", "Ở cùng", "Lê Thị Mai", new DateOnly(1988, 9, 20), "Nữ", null, "CCCD", "001188012346", "VN", "Nam Định", null, "Vợ",
                    new DateOnly(2024, 3, 1), null, null],
                ["101", "Ở cùng", "Phạm Minh An", new DateOnly(2018, 6, 1), "Nam", null, null, null, "VN", null, "Học sinh", "Con",
                    new DateOnly(2024, 3, 1), null, null]
            ]);
        return new ExportFile($"mau-nhap-nguoi-thue_{code}.xlsx", writer.Write(new SpreadsheetDocument([sheet])));
    }
}
