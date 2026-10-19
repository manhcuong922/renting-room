using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Fees;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;

namespace renting_room.Application.Imports;

// PR-UC-11: import phòng từ Excel — xem trước (không giữ) → sửa → lưu dòng hợp lệ
/// <summary>PR-UC-11 bước 1: đọc file, kiểm từng dòng — <b>không lưu gì, server không giữ gì</b>.</summary>
public sealed record PreviewRoomImportCommand(Guid PropertyId, byte[] Content) : IRequest<Result<ImportPreviewDto<RoomImportRow>>>;

public sealed class PreviewRoomImportHandler(
    IPropertyQueries properties, IRoomQueries rooms, IFeeQueries fees, ISpreadsheetReader reader, IValidator<CreateRoomCommand> roomValidator, IValidator<InstallMeterCommand> meterValidator,
    TimeProvider clock)
    : IRequestHandler<PreviewRoomImportCommand, Result<ImportPreviewDto<RoomImportRow>>>
{
    public async ValueTask<Result<ImportPreviewDto<RoomImportRow>>> Handle(PreviewRoomImportCommand request, CancellationToken cancellationToken)
    {
        var property = await ImportProperty.LoadAsync(properties, request.PropertyId, cancellationToken);
        if (property.IsFailure)
            return property.Error!;
        var table = ImportFiles.Locate(reader, request.Content, RoomImportTemplate.Columns, out var fileError);
        if (table is null)
            return fileError!;

        var rows = table.Rows.Select(RoomImportTemplate.Parse).ToList();
        var parseErrors = table.Rows.ToDictionary(r => r.RowNumber, r => r.Errors);
        var meterDate = RoomImportChecker.MeterDate(property.Value!, clock.GetUtcNow().ToBusinessDate());
        var results = await new RoomImportChecker(rooms, fees, roomValidator, meterValidator)
            .CheckAsync(property.Value!, rows, parseErrors, meterDate, cancellationToken);
        return ImportPreview.Of(results);
    }
}
