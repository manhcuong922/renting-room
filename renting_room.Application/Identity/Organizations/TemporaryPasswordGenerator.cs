using System.Security.Cryptography;

namespace renting_room.Application.Identity.Organizations;

/// <summary>Sinh mật khẩu tạm 12 ký tự bằng CSPRNG, luôn có chữ thường, chữ hoa và số (ID-BR-10).</summary>
public static class TemporaryPasswordGenerator
{
    // Bỏ các ký tự dễ nhầm khi đọc/gõ lại: 0/O, 1/l/I.
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string All = Lower + Upper + Digits;
    private const int Length = 12;

    public static string Generate()
    {
        var chars = new char[Length];
        chars[0] = Pick(Lower);
        chars[1] = Pick(Upper);
        chars[2] = Pick(Digits);
        for (var i = 3; i < Length; i++)
            chars[i] = Pick(All);

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
