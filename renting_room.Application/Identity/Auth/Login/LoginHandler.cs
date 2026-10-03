using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Auth.Login;

public sealed class LoginHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    AuthTokenIssuer tokenIssuer,
    TimeProvider clock,
    ILogger<LoginHandler> logger)
    : IRequestHandler<LoginCommand, Result<AuthTokens>>
{
    public async ValueTask<Result<AuthTokens>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var user = await FindUserAsync(request.Username, cancellationToken);

        // Luôn băm mật khẩu (kể cả khi user không tồn tại) để thời gian phản hồi như nhau (ID-BR-13).
        var verification = passwordHasher.Verify(user?.PasswordHash, request.Password);

        if (user is null)
        {
            logger.LogInformation("Login failed: unknown username from {IpAddress}", request.IpAddress);
            return IdentityErrors.InvalidCredentials;
        }

        // Đang bị khóa: trả CÙNG lỗi với sai mật khẩu, dù mật khẩu đúng hay sai. Nếu trả lỗi khác khi mật khẩu đúng,
        // kẻ tấn công vẫn dò được mật khẩu trong thời gian khóa (sai → 401, đúng → 423), và lộ tài khoản có tồn tại.
        if (user.IsLockedOut(now))
        {
            logger.LogWarning("Login rejected: user {UserId} is locked out", user.Id);
            return IdentityErrors.InvalidCredentials;
        }

        if (verification == PasswordCheckResult.Failed)
        {
            await FailedLoginTracker.RegisterFailureAsync(db, user.Id, now, cancellationToken);
            logger.LogInformation("Login failed: wrong password for user {UserId} from {IpAddress}", user.Id, request.IpAddress);
            return IdentityErrors.InvalidCredentials;
        }

        // ID-BR-20: chỉ báo hết hạn khi mật khẩu ĐÚNG — không giúp kẻ dò mật khẩu.
        if (user.IsTemporaryPasswordExpired(now))
            return IdentityErrors.TemporaryPasswordExpired;

        if (user.OrganizationId is { } organizationId)
        {
            var organizationStatus = await db.Organizations
                .Where(o => o.Id == organizationId)
                .Select(o => o.Status)
                .FirstAsync(cancellationToken);

            if (organizationStatus != OrganizationStatus.Active)
                return IdentityErrors.OrganizationSuspended;
        }

        if (verification == PasswordCheckResult.SuccessRehashNeeded)
            await UpgradePasswordHashAsync(user, request.Password, cancellationToken);

        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FailedLoginCount, 0)
                .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null)
                .SetProperty(u => u.LastLoginAt, now), cancellationToken);

        var (tokens, refreshToken) = tokenIssuer.IssueNewSession(user, now, request.IpAddress);
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} logged in from {IpAddress}", user.Id, request.IpAddress);
        return tokens;
    }

    /// <summary>
    /// Chỉ ghi hash mới nếu hash trong DB vẫn là hash vừa kiểm tra — tránh ghi đè mật khẩu
    /// vừa được đổi bởi request khác chạy song song.
    /// </summary>
    private Task UpgradePasswordHashAsync(User user, string password, CancellationToken cancellationToken)
    {
        var verifiedHash = user.PasswordHash;
        var upgradedHash = passwordHasher.Hash(password);

        return db.Users
            .Where(u => u.Id == user.Id && u.PasswordHash == verifiedHash)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, upgradedHash), cancellationToken);
    }

    private Task<User?> FindUserAsync(string username, CancellationToken cancellationToken)
    {
        if (username.Contains('@'))
        {
            var email = ContactNormalizer.NormalizeEmail(username);
            return email is null
                ? Task.FromResult<User?>(null)
                : db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.EmailNormalized == email, cancellationToken);
        }

        var phone = ContactNormalizer.NormalizePhone(username);
        return phone is null
            ? Task.FromResult<User?>(null)
            : db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PhoneNormalized == phone, cancellationToken);
    }
}
