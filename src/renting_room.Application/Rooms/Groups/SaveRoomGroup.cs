using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Application.Properties;

namespace renting_room.Application.Rooms;

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
public sealed class SaveRoomGroupHandler(IRoomGroupRepository groups, IPropertyQueries properties, IUnitOfWork unitOfWork)
    : IRequestHandler<SaveRoomGroupCommand, Result<RoomGroupDto>>
{
    public async ValueTask<Result<RoomGroupDto>> Handle(SaveRoomGroupCommand request, CancellationToken cancellationToken)
    {
        RoomGroup? group;
        if (request.Id is { } id)
        {
            group = await groups.GetAsync(id, cancellationToken);
            if (group is null)
                return PropertyErrors.RoomGroupNotFound;
        }
        else
        {
            var property = await properties.GetStatusAsync(request.PropertyId, cancellationToken);
            if (property is null)
                return PropertyErrors.PropertyNotFound;
            if (property.IsArchived)
                return PropertyErrors.PropertyArchived;
            group = null;
        }

        var propertyId = group?.PropertyId ?? request.PropertyId;
        var name = request.Name.Trim();
        var nameTaken = await groups.IsNameTakenAsync(propertyId, name, request.Id, cancellationToken);
        if (nameTaken)
            return PropertyErrors.RoomGroupNameTaken;

        if (group is null)
        {
            group = RoomGroup.Create(propertyId, name, request.Description);
            groups.Add(group);
        }
        else
        {
            group.Rename(name, request.Description);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RoomGroupDto(group.Id, group.PropertyId, group.Name, group.Description, group.Members.Select(m => m.RoomId).ToList());
    }
}
