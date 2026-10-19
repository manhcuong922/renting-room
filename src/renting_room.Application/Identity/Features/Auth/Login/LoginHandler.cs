using Mediator;
using renting_room.Application.Identity.Organizations;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Auth.Login;

public sealed class LoginHandler(
    IUserRepository users,
    IUserQueries userQueries,
    IOrganizationQueries organizations,
    IRefreshTokenRepository refreshTokens,
    ILoginEventRepository loginEvents,
    IUnitOfWork unitOfWork,
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
        var (result, outcome) = await AttemptAsync(request, user, now, cancellationToken);

        // M01 §11: mỗi lần đăng nhập thành công / thất bại ghi 1 dòng nhật ký (không lưu tên đăng nhập gõ vào).
        loginEvents.Add(LoginEvent.Record(user, request.Username, request.IpAddress, outcome, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<(Result<AuthTokens> Result, LoginOutcome Outcome)> AttemptAsync(
        LoginCommand request, User? user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Luôn băm mật khẩu (kể cả khi user không tồn tại) để thời gian phản hồi như nhau (ID-BR-13).
        var verification = passwordHasher.Verify(user?.PasswordHash, request.Password);

        if (user is null)
        {
            logger.LogInformation("Login failed: unknown username from {IpAddress}", request.IpAddress);
            return (IdentityErrors.InvalidCredentials, LoginOutcome.UnknownUser);
        }

        // Đang bị khóa: trả CÙNG lỗi với sai mật khẩu, dù mật khẩu đúng hay sai. Nếu trả lỗi khác khi mật khẩu đúng,
        // kẻ tấn công vẫn dò được mật khẩu trong thời gian khóa (sai → 401, đúng → 423), và lộ tài khoản có tồn tại.
        if (user.IsLockedOut(now))
        {
            logger.LogWarning("Login rejected: user {UserId} is locked out", user.Id);
            return (IdentityErrors.InvalidCredentials, LoginOutcome.LockedOut);
        }

        if (verification == PasswordCheckResult.Failed)
        {
            await users.RegisterFailedLoginAsync(user.Id, now, cancellationToken);
            logger.LogInformation("Login failed: wrong password for user {UserId} from {IpAddress}", user.Id, request.IpAddress);
            return (IdentityErrors.InvalidCredentials, LoginOutcome.WrongPassword);
        }

        // ID-BR-20: chỉ báo hết hạn khi mật khẩu ĐÚNG — không giúp kẻ dò mật khẩu.
        if (user.IsTemporaryPasswordExpired(now))
            return (IdentityErrors.TemporaryPasswordExpired, LoginOutcome.TemporaryPasswordExpired);

        if (user.OrganizationId is { } organizationId)
        {
            var organizationStatus = (await organizations.GetStatusAsync(organizationId, cancellationToken))!.Value;

            if (organizationStatus != OrganizationStatus.Active)
                return (IdentityErrors.OrganizationSuspended, LoginOutcome.OrganizationSuspended);
        }

        if (verification == PasswordCheckResult.SuccessRehashNeeded)
            await UpgradePasswordHashAsync(user, request.Password, cancellationToken);

        await users.RecordSuccessfulLoginAsync(user.Id, now, cancellationToken);

        var (tokens, refreshToken) = tokenIssuer.IssueNewSession(user, now, request.IpAddress);
        refreshTokens.Add(refreshToken);

        logger.LogInformation("User {UserId} logged in from {IpAddress}", user.Id, request.IpAddress);
        return (tokens, LoginOutcome.Success);
    }

    /// <summary>
    /// Chỉ ghi hash mới nếu hash trong DB vẫn là hash vừa kiểm tra — tránh ghi đè mật khẩu
    /// vừa được đổi bởi request khác chạy song song.
    /// </summary>
    private Task UpgradePasswordHashAsync(User user, string password, CancellationToken cancellationToken)
    {
        var verifiedHash = user.PasswordHash;
        var upgradedHash = passwordHasher.Hash(password);

        return users.UpgradePasswordHashAsync(user.Id, verifiedHash, upgradedHash, cancellationToken);
    }

    private Task<User?> FindUserAsync(string username, CancellationToken cancellationToken)
    {
        if (username.Contains('@'))
        {
            var email = ContactNormalizer.NormalizeEmail(username);
            return email is null
                ? Task.FromResult<User?>(null)
                : userQueries.FindByEmailAsync(email, cancellationToken);
        }

        var phone = ContactNormalizer.NormalizePhone(username);
        return phone is null
            ? Task.FromResult<User?>(null)
            : userQueries.FindByPhoneAsync(phone, cancellationToken);
    }
}
