namespace renting_room.Domain.Contracts;

/// <summary>Một điều khoản (mục) của hợp đồng, VD "Trách nhiệm của bên A".</summary>
public sealed record ContractClause(string Heading, string Body);
