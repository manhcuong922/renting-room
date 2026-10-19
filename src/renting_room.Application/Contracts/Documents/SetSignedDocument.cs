using FluentValidation;
using Mediator;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Contracts;

/// <summary>Đánh dấu đã có / chưa có bản HĐ ký (giấy, ảnh, PDF) + ghi chú nơi cất — hết cờ "Thiếu tài liệu". Không ảnh hưởng thu tiền.</summary>
public sealed record SetSignedDocumentCommand(Guid ContractId, bool HasSignedDocument, string? Note) : IRequest<Result>;

public sealed class SetSignedDocumentCommandValidator : AbstractValidator<SetSignedDocumentCommand>
{
    public SetSignedDocumentCommandValidator() => RuleFor(x => x.Note).OptionalText(300);
}

public sealed class SetSignedDocumentHandler(ContractMutation mutation) : IRequestHandler<SetSignedDocumentCommand, Result>
{
    public ValueTask<Result> Handle(SetSignedDocumentCommand request, CancellationToken cancellationToken) =>
        new(mutation.RunAsync(request.ContractId,
            contract => Task.FromResult(contract.SetSignedDocument(request.HasSignedDocument, request.Note)), cancellationToken));
}
