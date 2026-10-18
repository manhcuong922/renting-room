using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Meters;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Fees;

namespace renting_room.Application.Imports;

/// <summary>PR-UC-11 bước 2: nhận các dòng (đã sửa trên màn), kiểm lại toàn bộ, lưu dòng hợp lệ — mỗi dòng 1 transaction.</summary>
public sealed record SaveRoomImportCommand(Guid PropertyId, IReadOnlyList<RoomImportRow> Rows) : IRequest<Result<ImportSaveResultDto>>;

public sealed class SaveRoomImportCommandValidator : AbstractValidator<SaveRoomImportCommand>
{
    public SaveRoomImportCommandValidator() =>
        RuleFor(x => x.Rows).NotEmpty().WithErrorCode("REQUIRED").Must(r => r is null || r.Count <= ImportErrors.MaxRows).WithErrorCode("IMPORT_TOO_MANY_ROWS");
}

public sealed class SaveRoomImportHandler(
    IAppDbContext db, ISender sender, IAuditTrail auditTrail, IValidator<CreateRoomCommand> roomValidator,
    IValidator<InstallMeterCommand> meterValidator, TimeProvider clock)
    : IRequestHandler<SaveRoomImportCommand, Result<ImportSaveResultDto>>
{
    public async ValueTask<Result<ImportSaveResultDto>> Handle(SaveRoomImportCommand request, CancellationToken cancellationToken)
    {
        var property = await ImportProperty.LoadAsync(db, request.PropertyId, cancellationToken);
        if (property.IsFailure)
            return property.Error!;
        var meterDate = RoomImportChecker.MeterDate(property.Value!, clock.GetUtcNow().ToBusinessDate());
        var checks = await new RoomImportChecker(db, roomValidator, meterValidator)
            .CheckAsync(property.Value!, request.Rows, new Dictionary<int, List<ImportIssue>>(), meterDate, cancellationToken);
        var meterFees = await RoomImportChecker.MeterFeesAsync(db, request.PropertyId, cancellationToken);

        var units = new List<ImportUnitResult>();
        foreach (var check in checks)
        {
            var row = check.Row;
            var key = row.Code ?? $"#{row.RowNumber}";
            if (!check.IsValid)
            {
                units.Add(new ImportUnitResult(key, [row.RowNumber], ImportOutcome.Invalid, check.Errors));
                continue;
            }
            var failure = await ImportFiles.SaveUnitAsync(db, async () =>
            {
                var created = await sender.Send(new CreateRoomCommand(request.PropertyId, row.Code!, RoomImportTemplate.Spec(row)), cancellationToken);
                if (created.IsFailure)
                    return created.Error;
                foreach (var (code, serial, reading) in new[]
                         {
                             (FeeSystemCodes.Electricity, row.ElectricitySerial, row.ElectricityReading),
                             (FeeSystemCodes.Water, row.WaterSerial, row.WaterReading)
                         })
                {
                    if (reading is not { } value)
                        continue;
                    var installed = await sender.Send(
                        new InstallMeterCommand(created.Value, meterFees[code], serial, meterDate, value, "Nhập từ Excel"), cancellationToken);
                    if (installed.IsFailure)
                        return installed.Error;
                }
                return null;
            }, cancellationToken);
            units.Add(new ImportUnitResult(key, [row.RowNumber], failure.Count == 0 ? ImportOutcome.Saved : ImportOutcome.Failed, failure));
        }
        return await ImportPreview.RecordAsync(db, auditTrail, request.PropertyId, "Rooms", units, cancellationToken);
    }
}
