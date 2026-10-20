using FluentValidation;
using Mediator;
using renting_room.Application.Billing;
using renting_room.Application.Properties;
using renting_room.Application.Rooms;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Meters;
using renting_room.Domain.Properties;

namespace renting_room.Application.Meters;

public sealed record ReplaceSheetRowDto(Guid MeterId, Guid RoomId, string RoomCode, string? Floor, string? SerialNo, decimal? LastValue, DateOnly? LastDate);

public sealed record GetReplaceSheetQuery(Guid PropertyId, Guid FeeTypeId) : IRequest<Result<IReadOnlyList<ReplaceSheetRowDto>>>;

public sealed class GetReplaceSheetHandler(IPropertyQueries properties, IMeterQueries meterQueries, IRoomQueries roomQueries)
    : IRequestHandler<GetReplaceSheetQuery, Result<IReadOnlyList<ReplaceSheetRowDto>>>
{
    public async ValueTask<Result<IReadOnlyList<ReplaceSheetRowDto>>> Handle(GetReplaceSheetQuery request, CancellationToken cancellationToken)
    {
        if (!await properties.ExistsAsync(request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var meters = await meterQueries.ListActiveByFeeAsync(request.PropertyId, request.FeeTypeId, cancellationToken);
        var roomIds = meters.Select(m => m.RoomId).ToList();
        var rooms = await roomQueries.ListAsync(roomIds, cancellationToken);
        return meters.Select(m =>
            {
                var last = m.Ordered.LastOrDefault();
                var room = rooms[m.RoomId];
                return new ReplaceSheetRowDto(m.Id, m.RoomId, room.Code, room.Floor, m.SerialNo, last?.Value, last?.ReadingDate);
            })
            .OrderBy(r => r.Floor).ThenBy(r => r.RoomCode).ToList();
    }
}

public sealed record BulkReplaceRow(Guid MeterId, decimal OldFinalValue, string? NewSerialNo, decimal? NewInitialValue);

public sealed record BulkReplaceRowError(int Index, Guid MeterId, string Code, string Message);

public sealed record BulkReplaceResult(int Replaced, IReadOnlyList<Guid> NewMeterIds);

public sealed record BulkReplaceMetersCommand(Guid PropertyId, Guid FeeTypeId, DateOnly ReplacedOn, IReadOnlyList<BulkReplaceRow> Rows, string? Note)
    : IRequest<Result<BulkReplaceResult>>;

public sealed class BulkReplaceMetersCommandValidator : AbstractValidator<BulkReplaceMetersCommand>
{
    public const int MaxRows = 500;

    public BulkReplaceMetersCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.ReplacedOn).ReadingDate(clock);
        RuleFor(x => x.Rows).NotEmpty().WithErrorCode("REQUIRED")
            .Must(r => r is null || r.Count <= MaxRows).WithErrorCode("OUT_OF_RANGE").WithMessage($"Tối đa {MaxRows} công tơ một lần.")
            .Must(r => r is null || r.Select(x => x.MeterId).Distinct().Count() == r.Count).WithErrorCode("DUPLICATE_METER")
            .WithMessage("Một công tơ xuất hiện 2 lần.");
        RuleForEach(x => x.Rows).ChildRules(row =>
        {
            row.RuleFor(r => r.OldFinalValue).ReadingValue();
            row.RuleFor(r => r.NewInitialValue!.Value).ReadingValue().When(r => r.NewInitialValue is not null);
            row.RuleFor(r => r.NewSerialNo).OptionalText(50);
        });
        RuleFor(x => x.Note).OptionalText(300);
    }
}

public sealed class BulkReplaceMetersHandler(IMeterRepository meterRepository, IInvoiceQueries invoices, IUnitOfWork unitOfWork)
    : IRequestHandler<BulkReplaceMetersCommand, Result<BulkReplaceResult>>
{
    public async ValueTask<Result<BulkReplaceResult>> Handle(BulkReplaceMetersCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var meters = (await meterRepository.LockAndLoadAsync(request.Rows.Select(r => r.MeterId), cancellationToken)).ToDictionary(m => m.Id);
        var locks = await invoices.GetMeterLockedUntilAsync(meters.Keys.ToList(), cancellationToken);

        var errors = new List<BulkReplaceRowError>();
        var created = new List<Meter>();
        for (var index = 0; index < request.Rows.Count; index++)
        {
            var row = request.Rows[index];
            if (!meters.TryGetValue(row.MeterId, out var meter) || meter.PropertyId != request.PropertyId || meter.FeeTypeId != request.FeeTypeId)
            {
                errors.Add(new BulkReplaceRowError(index, row.MeterId, MeterErrors.NotFound.Code, "Công tơ không thuộc khu / khoản thu đã chọn."));
                continue;
            }
            if (MeterPeriodLocks.Check(locks, meter.Id, request.ReplacedOn) is { } locked)
            {
                errors.Add(new BulkReplaceRowError(index, row.MeterId, locked.Code, locked.Message));
                continue;
            }
            var next = meter.ReplaceWith(request.ReplacedOn, row.OldFinalValue, row.NewSerialNo, row.NewInitialValue ?? 0, request.Note);
            if (next.IsFailure)
                errors.Add(new BulkReplaceRowError(index, row.MeterId, next.Error!.Code, next.Error.Message));
            else
                created.Add(next.Value!);
        }
        if (errors.Count > 0)
            return Error.BusinessRule("BULK_REPLACE_INVALID", "Có dòng thay công tơ không hợp lệ — chưa lưu dòng nào.").WithDetail("rowErrors", errors);

        // Như thay đơn: tháo hết bản cũ trước rồi mới lắp bản mới — unique "1 công tơ hoạt động" kiểm từng câu lệnh.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        meterRepository.AddRange(created);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BulkReplaceResult(created.Count, created.Select(m => m.Id).ToList());
    }
}
