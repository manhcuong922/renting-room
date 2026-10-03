using renting_room.Domain.Common;

namespace renting_room.Domain.Properties;

/// <summary>Nhóm phòng trong cùng 1 khu (VD "Tầng 3") — dùng để áp điều chỉnh giá theo nhóm (M07).</summary>
public sealed class RoomGroup : TenantEntity
{
    private readonly List<RoomGroupMember> _members = [];

    private RoomGroup() { } // EF Core

    public Guid PropertyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public IReadOnlyList<RoomGroupMember> Members => _members;

    public static RoomGroup Create(Guid propertyId, string name, string? description)
    {
        var group = new RoomGroup { Id = Guid.NewGuid(), PropertyId = propertyId };
        group.Rename(name, description);
        return group;
    }

    public void Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name is required.", nameof(name));

        Name = name.Trim();
        Description = TextNormalizer.TrimToNull(description);
    }

    /// <summary>Thay toàn bộ thành viên. Phòng phải cùng khu — kiểm ở Application và chặn bởi FK composite (PR-BR-07).</summary>
    public void SetMembers(IEnumerable<Guid> roomIds)
    {
        var target = roomIds.ToHashSet();
        _members.RemoveAll(m => !target.Contains(m.RoomId));
        foreach (var roomId in target.Where(id => _members.All(m => m.RoomId != id)))
            _members.Add(new RoomGroupMember(Id, PropertyId, roomId));
    }
}

public sealed class RoomGroupMember : TenantEntity
{
    private RoomGroupMember() { } // EF Core

    internal RoomGroupMember(Guid roomGroupId, Guid propertyId, Guid roomId)
    {
        Id = Guid.NewGuid();
        RoomGroupId = roomGroupId;
        PropertyId = propertyId;
        RoomId = roomId;
    }

    public Guid RoomGroupId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid RoomId { get; private set; }
}
