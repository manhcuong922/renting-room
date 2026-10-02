using renting_room.Application.Identity.Auth.ChangePassword;
using renting_room.Application.Identity.Organizations;
using renting_room.Application.Identity.Organizations.CreateOrganization;

namespace renting_room.UnitTests.Application;

public sealed class TemporaryPasswordGeneratorTests
{
    [Fact]
    public void Generate_ReturnsTwelveChars_WithLowerUpperAndDigit()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = TemporaryPasswordGenerator.Generate();

            password.Should().HaveLength(12);
            password.Should().Match(p => p.Any(char.IsLower) && p.Any(char.IsUpper) && p.Any(char.IsDigit));
            password.Should().NotContainAny("0", "O", "1", "l", "I");
        }
    }

    [Fact]
    public void Generate_ProducesDifferentPasswords()
    {
        var passwords = Enumerable.Range(0, 100).Select(_ => TemporaryPasswordGenerator.Generate()).ToHashSet();

        passwords.Should().HaveCount(100);
    }
}

public sealed class ChangePasswordCommandValidatorTests
{
    private readonly ChangePasswordCommandValidator _validator = new();

    [Theory]
    [InlineData("short1")]       // < 8 ký tự
    [InlineData("onlyletters")]  // không có số
    [InlineData("1234567890")]   // không có chữ
    [InlineData("")]
    public void Rejects_WeakNewPassword(string newPassword)
    {
        var result = _validator.Validate(new ChangePasswordCommand("Current123", newPassword, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public void Accepts_StrongPassword()
    {
        _validator.Validate(new ChangePasswordCommand("Current123", "ChuTro2026x", null)).IsValid.Should().BeTrue();
    }
}

public sealed class CreateOrganizationCommandValidatorTests
{
    private readonly CreateOrganizationCommandValidator _validator = new();

    private static CreateOrganizationCommand Valid() => new(
        "NT-01", "Nhà trọ A", null, null, null, null, null,
        new CreateOrganizationOwner("Nguyễn Văn A", "0912345678", null));

    [Fact]
    public void Accepts_ValidCommand()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_OwnerWithoutPhoneOrEmail()
    {
        var command = Valid() with { Owner = new CreateOrganizationOwner("Nguyễn Văn A", null, null) };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(e => e.ErrorCode == "USERNAME_REQUIRED");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("quá-dài-quá-dài-quá-dài-quá-dài-quá-dài")]
    public void Rejects_InvalidCode(string code)
    {
        _validator.Validate(Valid() with { Code = code }).Errors
            .Should().Contain(e => e.PropertyName == nameof(CreateOrganizationCommand.Code));
    }

    [Fact]
    public void Rejects_InvalidPhoneAndTaxCode()
    {
        var command = Valid() with
        {
            TaxCode = "12345",
            Owner = new CreateOrganizationOwner("A", "0123", null)
        };

        var codes = _validator.Validate(command).Errors.Select(e => e.ErrorCode).ToList();

        codes.Should().Contain("INVALID_PHONE").And.Contain("INVALID_TAX_CODE");
    }
}
