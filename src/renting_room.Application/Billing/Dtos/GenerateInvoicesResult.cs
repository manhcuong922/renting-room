namespace renting_room.Application.Billing;

public sealed record UnbilledPeriodDto(DateOnly Start, DateOnly End);

/// <param name="UnbilledPeriods">Chỉ có khi <c>Reason = PREVIOUS_PERIOD_NOT_BILLED</c>: các kỳ chưa lập (BL-BR-42) để giao diện hỏi "Bỏ qua N kỳ?".</param>
public sealed record SkippedContract(
    Guid ContractId, string ContractNo, string RoomCode, string Reason, IReadOnlyList<UnbilledPeriodDto>? UnbilledPeriods = null);

public sealed record GenerateInvoicesResult(
    int Created, int Recalculated, int WithIssues, IReadOnlyList<SkippedContract> Skipped, int GapsCreated = 0);
