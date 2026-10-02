using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Security;
using renting_room.Domain.Identity;
using renting_room.Infrastructure.Identity;

namespace renting_room.UnitTests.Infrastructure;

public sealed class JwtTokenServiceTests
{
    private const string SigningKey = "unit-test-signing-key-at-least-32-bytes-long!!";
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);

    private static JwtTokenService CreateService(string environment = "Production", string? key = SigningKey)
    {
        var options = Options.Create(new JwtOptions { SigningKey = key, AccessTokenMinutes = 15, RefreshTokenDays = 30 });
        var keyProvider = new JwtSigningKeyProvider(options, new FakeHostEnvironment(environment), NullLogger<JwtSigningKeyProvider>.Instance);
        return new JwtTokenService(options, keyProvider);
    }

    [Fact]
    public void CreateAccessToken_ContainsIdentityClaims_AndExpiresAfterConfiguredMinutes()
    {
        var organizationId = Guid.NewGuid();
        var user = User.CreateOrgOwner(organizationId, "Minh", "0912345678", null, "hash");

        var token = CreateService().CreateAccessToken(user, Now);
        var jwt = new JsonWebToken(token.Value);

        token.ExpiresAt.Should().Be(Now.AddMinutes(15));
        jwt.GetClaim(AppClaimTypes.Subject).Value.Should().Be(user.Id.ToString());
        jwt.GetClaim(AppClaimTypes.OrganizationId).Value.Should().Be(organizationId.ToString());
        jwt.GetClaim(AppClaimTypes.Role).Value.Should().Be("OrgOwner");
        jwt.GetClaim(AppClaimTypes.SecurityStamp).Value.Should().Be(user.SecurityStamp.ToString());
        jwt.GetClaim(AppClaimTypes.MustChangePassword).Value.Should().Be("true");
        jwt.Alg.Should().Be("HS256");
    }

    [Fact]
    public void CreateAccessToken_OmitsOrganizationAndPasswordChangeClaims_ForSystemAdmin()
    {
        var admin = User.CreateSystemAdmin("Admin", "0900000001", null, "hash");

        var jwt = new JsonWebToken(CreateService().CreateAccessToken(admin, Now).Value);

        jwt.TryGetClaim(AppClaimTypes.OrganizationId, out _).Should().BeFalse();
        jwt.TryGetClaim(AppClaimTypes.MustChangePassword, out _).Should().BeFalse();
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsRandomValue_AndMatchingSha256Hash()
    {
        var service = CreateService();

        var first = service.GenerateRefreshToken();
        var second = service.GenerateRefreshToken();

        first.Value.Should().NotBe(second.Value);
        first.Hash.Should().MatchRegex("^[0-9a-f]{64}$");
        service.HashRefreshToken(first.Value).Should().Be(first.Hash);
    }

    [Fact]
    public void SigningKeyProvider_Throws_OutsideDevelopment_WhenKeyMissing()
    {
        var act = () => CreateService(environment: "Production", key: null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:SigningKey*");
    }

    [Fact]
    public void SigningKeyProvider_Throws_WhenKeyTooShort()
    {
        var act = () => CreateService(key: "too-short");

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least 32 bytes*");
    }

    [Fact]
    public void SigningKeyProvider_UsesEphemeralKey_InDevelopment()
    {
        var act = () => CreateService(environment: "Development", key: null);

        act.Should().NotThrow();
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Verify_Succeeds_ForCorrectPassword_AndFails_ForWrongPassword()
    {
        var hash = _hasher.Hash("ChuTro2026x");

        hash.Should().NotContain("ChuTro2026x");
        _hasher.Verify(hash, "ChuTro2026x").Should().Be(PasswordCheckResult.Success);
        _hasher.Verify(hash, "chutro2026x").Should().Be(PasswordCheckResult.Failed);
    }

    [Fact]
    public void Hash_IsSalted()
    {
        _hasher.Hash("same-password1").Should().NotBe(_hasher.Hash("same-password1"));
    }

    [Fact]
    public void Verify_ReturnsFailed_WhenUserDoesNotExist()
    {
        _hasher.Verify(null, "anything1").Should().Be(PasswordCheckResult.Failed);
    }
}
