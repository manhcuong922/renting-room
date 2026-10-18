namespace renting_room.Domain.Exceptions;

/// <summary>Vi phạm bất biến của domain (lỗi lập trình hoặc dữ liệu không hợp lệ lọt qua validation).</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
