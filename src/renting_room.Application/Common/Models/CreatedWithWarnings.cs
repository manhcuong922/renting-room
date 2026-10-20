using renting_room.Domain.Common;

namespace renting_room.Application.Common.Models;

public sealed record CreatedWithWarnings(Guid Id, IReadOnlyList<Warning> Warnings);
