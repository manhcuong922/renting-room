namespace renting_room.Domain.Common;

public abstract class Entity
{
    private List<IDomainEvent>? _domainEvents;

    public Guid Id { get; protected set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents ?? [];

    protected void Raise(IDomainEvent domainEvent) => (_domainEvents ??= []).Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents?.Clear();
}
