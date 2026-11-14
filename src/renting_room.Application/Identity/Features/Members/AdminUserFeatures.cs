using Mediator;
using Microsoft.Extensions.Logging;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Identity;

namespace renting_room.Application.Identity.Members;

/// <summary>
/// SystemAdmin chỉ quản lý TÀI KHOẢN (cấp lại mật khẩu, khóa, mở khóa) — không xem dữ liệu trọ (ID-BR-11).
/// Không tác động được tài khoản SystemAdmin khác qua các lệnh này.
/// </summary>
public sealed record AdminResetPasswordCommand(Guid UserId) : IRequest<Result<AdminPasswordResetResult>>;

/// <param name="TemporaryPassword">null khi mật khẩu đã gửi tới email của chính chủ — admin không bao giờ thấy (M-14).</param>
/// <param name="DeliveredTo">Email đã che (VD <c>c***@gmail.com</c>) khi gửi thư; null khi tài khoản chưa có email.</param>
public sealed record AdminPasswordResetResult(Guid UserId, string Username, string? TemporaryPassword, DateTimeOffset ExpiresAt, string? DeliveredTo);

/// <summary>
/// M-14 (ID-BR-11): tài khoản có email ⇒ mật khẩu tạm gửi tới email đó (admin không nhận), gửi lỗi ⇒ hủy cấp lại (mật khẩu cũ còn dùng được).
/// Tài khoản chưa có email: TẠM giữ cách cũ (admin nhận mật khẩu trong response) — chờ kênh Zalo M11 / bắt buộc email.
/// </summary>
public sealed class AdminResetPasswordHandler(
    IUserRepository users, IRefreshTokenRepository refreshTokens, IUnitOfWork unitOfWork, IPasswordHasher passwordHasher,
    IUserSessionStore sessionStore, IEmailSender emailSender, TimeProvider clock, ILogger<AdminResetPasswordHandler> logger)
    : IRequestHandler<AdminResetPasswordCommand, Result<AdminPasswordResetResult>>
{
    private const string RemovedAccountDomain = "@removed.invalid";

    public async ValueTask<Result<AdminPasswordResetResult>> Handle(AdminResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetNonAdminForUpdateAsync(request.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;

        var email = user.EmailNormalized is { Length: > 0 } address && !address.EndsWith(RemovedAccountDomain, StringComparison.Ordinal) ? address : null;
        var result = await PasswordReset.ResetAsync(unitOfWork, refreshTokens, passwordHasher, sessionStore, clock, user, cancellationToken,
            email is null ? null : credentials => emailSender.SendAsync(Compose(user.FullName, email, credentials), cancellationToken));
        if (result.IsFailure)
        {
            if (email is not null)
                logger.LogWarning("Admin reset password of user {UserId} cancelled: email not delivered ({Code})", user.Id, result.Error!.Code);
            return result.Error!;
        }

        logger.LogInformation("Admin reset password of user {UserId}, delivered by {Channel}", user.Id, email is null ? "response" : "email");
        var credentials = result.Value!;
        return email is null
            ? new AdminPasswordResetResult(credentials.UserId, credentials.Username, credentials.TemporaryPassword, credentials.ExpiresAt, null)
            : new AdminPasswordResetResult(credentials.UserId, credentials.Username, null, credentials.ExpiresAt, Mask(email));
    }

    private static EmailMessage Compose(string fullName, string email, TemporaryCredentials credentials) => new(email,
        "Mật khẩu tạm mới cho tài khoản Renting Room",
        $"""
        Xin chào {fullName},

        Quản trị hệ thống vừa cấp lại mật khẩu cho tài khoản của bạn.

        Tên đăng nhập: {credentials.Username}
        Mật khẩu tạm: {credentials.TemporaryPassword}

        Mật khẩu tạm có hiệu lực đến {credentials.ExpiresAt.ToOffset(VietnamTime.Offset):HH:mm dd/MM/yyyy} (giờ Việt Nam). Bạn phải đổi mật khẩu ngay ở lần đăng nhập đầu tiên.

        Nếu bạn không nhờ cấp lại mật khẩu, hãy liên hệ quản trị hệ thống ngay.
        """);

    /// <summary>"chu.tro@gmail.com" ⇒ "c***@gmail.com".</summary>
    private static string Mask(string email) => email.IndexOf('@') is var at and > 0 ? $"{email[0]}***{email[at..]}" : "***";
}

public sealed record AdminSetUserLockCommand(Guid UserId, bool Locked) : IRequest<Result>;

public sealed class AdminSetUserLockHandler(
    IUserRepository users, IRefreshTokenRepository refreshTokens, IUnitOfWork unitOfWork, IUserSessionStore sessionStore, TimeProvider clock,
    ILogger<AdminSetUserLockHandler> logger)
    : IRequestHandler<AdminSetUserLockCommand, Result>
{
    public async ValueTask<Result> Handle(AdminSetUserLockCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetNonAdminForUpdateAsync(request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure(IdentityErrors.UserNotFound);

        if (request.Locked && user.Role == UserRole.OrgOwner)
            return Result.Failure(IdentityErrors.CannotLockOwner);

        var result = request.Locked ? user.Lock() : user.Unlock();
        if (result.IsFailure)
            return result;

        var now = clock.GetUtcNow();
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (request.Locked)
            await AccountSessions.RevokeAllAsync(refreshTokens, user.Id, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        sessionStore.Invalidate(user.Id);

        logger.LogInformation("Admin {Action} user {UserId}", request.Locked ? "locked" : "unlocked", user.Id);
        return Result.Success();
    }
}
