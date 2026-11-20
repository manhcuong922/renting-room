using System.Linq.Expressions;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Notifications;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Persistence;

/// <summary>
/// M01 ID-BR-24..26 (08/11/2026): phạm vi khu của quản lý khu, ghép vào global query filter của tổ chức. Mọi điều kiện bắt đầu bằng
/// <c>CurrentPropertyIds == null ||</c> ⇒ chủ trọ / job hệ thống không bị giới hạn. Biểu thức tham chiếu <c>this</c> (như filter tổ chức) ⇒ EF đọc
/// lại <see cref="CurrentPropertyIds"/> của context đang chạy ở mỗi truy vấn.
/// <list type="bullet">
/// <item>Khu: theo id. Bản ghi <see cref="IPropertyScoped"/>: theo <c>PropertyId</c>.</item>
/// <item>Bản ghi con (người ở, dòng phiếu, chỉ số, bản giá, phân bổ…): theo bản ghi cha (HĐ / phiếu / công tơ / khoản thu / phiếu thu) trong phạm vi.</item>
/// <item>Hồ sơ người thuê (không gắn khu — ID-BR-26): có HĐ ở khu được giao, do chính mình tạo, hoặc đã nhập đúng số giấy tờ (<see cref="RenterClaim"/>).</item>
/// </list>
/// </summary>
public partial class AppDbContext
{
    /// <summary>Thứ tự tìm khóa cha của bản ghi con (khóa bắt buộc — Guid không null).</summary>
    private static readonly (string ForeignKey, string ParentSet, Type ParentType)[] ScopedParents =
    [
        ("ContractId", nameof(Contracts), typeof(Contract)),
        ("InvoiceId", nameof(Invoices), typeof(Domain.Billing.Invoice)),
        ("MeterId", nameof(Meters), typeof(Domain.Meters.Meter)),
        ("FeeTypeId", nameof(FeeTypes), typeof(Domain.Fees.FeeType)),
        ("PaymentId", nameof(Payments), typeof(Domain.Payments.Payment)),
    ];

    private LambdaExpression? PropertyScopeFilter(Type type)
    {
        if (type == typeof(Property))
            return (Expression<Func<Property, bool>>)(e => CurrentPropertyIds == null || CurrentPropertyIds.Contains(e.Id));
        if (type == typeof(Renter))
            return (Expression<Func<Renter, bool>>)(e => CurrentPropertyIds == null
                || e.CreatedBy == CurrentUserId
                || RenterClaims.Any(c => c.RenterId == e.Id && c.UserId == CurrentUserId)
                || Contracts.Any(c => CurrentPropertyIds.Contains(c.PropertyId)
                    && (c.RepresentativeRenterId == e.Id || c.Occupants.Any(o => o.RenterId == e.Id) || c.RepresentativeHistory.Any(h => h.RenterId == e.Id))));
        if (type == typeof(RenterConsent))
            return (Expression<Func<RenterConsent, bool>>)(e => CurrentPropertyIds == null || Renters.Any(r => r.Id == e.RenterId));
        // M11 §17: tin của phiếu HOẶC của thông báo chung (hai khóa cha nullable — vòng lặp dưới chỉ nhận Guid không null).
        if (type == typeof(NotificationOutbox))
            return (Expression<Func<NotificationOutbox, bool>>)(e => CurrentPropertyIds == null
                || Invoices.Any(i => i.Id == e.InvoiceId && CurrentPropertyIds.Contains(i.PropertyId))
                || Announcements.Any(a => a.Id == e.AnnouncementId && CurrentPropertyIds.Contains(a.PropertyId)));
        // Bảng của chính cơ chế phạm vi: chỉ lọc tổ chức (RenterClaim trỏ Renter — lọc theo Renter sẽ thành vòng).
        if (type == typeof(RenterClaim) || type == typeof(Domain.Identity.UserPropertyAccess))
            return null;

        var entity = Expression.Parameter(type, "e");
        var ids = Expression.Property(Expression.Constant(this), nameof(CurrentPropertyIds));
        var unrestricted = Expression.Equal(ids, Expression.Constant(null, typeof(Guid[])));
        if (typeof(IPropertyScoped).IsAssignableFrom(type))
            return Expression.Lambda(Expression.OrElse(unrestricted, InScope(ids, Expression.Property(entity, nameof(IPropertyScoped.PropertyId)))), entity);

        foreach (var (foreignKey, parentSet, parentType) in ScopedParents)
        {
            if (type.GetProperty(foreignKey) is not { } property || property.PropertyType != typeof(Guid))
                continue;
            var parent = Expression.Parameter(parentType, "p");
            var match = Expression.Lambda(
                Expression.AndAlso(
                    Expression.Equal(Expression.Property(parent, nameof(Entity.Id)), Expression.Property(entity, property)),
                    InScope(ids, Expression.Property(parent, nameof(IPropertyScoped.PropertyId)))),
                parent);
            var any = Expression.Call(typeof(Queryable), nameof(Queryable.Any), [parentType],
                Expression.Property(Expression.Constant(this), parentSet), Expression.Quote(match));
            return Expression.Lambda(Expression.OrElse(unrestricted, any), entity);
        }
        return null;
    }

    private static MethodCallExpression InScope(Expression ids, Expression propertyId) =>
        Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)], ids, propertyId);

    /// <summary>Thay tham số của biểu thức filter phạm vi bằng tham số của filter tổ chức để ghép thành một lambda.</summary>
    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        public static Expression Replace(Expression body, ParameterExpression from, ParameterExpression to) => new ParameterRebinder(from, to).Visit(body);

        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
