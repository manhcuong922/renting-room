namespace renting_room.Application.Common.Interfaces;

public enum PasswordCheckResult
{
    Failed,
    Success,
    SuccessRehashNeeded
}

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// So khớp mật khẩu. Truyền <paramref name="passwordHash"/> = null khi không tìm thấy user:
    /// hàm vẫn tốn thời gian băm như bình thường để chống dò tài khoản qua thời gian phản hồi.
    /// </summary>
    PasswordCheckResult Verify(string? passwordHash, string password);
}
