using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;
using AspNetPasswordHasher = Microsoft.AspNetCore.Identity.PasswordHasher<renting_room.Domain.Identity.User>;
using AspNetVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace renting_room.Infrastructure.Identity;

/// <summary>PBKDF2 (HMAC-SHA512, 100.000 vòng — định dạng V3 của ASP.NET Core Identity).</summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private static readonly AspNetPasswordHasher Hasher = new();

    // Hash giả để khi user không tồn tại vẫn tốn thời gian băm tương đương (chống dò tài khoản).
    private static readonly string DummyHash = Hasher.HashPassword(null!, Guid.NewGuid().ToString());

    public string Hash(string password) => Hasher.HashPassword(null!, password);

    public PasswordCheckResult Verify(string? passwordHash, string password)
    {
        if (passwordHash is null)
        {
            Hasher.VerifyHashedPassword(null!, DummyHash, password);
            return PasswordCheckResult.Failed;
        }

        return Hasher.VerifyHashedPassword(null!, passwordHash, password) switch
        {
            AspNetVerificationResult.Success => PasswordCheckResult.Success,
            AspNetVerificationResult.SuccessRehashNeeded => PasswordCheckResult.SuccessRehashNeeded,
            _ => PasswordCheckResult.Failed
        };
    }
}
