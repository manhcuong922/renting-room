using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Renters;
using renting_room.Domain.Common;

namespace renting_room.Application.Imports;

public sealed record TenancyImportPreviewDto(
    int ValidRooms, int InvalidRooms, IReadOnlyList<ImportRowResult<TenancyImportRow>> Rows, IReadOnlyList<TenancyRoomDto> Rooms,
    IReadOnlyList<ImportIssue> FileErrors);

/// <summary>RT-UC-10 bước 1: đọc file, kiểm từng người + từng phòng — <b>không lưu gì, server không giữ gì</b>.</summary>
public sealed record PreviewTenancyImportCommand(Guid PropertyId, byte[] Content) : IRequest<Result<TenancyImportPreviewDto>>;

public sealed class PreviewTenancyImportHandler(
    IAppDbContext db, ICurrentUser currentUser, IPersonalDataProtector protector, ISpreadsheetReader reader,
    IValidator<CreateRenterCommand> renterValidator, TimeProvider clock)
    : IRequestHandler<PreviewTenancyImportCommand, Result<TenancyImportPreviewDto>>
{
    public async ValueTask<Result<TenancyImportPreviewDto>> Handle(PreviewTenancyImportCommand request, CancellationToken cancellationToken)
    {
        var property = await ImportProperty.LoadAsync(db, request.PropertyId, cancellationToken);
        if (property.IsFailure)
            return property.Error!;
        var table = ImportFiles.Locate(reader, request.Content, TenancyImportTemplate.Columns, out var fileError);
        if (table is null)
            return fileError!;

        var rows = table.Rows.Select(TenancyImportTemplate.Parse).ToList();
        var check = await new TenancyImportChecker(db, currentUser, protector, renterValidator)
            .CheckAsync(property.Value!, rows, table.Rows.ToDictionary(r => r.RowNumber, r => r.Errors), clock.GetUtcNow().ToBusinessDate(),
                cancellationToken);
        var valid = check.Rooms.Count(r => r.IsValid);
        return new TenancyImportPreviewDto(valid, check.Rooms.Count - valid, check.Rows, check.Rooms,
            rows.Count == 0 ? [new ImportIssue("IMPORT_EMPTY", "File không có dòng dữ liệu nào.")] : []);
    }
}
