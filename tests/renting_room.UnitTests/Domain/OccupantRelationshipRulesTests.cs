using renting_room.Domain.Contracts;
using renting_room.Domain.Renters;

namespace renting_room.UnitTests.Domain;

public sealed class OccupantRelationshipRulesTests
{
    private static readonly DateOnly MoveIn = new(2026, 2, 1);
    private static readonly PersonFacts Father = new(Guid.NewGuid(), new DateOnly(1985, 5, 1), Gender.Male);

    private static PersonFacts Person(int birthYear, Gender gender) => new(Guid.NewGuid(), new DateOnly(birthYear, 6, 1), gender);

    private static (OccupantInput, PersonFacts) Stay(
        PersonFacts person, OccupantRelationship? relation, bool consent = false, string? note = null, DateOnly? moveIn = null, DateOnly? end = null) =>
        (new OccupantInput(person.RenterId, moveIn ?? MoveIn, end, note, null, relation, consent), person);

    private static IReadOnlyList<string> Codes(params (OccupantInput, PersonFacts)[] occupants) =>
        OccupantRelationshipRules.Check(Father, occupants).Select(v => v.Error.Code).ToList();

    [Fact]
    public void Family_FatherSignsForWifeAndChild_IsValid()
    {
        Codes(
            Stay(Father, null),
            Stay(Person(1988, Gender.Female), OccupantRelationship.Wife),
            Stay(Person(2016, Gender.Male), OccupantRelationship.Child),
            Stay(Person(1958, Gender.Female), OccupantRelationship.Mother)).Should().BeEmpty();
    }

    [Fact]
    public void NonRepresentative_MustDeclareRelationship_AndOtherNeedsNote()
    {
        Codes(Stay(Person(1990, Gender.Male), null)).Should().Equal("RELATIONSHIP_REQUIRED");
        Codes(Stay(Person(1990, Gender.Male), OccupantRelationship.Other)).Should().Equal("RELATIONSHIP_NOTE_REQUIRED");
        Codes(Stay(Person(1990, Gender.Male), OccupantRelationship.Other, note: "Bạn học")).Should().BeEmpty();
    }

    [Theory]
    [InlineData(OccupantRelationship.Wife, Gender.Male)]
    [InlineData(OccupantRelationship.Husband, Gender.Female)]
    [InlineData(OccupantRelationship.DaughterInLaw, Gender.Male)]
    public void GenderedRelationship_MustMatchGender(OccupantRelationship relation, Gender gender)
    {
        Codes(Stay(Person(1990, gender), relation)).Should().Equal("RELATIONSHIP_GENDER_MISMATCH");
        Codes(Stay(Person(1990, Gender.Other), relation)).Should().NotContain("RELATIONSHIP_GENDER_MISMATCH");
    }

    [Theory]
    [InlineData(OccupantRelationship.Child, 1980)]        // con lớn tuổi hơn cha
    [InlineData(OccupantRelationship.Grandchild, 1984)]
    [InlineData(OccupantRelationship.Father, 1990)]       // cha nhỏ tuổi hơn con
    [InlineData(OccupantRelationship.Grandparent, 1986)]
    public void BiologicalRelationship_MustMatchAgeOrder(OccupantRelationship relation, int birthYear)
    {
        Codes(Stay(Person(birthYear, Gender.Male), relation)).Should().Contain("RELATIONSHIP_AGE_MISMATCH");
    }

    [Fact]
    public void Spouse_MustBeOfMarriageAge_OnBothSides()
    {
        Codes(Stay(Person(2009, Gender.Female), OccupantRelationship.Wife, consent: true)).Should().Contain("SPOUSE_UNDER_MARRIAGE_AGE");

        var youngHusband = new PersonFacts(Guid.NewGuid(), new DateOnly(2007, 1, 1), Gender.Male); // 19 tuổi lúc vào ở
        var wife = Person(2000, Gender.Female);
        OccupantRelationshipRules.Check(youngHusband, [Stay(wife, OccupantRelationship.Wife)])
            .Select(v => v.Error.Code).Should().Equal("SPOUSE_UNDER_MARRIAGE_AGE");
    }

    [Fact]
    public void OnlyOneSpouse_AtTheSameTime()
    {
        var first = Person(1987, Gender.Female);
        var second = Person(1990, Gender.Female);

        Codes(Stay(first, OccupantRelationship.Wife), Stay(second, OccupantRelationship.Wife))
            .Should().Equal("MULTIPLE_SPOUSES");
        Codes(Stay(first, OccupantRelationship.Wife, end: MoveIn.AddMonths(3)),
                Stay(second, OccupantRelationship.Wife, moveIn: MoveIn.AddMonths(4)))
            .Should().BeEmpty("không ở cùng thời gian");
    }

    [Fact]
    public void Minor_NeedsGuardianConsent_UnlessRepresentativeIsParentOrGuardian()
    {
        var teen = Person(2010, Gender.Female);

        Codes(Stay(teen, OccupantRelationship.CoTenant)).Should().Equal("GUARDIAN_CONSENT_REQUIRED");
        Codes(Stay(teen, OccupantRelationship.NephewNiece)).Should().Equal("GUARDIAN_CONSENT_REQUIRED");
        Codes(Stay(teen, OccupantRelationship.CoTenant, consent: true)).Should().BeEmpty();
        Codes(Stay(teen, OccupantRelationship.Child)).Should().BeEmpty();
        Codes(Stay(teen, OccupantRelationship.Ward)).Should().BeEmpty();
    }

    [Fact]
    public void ViolationIndex_PointsToTheOffendingOccupant()
    {
        var violations = OccupantRelationshipRules.Check(Father,
            [Stay(Father, null), Stay(Person(1988, Gender.Female), OccupantRelationship.Wife), Stay(Person(1980, Gender.Male), OccupantRelationship.Child)]);

        violations.Should().ContainSingle().Which.Should().Match<OccupantRuleViolation>(v => v.Index == 2 && v.Field == "relationshipType");
    }
}
