using FluentValidation;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Billing;

namespace renting_room.Application.Properties;

internal static class PropertyRules
{
    public static void AddressRules<T>(this AbstractValidator<T> v, Func<T, AddressInput> address)
    {
        v.RuleFor(x => address(x)).NotNull().OverridePropertyName("address").WithErrorCode("REQUIRED");
        v.When(x => address(x) is not null, () =>
        {
            v.RuleFor(x => address(x).StreetAddress).RequiredText(300, "Số nhà, đường").OverridePropertyName("address.streetAddress");
            v.RuleFor(x => address(x).CommuneName).RequiredText(100, "Xã/phường").OverridePropertyName("address.communeName");
            v.RuleFor(x => address(x).ProvinceName).RequiredText(100, "Tỉnh/thành phố").OverridePropertyName("address.provinceName");
            v.RuleFor(x => address(x).CommuneCode).OptionalText(10).OverridePropertyName("address.communeCode");
            v.RuleFor(x => address(x).ProvinceCode).OptionalText(10).OverridePropertyName("address.provinceCode");
        });
    }

    /// <summary>PR-BR-09: ngày chốt 1–28 (tháng nào cũng có ngày chốt).</summary>
    public static void BillingRules<T>(this AbstractValidator<T> v, Func<T, PropertyBillingInput?> billing)
    {
        v.When(x => billing(x) is not null, () =>
        {
            v.RuleFor(x => billing(x)!.AnchorDay).InclusiveBetween(BillingSchedule.MinAnchorDay, BillingSchedule.MaxAnchorDay)
                .OverridePropertyName("billing.anchorDay").WithErrorCode("OUT_OF_RANGE");
            v.RuleFor(x => billing(x)!.PaymentDueDays).InclusiveBetween(0, 60).OverridePropertyName("billing.paymentDueDays").WithErrorCode("OUT_OF_RANGE");
            v.RuleFor(x => billing(x)!.NoticeDays).InclusiveBetween(0, 180).OverridePropertyName("billing.noticeDays").WithErrorCode("OUT_OF_RANGE");
            v.RuleFor(x => billing(x)!.ChargeMode).IsInEnum().OverridePropertyName("billing.chargeMode");
            v.RuleFor(x => billing(x)!.ProrationMode).IsInEnum().OverridePropertyName("billing.prorationMode");
        });
    }

    public static void LandRules<T>(this AbstractValidator<T> v, Func<T, LandParcelInput?> land)
    {
        v.When(x => land(x) is not null, () =>
        {
            v.RuleFor(x => land(x)!.ParcelNo).OptionalText(20).OverridePropertyName("land.parcelNo");
            v.RuleFor(x => land(x)!.MapSheetNo).OptionalText(20).OverridePropertyName("land.mapSheetNo");
            v.RuleFor(x => land(x)!.OwnershipCertificateNo).OptionalText(50).OverridePropertyName("land.ownershipCertificateNo");
        });
    }
}
