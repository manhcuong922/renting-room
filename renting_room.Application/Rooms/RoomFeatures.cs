using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record RoomSpecInput(
    string? Floor,
    decimal? AreaM2,
    int MaxOccupants,
    decimal? ListedRent,
    decimal? DefaultDeposit,
    IReadOnlyCollection<string>? Amenities,
    string? Description)
{
    public RoomSpec ToDomain() => new(Floor, AreaM2, MaxOccupants, ListedRent, DefaultDeposit, Amenities ?? [], Description);
}

public sealed record CurrentContractDto(Guid Id, string ContractNo, string RepresentativeName, DateOnly StartDate, DateOnly? EndDate, int OccupantCount);

public sealed record RoomDto(
    Guid Id,
    Guid PropertyId,
    string PropertyCode,
    string Code,
    string? Floor,
    decimal? AreaM2,
    int MaxOccupants,
    decimal? ListedRent,
    decimal? DefaultDeposit,
    string[] Amenities,
    string? Description,
    RoomDisplayStatus Status,
    string? MaintenanceNote,
    CurrentContractDto? CurrentContract,
    string Version);

internal static class RoomSpecRules
{
    public static void SpecRules<T>(this AbstractValidator<T> v, Func<T, RoomSpecInput> spec, string prefix = "")
    {
        v.RuleFor(x => spec(x).Floor).OptionalText(10).OverridePropertyName($"{prefix}floor");
        v.RuleFor(x => spec(x).AreaM2)
            .Must(a => a is null || (a > 0 && a <= 1000 && decimal.Round(a.Value, 2) == a))
            .OverridePropertyName($"{prefix}areaM2").WithErrorCode("OUT_OF_RANGE").WithMessage("Diện tích từ 0 đến 1000 m², tối đa 2 số lẻ.");
        v.RuleFor(x => spec(x).MaxOccupants).InclusiveBetween(1, Room.MaxOccupantsLimit)
            .OverridePropertyName($"{prefix}maxOccupants").WithErrorCode("OUT_OF_RANGE");
        v.RuleFor(x => spec(x).ListedRent).OptionalMoney().OverridePropertyName($"{prefix}listedRent");
        v.RuleFor(x => spec(x).DefaultDeposit).OptionalMoney().OverridePropertyName($"{prefix}defaultDeposit");
        v.RuleFor(x => spec(x).Amenities)
            .Must(a => a is null || (a.Count <= 30 && a.All(t => System.Text.RegularExpressions.Regex.IsMatch(t, "^[a-z0-9_]{1,30}$"))))
            .OverridePropertyName($"{prefix}amenities").WithErrorCode("INVALID_FORMAT")
            .WithMessage("Tối đa 30 tiện ích, mỗi mã gồm chữ thường, số, '_' (VD: air_con).");
        v.RuleFor(x => spec(x).Description).OptionalText(2000).OverridePropertyName($"{prefix}description");
    }
}

/// <summary>
/// PR-BR-02: trạng thái phòng dẫn xuất theo ngày D — không thể lệch với hợp đồng.
/// Thứ tự ưu tiên: Ngừng dùng → Bảo trì → Đang thuê → Giữ chỗ (có HĐ nháp) → Trống.
/// </summary>
internal sealed class RoomRow
{
    public Room Room { get; init; } = null!;
    public string PropertyCode { get; init; } = null!;
    public CurrentContractDto? Current { get; init; }
    public RoomDisplayStatus Status { get; init; }

    public RoomDto ToDto() => new(
        Room.Id, Room.PropertyId, PropertyCode, Room.Code, Room.Floor, Room.AreaM2, Room.MaxOccupants, Room.ListedRent,
        Room.DefaultDeposit, Room.Amenities, Room.Description, Status, Room.MaintenanceNote, Current, Room.Version.ToString());
}

internal static class RoomStatusQuery
{
    /// <summary>Dùng object initializer (không dùng constructor) để EF dịch được Where/OrderBy trên Status.</summary>
    public static IQueryable<RoomRow> ToRows(this IQueryable<Room> rooms, IAppDbContext db, DateOnly today) =>
        rooms.Select(r => new
        {
            Room = r,
            PropertyCode = db.Properties.Where(p => p.Id == r.PropertyId).Select(p => p.Code).First(),
            Current = db.Contracts
                .Where(c => c.RoomId == r.Id
                    && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating || c.Status == ContractStatus.Ended)
                    && c.StartDate <= today && (c.ActualEndDate == null || c.ActualEndDate >= today))
                .Select(c => new CurrentContractDto(
                    c.Id,
                    c.ContractNo,
                    db.Renters.Where(x => x.Id == c.RepresentativeRenterId).Select(x => x.FullName).First(),
                    c.StartDate,
                    c.EndDate,
                    c.Occupants.Count(o => o.MoveInDate <= today && (o.MoveOutDate == null || o.MoveOutDate >= today))))
                .FirstOrDefault(),
            // Giữ chỗ: có hợp đồng nháp, hoặc đã kích hoạt nhưng ngày bàn giao ở tương lai (kích hoạt trước 1 ngày).
            HasDraft = db.Contracts.Any(c => c.RoomId == r.Id
                && (c.Status == ContractStatus.Draft || (c.Status == ContractStatus.Active && c.StartDate > today)))
        })
        .Select(x => new RoomRow
        {
            Room = x.Room,
            PropertyCode = x.PropertyCode,
            Current = x.Current,
            Status = x.Room.ArchivedAt != null ? RoomDisplayStatus.Archived
                : x.Room.IsUnderMaintenance ? RoomDisplayStatus.Maintenance
                : x.Current != null ? RoomDisplayStatus.Occupied
                : x.HasDraft ? RoomDisplayStatus.Reserved
                : RoomDisplayStatus.Vacant
        });

    /// <summary>
    /// Phòng còn HĐ đang chiếm: hiệu lực / đang thanh lý / (tùy chọn) nháp, và HĐ đã kết thúc mà hôm nay vẫn là ngày trả phòng
    /// (người thuê còn ở hết ngày đó — khớp trạng thái "Đang thuê").
    /// </summary>
    public static Task<bool> HasOpenContractAsync(this IAppDbContext db, Guid roomId, bool includeDraft, DateOnly today, CancellationToken ct) =>
        db.Contracts.AnyAsync(c => c.RoomId == roomId
            && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating
                || (c.Status == ContractStatus.Ended && c.ActualEndDate >= today)
                || (includeDraft && c.Status == ContractStatus.Draft)), ct);
}

// ============================================================ Tạo phòng

public sealed record CreateRoomCommand(Guid PropertyId, string Code, RoomSpecInput Spec) : IRequest<Result<Guid>>;

public sealed class CreateRoomCommandValidator : AbstractValidator<CreateRoomCommand>
{
    public CreateRoomCommandValidator()
    {
        RuleFor(x => x.Code).Code(20, "Mã phòng");
        RuleFor(x => x.Spec).NotNull().WithErrorCode("REQUIRED");
        When(x => x.Spec is not null, () => this.SpecRules(x => x.Spec, prefix: "spec."));
    }
}

public sealed class CreateRoomHandler(IAppDbContext db) : IRequestHandler<CreateRoomCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(CreateRoomCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;
        if (property.IsArchived)
            return PropertyErrors.PropertyArchived;

        var code = TextNormalizer.NormalizeCode(request.Code);
        if (await db.Rooms.AnyAsync(r => r.PropertyId == property.Id && r.Code == code, cancellationToken))
            return PropertyErrors.RoomCodeTaken;

        var room = Room.Create(property.Id, code, request.Spec.ToDomain());
        db.Rooms.Add(room);
        await db.SaveChangesAsync(cancellationToken);
        return room.Id;
    }
}

public sealed record BulkRoomFloor(string? Floor, IReadOnlyList<string> Codes);

/// <summary>Tạo hàng loạt theo tầng — all-or-nothing: một mã trùng thì không tạo phòng nào.</summary>
public sealed record BulkCreateRoomsCommand(
    Guid PropertyId,
    IReadOnlyList<BulkRoomFloor> Floors,
    int MaxOccupants,
    decimal? AreaM2,
    decimal? ListedRent,
    decimal? DefaultDeposit,
    IReadOnlyCollection<string>? Amenities) : IRequest<Result<BulkCreateRoomsResult>>;

public sealed record BulkCreateRoomsResult(int Created, IReadOnlyList<Guid> RoomIds);

public sealed class BulkCreateRoomsCommandValidator : AbstractValidator<BulkCreateRoomsCommand>
{
    public const int MaxRooms = 500;

    public BulkCreateRoomsCommandValidator()
    {
        RuleFor(x => x.Floors).NotEmpty().WithErrorCode("REQUIRED");
        RuleForEach(x => x.Floors).ChildRules(floor =>
        {
            floor.RuleFor(f => f.Floor).OptionalText(10);
            floor.RuleFor(f => f.Codes).NotEmpty().WithErrorCode("REQUIRED");
            floor.RuleForEach(f => f.Codes).Code(20, "Mã phòng");
        });
        RuleFor(x => x.Floors)
            .Must(f => f is null || f.Sum(x => x.Codes?.Count ?? 0) <= MaxRooms)
            .WithErrorCode("TOO_MANY_ROOMS").WithMessage($"Tối đa {MaxRooms} phòng mỗi lần.")
            .Must(f => f is null || AllCodes(f).Count == AllCodes(f).Distinct().Count())
            .WithErrorCode("DUPLICATE_IN_REQUEST").WithMessage("Danh sách có mã phòng bị trùng.");
        this.SpecRules(x => new RoomSpecInput(null, x.AreaM2, x.MaxOccupants, x.ListedRent, x.DefaultDeposit, x.Amenities, null));
    }

    internal static List<string> AllCodes(IEnumerable<BulkRoomFloor> floors) =>
        floors.SelectMany(f => f.Codes ?? []).Select(TextNormalizer.NormalizeCode).ToList();
}

public sealed class BulkCreateRoomsHandler(IAppDbContext db) : IRequestHandler<BulkCreateRoomsCommand, Result<BulkCreateRoomsResult>>
{
    public async ValueTask<Result<BulkCreateRoomsResult>> Handle(BulkCreateRoomsCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;
        if (property.IsArchived)
            return PropertyErrors.PropertyArchived;

        var codes = BulkCreateRoomsCommandValidator.AllCodes(request.Floors);
        var existing = await db.Rooms.Where(r => r.PropertyId == property.Id && codes.Contains(r.Code))
            .Select(r => r.Code).ToListAsync(cancellationToken);
        if (existing.Count > 0)
            return Error.Conflict(PropertyErrors.RoomCodeTaken.Code, $"Mã phòng đã tồn tại: {string.Join(", ", existing)}.");

        var rooms = request.Floors
            .SelectMany(f => f.Codes.Select(code => Room.Create(property.Id, code,
                new RoomSpec(f.Floor, request.AreaM2, request.MaxOccupants, request.ListedRent, request.DefaultDeposit, request.Amenities ?? [], null))))
            .ToList();

        db.Rooms.AddRange(rooms);
        await db.SaveChangesAsync(cancellationToken);
        return new BulkCreateRoomsResult(rooms.Count, rooms.Select(r => r.Id).ToList());
    }
}

// ============================================================ Sửa phòng

public sealed record UpdateRoomCommand(Guid Id, string Code, RoomSpecInput Spec, uint Version) : IRequest<Result<RoomDto>>;

public sealed class UpdateRoomCommandValidator : AbstractValidator<UpdateRoomCommand>
{
    public UpdateRoomCommandValidator()
    {
        RuleFor(x => x.Code).Code(20, "Mã phòng");
        RuleFor(x => x.Spec).NotNull().WithErrorCode("REQUIRED");
        When(x => x.Spec is not null, () => this.SpecRules(x => x.Spec, prefix: "spec."));
    }
}

public sealed class UpdateRoomHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<UpdateRoomCommand, Result<RoomDto>>
{
    public async ValueTask<Result<RoomDto>> Handle(UpdateRoomCommand request, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (room is null)
            return PropertyErrors.RoomNotFound;

        var code = TextNormalizer.NormalizeCode(request.Code);
        if (await db.Rooms.AnyAsync(r => r.PropertyId == room.PropertyId && r.Code == code && r.Id != room.Id, cancellationToken))
            return PropertyErrors.RoomCodeTaken;

        // PR-BR-06: không giảm sức chứa xuống dưới số người đang ở.
        var today = clock.GetUtcNow().ToBusinessDate();
        var currentOccupants = await db.Contracts
            .Where(c => c.RoomId == room.Id && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Liquidating))
            .SelectMany(c => c.Occupants)
            .CountAsync(o => o.MoveInDate <= today && (o.MoveOutDate == null || o.MoveOutDate >= today), cancellationToken);
        if (request.Spec.MaxOccupants < currentOccupants)
            return PropertyErrors.MaxOccupantsBelowCurrent;

        db.SetExpectedVersion(room, request.Version);
        room.Update(code, request.Spec.ToDomain());
        await db.SaveChangesAsync(cancellationToken);

        var row = await db.Rooms.AsNoTracking().Where(r => r.Id == room.Id).ToRows(db, today).FirstAsync(cancellationToken);
        return row.ToDto();
    }
}

// ============================================================ Bảo trì / ngừng dùng

public enum RoomAction
{
    StartMaintenance,
    EndMaintenance,
    Archive,
    Restore
}

public sealed record ChangeRoomStateCommand(Guid Id, RoomAction Action, string? Note = null) : IRequest<Result>;

public sealed class ChangeRoomStateCommandValidator : AbstractValidator<ChangeRoomStateCommand>
{
    public ChangeRoomStateCommandValidator() => RuleFor(x => x.Note).OptionalText(500);
}

/// <summary>PR-BR-03/05: khóa hàng phòng ⇒ không chạy song song với kích hoạt hợp đồng trên cùng phòng.</summary>
public sealed class ChangeRoomStateHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ChangeRoomStateCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeRoomStateCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Room>(request.Id, cancellationToken);

        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (room is null)
            return Result.Failure(PropertyErrors.RoomNotFound);

        Result result;
        switch (request.Action)
        {
            case RoomAction.StartMaintenance:
                if (await db.HasOpenContractAsync(room.Id, includeDraft: false, clock.GetUtcNow().ToBusinessDate(), cancellationToken))
                    return Result.Failure(PropertyErrors.RoomOccupied);
                result = room.StartMaintenance(request.Note);
                break;
            case RoomAction.EndMaintenance:
                result = room.EndMaintenance();
                break;
            case RoomAction.Archive:
                if (await db.HasOpenContractAsync(room.Id, includeDraft: true, clock.GetUtcNow().ToBusinessDate(), cancellationToken))
                    return Result.Failure(PropertyErrors.RoomHasContracts);
                result = room.Archive(clock.GetUtcNow());
                break;
            default:
                var propertyArchived = await db.Properties.AnyAsync(p => p.Id == room.PropertyId && p.ArchivedAt != null, cancellationToken);
                result = propertyArchived ? Result.Failure(PropertyErrors.PropertyArchived) : room.Restore();
                break;
        }

        if (result.IsFailure)
            return result;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}

// ============================================================ Queries

public sealed record ListRoomsQuery(
    Guid? PropertyId,
    RoomDisplayStatus? Status,
    string? Floor,
    Guid? GroupId,
    string? Search,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize) : IRequest<PagedResult<RoomDto>>;

public sealed class ListRoomsQueryValidator : AbstractValidator<ListRoomsQuery>
{
    public ListRoomsQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.Search).OptionalText(50);
        RuleFor(x => x.Floor).OptionalText(10);
    }
}

public sealed class ListRoomsHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<ListRoomsQuery, PagedResult<RoomDto>>
{
    public async ValueTask<PagedResult<RoomDto>> Handle(ListRoomsQuery request, CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow().ToBusinessDate();
        var rooms = db.Rooms.AsNoTracking();

        if (request.PropertyId is { } propertyId)
            rooms = rooms.Where(r => r.PropertyId == propertyId);
        if (request.Status != RoomDisplayStatus.Archived)
            rooms = rooms.Where(r => r.ArchivedAt == null);
        if (!string.IsNullOrWhiteSpace(request.Floor))
            rooms = rooms.Where(r => r.Floor == request.Floor.Trim());
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = TextNormalizer.NormalizeCode(request.Search);
            rooms = rooms.Where(r => r.Code.Contains(term));
        }
        if (request.GroupId is { } groupId)
            rooms = rooms.Where(r => db.RoomGroups.Any(g => g.Id == groupId && g.Members.Any(m => m.RoomId == r.Id)));

        var query = rooms.ToRows(db, today);
        if (request.Status is { } status)
            query = query.Where(r => r.Status == status);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(r => r.PropertyCode).ThenBy(r => r.Room.Code)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<RoomDto>(rows.Select(r => r.ToDto()).ToList(), request.Page, request.PageSize, total);
    }
}

public sealed record GetRoomQuery(Guid Id) : IRequest<Result<RoomDto>>;

public sealed class GetRoomHandler(IAppDbContext db, TimeProvider clock) : IRequestHandler<GetRoomQuery, Result<RoomDto>>
{
    public async ValueTask<Result<RoomDto>> Handle(GetRoomQuery request, CancellationToken cancellationToken)
    {
        var row = await db.Rooms.AsNoTracking()
            .Where(r => r.Id == request.Id)
            .ToRows(db, clock.GetUtcNow().ToBusinessDate())
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? PropertyErrors.RoomNotFound : row.ToDto();
    }
}
