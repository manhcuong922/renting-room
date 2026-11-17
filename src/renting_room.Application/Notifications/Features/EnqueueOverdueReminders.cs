using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Notifications;

namespace renting_room.Application.Notifications;

/// <summary>
/// NT-UC-06 / NT-BR-06: tạo tin nhắc nợ cho phiếu quá hạn của khu đã cài mốc. Chạy theo từng tổ chức (job nền). Job lỡ ngày chạy bù ⇒ chỉ tạo
/// bước lớn nhất đã tới hạn. Chạy hai lần / hai instance ⇒ không tin trùng (UNIQUE invoice_id, channel, kind, step).
/// </summary>
/// <returns>Số tin đã tạo.</returns>
public sealed record EnqueueOverdueRemindersCommand(DateOnly AsOf) : IRequest<int>;

public sealed class EnqueueOverdueRemindersHandler(
    INotificationRepository notifications, NotificationEnqueuer enqueuer, IUnitOfWork unitOfWork)
    : IRequestHandler<EnqueueOverdueRemindersCommand, int>
{
    public async ValueTask<int> Handle(EnqueueOverdueRemindersCommand request, CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var row in await notifications.ListOverdueAsync(request.AsOf, cancellationToken))
        {
            if (OverdueReminderPolicy.DueStep(row.DueDate, request.AsOf, row.ConfiguredDays, row.SentSteps) is not { } step)
                continue;
            created += (await enqueuer.EnqueueAsync(row.InvoiceId, row.RenterId, NotificationKind.OverdueReminder, step, null, cancellationToken)).Count;
        }
        if (created > 0)
        {
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (unitOfWork.IsSaveFailure(ex))
            {
                // Instance khác vừa tạo cùng tin (unique) — lần chạy sau tự ổn.
                unitOfWork.ClearTracking();
                return 0;
            }
        }
        return created;
    }
}
