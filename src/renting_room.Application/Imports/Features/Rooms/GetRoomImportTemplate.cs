using Mediator;
using renting_room.Application.Properties;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Exports;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Imports;

public sealed record GetRoomImportTemplateQuery(Guid PropertyId) : IRequest<Result<ExportFile>>;

public sealed class GetRoomImportTemplateHandler(IPropertyQueries properties, ISpreadsheetWriter writer)
    : IRequestHandler<GetRoomImportTemplateQuery, Result<ExportFile>>
{
    public async ValueTask<Result<ExportFile>> Handle(GetRoomImportTemplateQuery request, CancellationToken cancellationToken)
    {
        var code = await properties.GetCodeAsync(request.PropertyId, cancellationToken);
        if (code is null)
            return PropertyErrors.PropertyNotFound;

        var sheet = new SpreadsheetSheet("Phòng", $"Mẫu nhập phòng — khu {code}",
            "Cột có * là bắt buộc. Tiện nghi: mã cách nhau dấu phẩy (VD air_con, wifi). Chỉ số đầu kỳ = số chốt đầu kỳ hiện tại của khu " +
            "(công tơ tính là lắp từ ngày chốt). Tối đa 500 dòng; không dùng công thức.",
            RoomImportTemplate.Columns.Select(c => new SpreadsheetColumn(c.Header, c.Type, c.Width)).ToList(),
            [["101", "1", 20, 2, 3_000_000, 3_000_000, "air_con, wifi", null, "E-101", 1250, null, 340]]);
        return new ExportFile($"mau-nhap-phong_{code}.xlsx", writer.Write(new SpreadsheetDocument([sheet])));
    }
}
