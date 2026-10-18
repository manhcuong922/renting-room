using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Domain.Contracts;

/// <summary>
/// Quan hệ của người ở với <b>người đứng tên hợp đồng</b> (CT-BR-28) — dùng làm "quan hệ với chủ hộ" khi những người cùng phòng
/// đăng ký tạm trú chung hộ. Danh mục theo Thông tư 55/2021/TT-BCA Điều 6 (sửa đổi bởi Thông tư 66/2023/TT-BCA).
/// </summary>
public enum OccupantRelationship
{
    // Vợ chồng
    Wife,
    Husband,
    // Cha mẹ
    Father,
    Mother,
    FatherInLaw,      // cha vợ / cha chồng
    MotherInLaw,      // mẹ vợ / mẹ chồng
    AdoptiveFather,
    AdoptiveMother,
    Stepfather,       // cha dượng
    Stepmother,       // mẹ kế
    // Con
    Child,            // con đẻ
    AdoptedChild,     // con nuôi
    StepChild,        // con riêng của vợ / chồng
    SonInLaw,         // con rể
    DaughterInLaw,    // con dâu
    // Ông bà, cháu chắt
    Grandparent,      // ông / bà nội, ngoại
    GreatGrandparent, // cụ nội / ngoại
    Grandchild,       // cháu nội / ngoại
    GreatGrandchild,  // chắt ruột
    // Anh chị em, họ hàng
    Sibling,          // anh / chị / em ruột
    HalfSibling,      // cùng cha khác mẹ / cùng mẹ khác cha
    SiblingInLaw,     // anh rể, em rể, chị dâu, em dâu
    NephewNiece,      // cháu ruột (con của anh chị em)
    UncleAunt,        // bác / chú / cậu / cô / dì ruột
    // Giám hộ
    Guardian,         // người giám hộ
    Ward,             // người được giám hộ
    // Không phải người thân
    CoTenant,         // cùng ở thuê (bạn bè, đồng nghiệp…)
    Other             // khác — bắt buộc ghi rõ
}

/// <summary>Thông tin tối thiểu của một người để kiểm tra quan hệ.</summary>
public sealed record PersonFacts(Guid RenterId, DateOnly DateOfBirth, Gender Gender);

/// <summary>Vi phạm của người ở thứ <see cref="Index"/> trong danh sách được kiểm tra; <see cref="Field"/> là tên field JSON.</summary>
public sealed record OccupantRuleViolation(int Index, string Field, Error Error);

/// <summary>
/// CT-BR-28..30 — kiểm tra quan hệ người ở với người đứng tên:
/// <list type="bullet">
/// <item>Người ở không phải người đứng tên phải khai quan hệ; "Khác" phải ghi rõ.</item>
/// <item>Quan hệ có giới tính (vợ, chồng, cha, mẹ, con rể…) phải khớp giới tính người ở (bỏ qua khi giới tính "Khác").</item>
/// <item>Cha / mẹ đẻ, ông bà, cụ phải lớn tuổi hơn; con đẻ, cháu nội ngoại, chắt phải nhỏ tuổi hơn người đứng tên.</item>
/// <item>Vợ / chồng: tối đa 1 người cùng thời gian; đủ tuổi kết hôn (nam ≥ 20, nữ ≥ 18 — Luật HN&amp;GĐ 2014 Điều 8).</item>
/// <item>Người chưa thành niên: phải có ý kiến đồng ý của cha, mẹ hoặc người giám hộ (Luật Cư trú 2020 Điều 28),
/// trừ khi người đứng tên chính là cha / mẹ / người giám hộ (con đẻ, con nuôi, người được giám hộ).</item>
/// </list>
/// </summary>
public static class OccupantRelationshipRules
{
    public const int MaleMarriageAge = 20;
    public const int FemaleMarriageAge = 18;

    private static readonly HashSet<OccupantRelationship> MustBeOlder =
        [OccupantRelationship.Father, OccupantRelationship.Mother, OccupantRelationship.Grandparent, OccupantRelationship.GreatGrandparent];

    private static readonly HashSet<OccupantRelationship> MustBeYounger =
        [OccupantRelationship.Child, OccupantRelationship.Grandchild, OccupantRelationship.GreatGrandchild];

    private static readonly HashSet<OccupantRelationship> RepresentativeIsLegalGuardian =
        [OccupantRelationship.Child, OccupantRelationship.AdoptedChild, OccupantRelationship.Ward];

    private static readonly Dictionary<OccupantRelationship, Gender> RequiredGender = new()
    {
        [OccupantRelationship.Wife] = Gender.Female,
        [OccupantRelationship.Husband] = Gender.Male,
        [OccupantRelationship.Father] = Gender.Male,
        [OccupantRelationship.Mother] = Gender.Female,
        [OccupantRelationship.FatherInLaw] = Gender.Male,
        [OccupantRelationship.MotherInLaw] = Gender.Female,
        [OccupantRelationship.AdoptiveFather] = Gender.Male,
        [OccupantRelationship.AdoptiveMother] = Gender.Female,
        [OccupantRelationship.Stepfather] = Gender.Male,
        [OccupantRelationship.Stepmother] = Gender.Female,
        [OccupantRelationship.SonInLaw] = Gender.Male,
        [OccupantRelationship.DaughterInLaw] = Gender.Female
    };

    public static bool IsSpouse(OccupantRelationship? r) => r is OccupantRelationship.Wife or OccupantRelationship.Husband;

    /// <param name="reference">Chủ hộ (CT-BR-36) — mặc định là người đứng tên; người này không khai quan hệ.</param>
    public static IReadOnlyList<OccupantRuleViolation> Check(
        PersonFacts reference, IReadOnlyList<(OccupantInput Input, PersonFacts Person)> occupants)
    {
        var violations = new List<OccupantRuleViolation>();
        for (var i = 0; i < occupants.Count; i++)
        {
            var (input, person) = occupants[i];
            if (person.RenterId == reference.RenterId)
                continue; // chủ hộ không khai quan hệ với chính mình

            violations.AddRange(CheckOne(i, input, person, reference));
        }

        violations.AddRange(CheckSingleSpouse(occupants));
        return violations;
    }

    private static IEnumerable<OccupantRuleViolation> CheckOne(int i, OccupantInput input, PersonFacts person, PersonFacts representative)
    {
        if (input.RelationshipType is not { } relation)
        {
            yield return new(i, "relationshipType", ContractErrors.RelationshipRequired);
            yield break;
        }

        if (relation == OccupantRelationship.Other && string.IsNullOrWhiteSpace(input.Relationship))
            yield return new(i, "relationship", ContractErrors.RelationshipNoteRequired);

        if (RequiredGender.TryGetValue(relation, out var gender) && person.Gender != Gender.Other && person.Gender != gender)
            yield return new(i, "relationshipType", ContractErrors.RelationshipGenderMismatch);

        if ((MustBeOlder.Contains(relation) && person.DateOfBirth >= representative.DateOfBirth)
            || (MustBeYounger.Contains(relation) && person.DateOfBirth <= representative.DateOfBirth))
            yield return new(i, "relationshipType", ContractErrors.RelationshipAgeMismatch);

        if (IsSpouse(relation) && !BothOfMarriageAge(relation, person, representative, input.MoveInDate))
            yield return new(i, "relationshipType", ContractErrors.SpouseUnderMarriageAge);

        var isMinor = person.DateOfBirth.AgeOn(input.MoveInDate) < LessorDetails.MinimumAge;
        if (isMinor && !RepresentativeIsLegalGuardian.Contains(relation) && !input.GuardianConsent)
            yield return new(i, "guardianConsent", ContractErrors.GuardianConsentRequired);
    }

    /// <summary>Vợ ⇒ người ở nữ ≥ 18, người đứng tên (chồng) ≥ 20; Chồng ⇒ ngược lại.</summary>
    private static bool BothOfMarriageAge(OccupantRelationship relation, PersonFacts person, PersonFacts representative, DateOnly on)
    {
        var (occupantMin, representativeMin) = relation == OccupantRelationship.Wife
            ? (FemaleMarriageAge, MaleMarriageAge)
            : (MaleMarriageAge, FemaleMarriageAge);
        return person.DateOfBirth.AgeOn(on) >= occupantMin && representative.DateOfBirth.AgeOn(on) >= representativeMin;
    }

    /// <summary>Tối đa 1 vợ/chồng ở cùng thời gian (khoảng ở giao nhau).</summary>
    private static IEnumerable<OccupantRuleViolation> CheckSingleSpouse(IReadOnlyList<(OccupantInput Input, PersonFacts Person)> occupants)
    {
        var spouses = occupants.Select((o, i) => (o.Input, Index: i)).Where(o => IsSpouse(o.Input.RelationshipType)).ToList();
        foreach (var (input, index) in spouses.Skip(1))
        {
            if (spouses.Any(other => other.Index < index && Overlaps(other.Input, input)))
                yield return new(index, "relationshipType", ContractErrors.MultipleSpouses);
        }
    }

    private static bool Overlaps(OccupantInput a, OccupantInput b) =>
        a.MoveInDate <= (b.ExpectedEndDate ?? DateOnly.MaxValue) && b.MoveInDate <= (a.ExpectedEndDate ?? DateOnly.MaxValue);
}
