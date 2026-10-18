using renting_room.Domain.Common;

namespace renting_room.Application.Common.Models;

/// <summary>Kết quả tạo mới kèm cảnh báo mềm (không chặn) — 201 <c>{ id, warnings }</c>.</summary>
public sealed record CreatedWithWarnings(Guid Id, IReadOnlyList<Warning> Warnings);
