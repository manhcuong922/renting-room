namespace renting_room.Application.Billing;

public sealed record SkippedContract(Guid ContractId, string ContractNo, string RoomCode, string Reason);

public sealed record GenerateInvoicesResult(int Created, int Recalculated, int WithIssues, IReadOnlyList<SkippedContract> Skipped);
