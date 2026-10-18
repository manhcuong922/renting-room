using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
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
    IAppDbContext db, ICurrentUser currentUser, IContractNumberGenerator numberGenerator, TimeProvider clock)
    : IRequestHandler<CreateContractCommand, Result<CreatedWithWarnings>>
{
    public async ValueTask<Result<CreatedWithWarnings>> Handle(CreateContractCommand request, CancellationToken cancellationToken)
    {
        // Khóa hàng phòng: không tạo được nháp đồng thời với lệnh ngừng dùng phòng (PR-BR-05).
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockForUpdateAsync<Room>(request.RoomId, cancellationToken);

        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RoomId, cancellationToken);
        if (room is null)
            return PropertyErrors.RoomNotFound;
        if (room.IsArchived)
            return PropertyErrors.RoomArchived;

        var property = await db.Properties.AsNoTracking().FirstAsync(p => p.Id == room.PropertyId, cancellationToken);
        var data = await ContractDraftBuilder.BuildAsync(db, request.Contract, room, property, currentTemplateId: null, cancellationToken);
        if (data.IsFailure)
            return data.Error!;

        string contractNo;
        if (request.ContractNo is not null)
        {
            contractNo = TextNormalizer.NormalizeCode(request.ContractNo);
            if (await db.Contracts.AnyAsync(c => c.ContractNo == contractNo, cancellationToken))
                return ContractErrors.ContractNoTaken;
        }
        else
        {
            contractNo = await ContractNumbers.NextAsync(db, numberGenerator, currentUser, clock, cancellationToken);
        }

        var contract = Contract.CreateDraft(property.Id, room.Id, contractNo, data.Value!);
        db.Contracts.Add(contract);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreatedWithWarnings(contract.Id, [.. ContractWarnings.For(contract), .. await ContractPaperWarnings.ForAsync(db, contract, clock.GetUtcNow().ToBusinessDate(), cancellationToken)]);
    }
}
