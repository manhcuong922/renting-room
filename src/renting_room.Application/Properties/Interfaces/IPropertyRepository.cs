using renting_room.Domain.Properties;

namespace renting_room.Application.Properties;

/// <summary>Lấy / cất aggregate khu trọ. Không chứa luật nghiệp vụ — luật nằm trong <see cref="Property"/>.</summary>
public interface IPropertyRepository
{
    Task<Property?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Khóa hàng khu tới hết transaction rồi nạp. Phải gọi trong transaction.</summary>
    Task<Property?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> IsCodeTakenAsync(string code, CancellationToken cancellationToken);

    void Add(Property property);
}
