using renting_room.Domain.Common;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Domain.Contracts;

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

    private static bool BothOfMarriageAge(OccupantRelationship relation, PersonFacts person, PersonFacts representative, DateOnly on)
    {
        var (occupantMin, representativeMin) = relation == OccupantRelationship.Wife
            ? (FemaleMarriageAge, MaleMarriageAge)
            : (MaleMarriageAge, FemaleMarriageAge);
        return person.DateOfBirth.AgeOn(on) >= occupantMin && representative.DateOfBirth.AgeOn(on) >= representativeMin;
    }

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
