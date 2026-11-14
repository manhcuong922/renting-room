namespace renting_room.Domain.Common;

/// <summary>
/// M01 ID-BR-24 (08/11/2026): bản ghi thuộc <b>một khu</b> — quản lý khu chỉ thấy / ghi khi <see cref="PropertyId"/> nằm trong các khu được giao
/// (global query filter + chặn ghi ở SaveChanges). Bản ghi con (dòng phiếu, chỉ số…) theo bản ghi cha.
/// </summary>
public interface IPropertyScoped
{
    Guid PropertyId { get; }
}
