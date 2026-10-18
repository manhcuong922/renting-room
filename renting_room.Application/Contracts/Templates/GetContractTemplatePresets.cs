using Mediator;

namespace renting_room.Application.Contracts;

// Truy vấn
/// <summary>Mẫu gợi ý dựng sẵn trong code — UI dùng làm điểm xuất phát rồi POST để tạo mẫu của tổ chức.</summary>
public sealed record GetContractTemplatePresetsQuery : IRequest<IReadOnlyList<ContractTemplateInput>>;

public sealed class GetContractTemplatePresetsHandler : IRequestHandler<GetContractTemplatePresetsQuery, IReadOnlyList<ContractTemplateInput>>
{
    public ValueTask<IReadOnlyList<ContractTemplateInput>> Handle(GetContractTemplatePresetsQuery request, CancellationToken cancellationToken) =>
        new(ContractTemplatePresets.All);
}
