using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record BulkRoomFloor(string? Floor, IReadOnlyList<string> Codes);

/// <summary>Tạo hàng loạt theo tầng — all-or-nothing: một mã trùng thì không tạo phòng nào.</summary>
public sealed record BulkCreateRoomsCommand(
    Guid PropertyId,
    IReadOnlyList<BulkRoomFloor> Floors,
    int? MaxOccupants,
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
