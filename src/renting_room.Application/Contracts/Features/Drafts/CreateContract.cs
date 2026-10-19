using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Application.Properties;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Contracts;

public sealed record CreateContractCommand(Guid RoomId, string? ContractNo, ContractInput Contract) : IRequest<Result<CreatedWithWarnings>>;

public sealed class CreateContractCommandValidator : AbstractValidator<CreateContractCommand>
{
    public CreateContractCommandValidator(TimeProvider clock)
    {
        RuleFor(x => x.RoomId).NotEmpty().WithErrorCode("REQUIRED");
        RuleFor(x => x.ContractNo!).Code(30, "Số hợp đồng").When(x => x.ContractNo is not null);
        RuleFor(x => x.Contract).NotNull().SetValidator(new ContractInputValidator(clock));
    }
}

public sealed class CreateContractHandler(
    IUnitOfWork unitOfWork, IContractRepository contracts, IContractQueries contractQueries, IRoomQueries rooms, IPropertyQueries properties,
    ContractDraftBuilder builder, ContractPaperWarnings paperWarnings, ICurrentUser currentUser, IContractNumberGenerator numberGenerator,
    TimeProvider clock)
    : IRequestHandler<CreateContractCommand, Result<CreatedWithWarnings>>
{
    public async ValueTask<Result<CreatedWithWarnings>> Handle(CreateContractCommand request, CancellationToken cancellationToken)
    {
        // Khóa hàng phòng: không tạo được nháp đồng thời với lệnh ngừng dùng phòng (PR-BR-05).
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockForUpdateAsync<Room>(request.RoomId, cancellationToken);

        var room = await rooms.FindAsync(request.RoomId, cancellationToken);
        if (room is null)
            return PropertyErrors.RoomNotFound;
        if (room.IsArchived)
            return PropertyErrors.RoomArchived;

        var property = (await properties.GetAsync(room.PropertyId, cancellationToken))!;
        var data = await builder.BuildAsync(request.Contract, room, property, currentTemplateId: null, cancellationToken);
        if (data.IsFailure)
            return data.Error!;

        string contractNo;
        if (request.ContractNo is not null)
        {
            contractNo = TextNormalizer.NormalizeCode(request.ContractNo);
            if (await contractQueries.IsContractNoTakenAsync(contractNo, cancellationToken))
                return ContractErrors.ContractNoTaken;
        }
        else
        {
            contractNo = await ContractNumbers.NextAsync(contractQueries, numberGenerator, currentUser, clock, cancellationToken);
        }

        var contract = Contract.CreateDraft(property.Id, room.Id, contractNo, data.Value!);
        contracts.Add(contract);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreatedWithWarnings(contract.Id, [.. ContractWarnings.For(contract), .. await paperWarnings.ForAsync(contract, clock.GetUtcNow().ToBusinessDate(), cancellationToken)]);
    }
}
