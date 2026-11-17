using Mediator;
using renting_room.Application.Identity.Organizations;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Notifications;
using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

/// <summary>M11 NT-UC-10: bật / tắt tự gửi phiếu khi chốt và cài các mốc nhắc nợ (số ngày sau hạn) của khu.</summary>
public sealed record UpdateNotificationSettingsCommand(Guid PropertyId, bool NotifyOnFinalize, IReadOnlyList<int> OverdueReminderDays)
    : IRequest<Result<PropertyDetailDto>>;

public sealed class UpdateNotificationSettingsHandler(
    IPropertyRepository properties, IOrganizationQueries organizations, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<UpdateNotificationSettingsCommand, Result<PropertyDetailDto>>
{
    public async ValueTask<Result<PropertyDetailDto>> Handle(UpdateNotificationSettingsCommand request, CancellationToken cancellationToken)
    {
        var property = await properties.GetAsync(request.PropertyId, cancellationToken);
        if (property is null)
            return PropertyErrors.PropertyNotFound;

        var updated = property.UpdateNotificationSettings(request.NotifyOnFinalize, request.OverdueReminderDays);
        if (updated.IsFailure)
            return updated.Error!;
        // Thay đổi cột của khu đã vào audit tự động (diff entity); không cần ghi thêm.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await property.ToDetailAsync(organizations, clock.GetUtcNow().ToBusinessDate(), cancellationToken);
    }
}
