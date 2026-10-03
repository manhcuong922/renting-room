using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;

namespace renting_room.Application.Rooms;

public sealed record RoomGroupDto(Guid Id, Guid PropertyId, string Name, string? Description, IReadOnlyList<Guid> RoomIds);

public sealed record ListRoomGroupsQuery(Guid PropertyId) : IRequest<Result<IReadOnlyList<RoomGroupDto>>>;

public sealed class ListRoomGroupsHandler(IAppDbContext db) : IRequestHandler<ListRoomGroupsQuery, Result<IReadOnlyList<RoomGroupDto>>>
{
    public async ValueTask<Result<IReadOnlyList<RoomGroupDto>>> Handle(ListRoomGroupsQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == request.PropertyId, cancellationToken))
            return PropertyErrors.PropertyNotFound;

        var groups = await db.RoomGroups.AsNoTracking()
            .Where(g => g.PropertyId == request.PropertyId)
            .OrderBy(g => g.Name)
            .Select(g => new RoomGroupDto(g.Id, g.PropertyId, g.Name, g.Description, g.Members.Select(m => m.RoomId).ToList()))
            .ToListAsync(cancellationToken);
        return groups;
    }
}

public sealed record SaveRoomGroupCommand(Guid? Id, Guid PropertyId, string Name, string? Description) : IRequest<Result<RoomGroupDto>>;

public sealed class SaveRoomGroupCommandValidator : AbstractValidator<SaveRoomGroupCommand>
{
    public SaveRoomGroupCommandValidator()
    {
        RuleFor(x => x.Name).RequiredText(100, "Tên nhóm phòng");
        RuleFor(x => x.Description).OptionalText(500);
    }
}

/// <summary>Tạo (Id = null) hoặc đổi tên nhóm. Tên unique trong khu.</summary>
public sealed class SaveRoomGroupHandler(IAppDbContext db) : IRequestHandler<SaveRoomGroupCommand, Result<RoomGroupDto>>
{
    public async ValueTask<Result<RoomGroupDto>> Handle(SaveRoomGroupCommand request, CancellationToken cancellationToken)
    {
        RoomGroup? group;
        if (request.Id is { } id)
        {
            group = await db.RoomGroups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
            if (group is null)
                return PropertyErrors.RoomGroupNotFound;
        }
        else
        {
            var property = await db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PropertyId, cancellationToken);
            if (property is null)
                return PropertyErrors.PropertyNotFound;
            if (property.IsArchived)
                return PropertyErrors.PropertyArchived;
            group = null;
        }

        var propertyId = group?.PropertyId ?? request.PropertyId;
        var name = request.Name.Trim();
        var nameTaken = await db.RoomGroups.AnyAsync(
            g => g.PropertyId == propertyId && g.Name.ToLower() == name.ToLower() && g.Id != request.Id, cancellationToken);
        if (nameTaken)
            return PropertyErrors.RoomGroupNameTaken;

        if (group is null)
        {
            group = RoomGroup.Create(propertyId, name, request.Description);
            db.RoomGroups.Add(group);
        }
        else
        {
            group.Rename(name, request.Description);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new RoomGroupDto(group.Id, group.PropertyId, group.Name, group.Description, group.Members.Select(m => m.RoomId).ToList());
    }
}

public sealed record DeleteRoomGroupCommand(Guid Id) : IRequest<Result>;

/// <summary>PR-BR-08: khi có M07, chặn xóa nhóm đang được quy tắc điều chỉnh giá tham chiếu.</summary>
public sealed class DeleteRoomGroupHandler(IAppDbContext db) : IRequestHandler<DeleteRoomGroupCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteRoomGroupCommand request, CancellationToken cancellationToken)
    {
        var group = await db.RoomGroups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken);
        if (group is null)
            return Result.Failure(PropertyErrors.RoomGroupNotFound);

        db.RoomGroups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record SetRoomGroupMembersCommand(Guid Id, IReadOnlyList<Guid> RoomIds) : IRequest<Result<RoomGroupDto>>;

public sealed class SetRoomGroupMembersCommandValidator : AbstractValidator<SetRoomGroupMembersCommand>
{
    public SetRoomGroupMembersCommandValidator() =>
        RuleFor(x => x.RoomIds).NotNull().Must(ids => ids.Count <= 500).WithErrorCode("OUT_OF_RANGE");
}

public sealed class SetRoomGroupMembersHandler(IAppDbContext db) : IRequestHandler<SetRoomGroupMembersCommand, Result<RoomGroupDto>>
{
    public async ValueTask<Result<RoomGroupDto>> Handle(SetRoomGroupMembersCommand request, CancellationToken cancellationToken)
    {
        var group = await db.RoomGroups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken);
        if (group is null)
            return PropertyErrors.RoomGroupNotFound;

        // PR-BR-07: phòng phải cùng khu với nhóm (FK composite trong DB là chốt chặn cuối).
        var roomIds = request.RoomIds.Distinct().ToList();
        var validCount = await db.Rooms.CountAsync(r => roomIds.Contains(r.Id) && r.PropertyId == group.PropertyId, cancellationToken);
        if (validCount != roomIds.Count)
            return PropertyErrors.RoomNotInProperty;

        group.SetMembers(roomIds);
        await db.SaveChangesAsync(cancellationToken);
        return new RoomGroupDto(group.Id, group.PropertyId, group.Name, group.Description, group.Members.Select(m => m.RoomId).ToList());
    }
}
