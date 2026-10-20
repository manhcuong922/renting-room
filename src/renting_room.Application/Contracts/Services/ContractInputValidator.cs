using FluentValidation;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

internal sealed class ContractInputValidator : AbstractValidator<ContractInput>
{
    public const int MaxOccupantsInRequest = 20;

    public ContractInputValidator(TimeProvider clock)
    {
        RuleFor(x => x.RepresentativeRenterId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.EndDate)
            .Must((x, end) => end is null || (end > x.StartDate && end <= x.StartDate.AddYears(10)))
            .WithErrorCode("INVALID_END_DATE").WithMessage("Ngày kết thúc phải sau ngày bắt đầu và không quá 10 năm.");
        RuleFor(x => x.StartDate)
            .Must((x, d) => d >= clock.GetUtcNow().ToBusinessDate().AddYears(x.BillingStartDate is null ? -1 : -10))
            .WithErrorCode("INVALID_START_DATE").WithMessage("Ngày bắt đầu không được trước hôm nay quá 1 năm (HĐ cũ: nhập \"Tính tiền từ ngày\").");
        RuleFor(x => x.BillingStartDate)
            .Must(d => d is null || d >= clock.GetUtcNow().ToBusinessDate().AddYears(-1))
            .WithErrorCode("INVALID_BILLING_START_DATE").WithMessage("\"Tính tiền từ ngày\" không được trước hôm nay quá 1 năm.");
        RuleFor(x => x.SignedDate)
            .Must(d => d is null || d <= clock.GetUtcNow().ToBusinessDate())
            .WithErrorCode("INVALID_SIGNED_DATE").WithMessage("Ngày ký không được ở tương lai.");
        RuleFor(x => x.EffectiveDate)
            .Must((x, d) => d is null || ((x.SignedDate is null || d >= x.SignedDate) && (x.EndDate is null || d <= x.EndDate)))
            .WithErrorCode("INVALID_EFFECTIVE_DATE").WithMessage("Ngày hiệu lực không được trước ngày ký hoặc sau ngày kết thúc.");
        RuleFor(x => x.SignedPlace).OptionalText(200);
        RuleFor(x => x.MonthlyRent).OptionalMoney().Must(v => v is null || v > 0)
            .WithErrorCode("INVALID_AMOUNT").WithMessage("Giá thuê phải lớn hơn 0.");
        RuleFor(x => x.DepositAmount).OptionalMoney();
        RuleFor(x => x.DepositTerms).OptionalText(5000);
        RuleFor(x => x.BillingStartDate)
            .Must((x, d) => d is null || (d >= x.StartDate && (x.EndDate is null || d <= x.EndDate)))
            .WithErrorCode("INVALID_BILLING_START_DATE").WithMessage("\"Tính tiền từ ngày\" phải từ ngày bắt đầu tới ngày kết thúc hợp đồng.");
        RuleFor(x => x.NoticeDays).InclusiveBetween(0, 180).When(x => x.NoticeDays is not null).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.PaymentMethods)
            .Must(m => m is null || (m.Count > 0 && m.All(Enum.IsDefined)))
            .WithErrorCode("INVALID_PAYMENT_METHODS").WithMessage("Chọn ít nhất 1 phương thức thanh toán hợp lệ.");
        RuleFor(x => x.CopiesCount).InclusiveBetween(1, 10).When(x => x.CopiesCount is not null).WithErrorCode("OUT_OF_RANGE");
        RuleFor(x => x.TermsText).OptionalText(50_000);
        RuleFor(x => x.Note).OptionalText(2000);
        RuleFor(x => x.Occupants)
            .Must(o => o is null || o.Count <= MaxOccupantsInRequest).WithErrorCode("OUT_OF_RANGE")
            .Must(o => o is null || o.Select(x => x.RenterId).Distinct().Count() == o.Count)
            .WithErrorCode("DUPLICATE_OCCUPANT").WithMessage("Một người chỉ xuất hiện 1 lần trong danh sách người ở.");
        RuleForEach(x => x.Occupants).ChildRules(o =>
        {
            o.RuleFor(x => x.RenterId).NotEmpty().WithErrorCode("REQUIRED");
            o.RuleFor(x => x.Relationship).OptionalText(50);
            o.RuleFor(x => x.RelationshipType).IsInEnum().When(x => x.RelationshipType is not null);
            o.RuleFor(x => x.Note).OptionalText(500);
        });
        RuleFor(x => x.ContractType).IsInEnum().When(x => x.ContractType is not null);
        RuleFor(x => x.Title).OptionalText(200);
        this.ClausesRules(x => x.Clauses, "clauses");
        RuleFor(x => x.Fees)
            .Must(f => f is null || f.Count <= ContractFeeRules.MaxFees).WithErrorCode("OUT_OF_RANGE")
            .WithMessage($"Tối đa {ContractFeeRules.MaxFees} khoản thu.")
            .Must(f => f is null || f.Select(x => x.FeeTypeId).Distinct().Count() == f.Count)
            .WithErrorCode("DUPLICATE_FEE").WithMessage("Mỗi khoản thu chỉ gắn 1 lần.");
        RuleForEach(x => x.Fees).ChildRules(f =>
        {
            f.RuleFor(x => x.Quantity).Must(ContractFeeRules.IsValidQuantity).WithErrorCode("OUT_OF_RANGE")
                .WithMessage("Số lượng 0–100, tối đa 2 số lẻ.");
            f.RuleFor(x => x.UnitPriceOverride).Must(ContractFeeRules.IsValidOverride).WithErrorCode("INVALID_AMOUNT")
                .WithMessage("Giá riêng 0–50.000.000đ, tối đa 2 số lẻ.");
        });
        RuleFor(x => x.CustomFields).Must(f => f is null || f.Count <= ContractDocumentRules.MaxFields)
            .WithErrorCode("OUT_OF_RANGE").WithMessage($"Tối đa {ContractDocumentRules.MaxFields} trường.");
        RuleForEach(x => x.Occupants)
            .Must((x, o) => o.MoveInDate is null || o.MoveInDate >= x.StartDate)
            .WithErrorCode("DATE_OUTSIDE_CONTRACT").WithMessage("Ngày vào ở không được trước ngày bắt đầu hợp đồng.");
    }
}
