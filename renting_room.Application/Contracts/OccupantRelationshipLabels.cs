using renting_room.Domain.Contracts;

namespace renting_room.Application.Contracts;

/// <summary>Nhãn tiếng Việt theo cách ghi "quan hệ với chủ hộ" trên tờ khai cư trú (TT 55/2021 Điều 6, sửa bởi TT 66/2023).</summary>
public static class OccupantRelationshipLabels
{
    public static string Label(OccupantRelationship relationship) => relationship switch
    {
        OccupantRelationship.Wife => "Vợ",
        OccupantRelationship.Husband => "Chồng",
        OccupantRelationship.Father => "Cha đẻ",
        OccupantRelationship.Mother => "Mẹ đẻ",
        OccupantRelationship.FatherInLaw => "Cha vợ / cha chồng",
        OccupantRelationship.MotherInLaw => "Mẹ vợ / mẹ chồng",
        OccupantRelationship.AdoptiveFather => "Cha nuôi",
        OccupantRelationship.AdoptiveMother => "Mẹ nuôi",
        OccupantRelationship.Stepfather => "Cha dượng",
        OccupantRelationship.Stepmother => "Mẹ kế",
        OccupantRelationship.Child => "Con đẻ",
        OccupantRelationship.AdoptedChild => "Con nuôi",
        OccupantRelationship.StepChild => "Con riêng của vợ / chồng",
        OccupantRelationship.SonInLaw => "Con rể",
        OccupantRelationship.DaughterInLaw => "Con dâu",
        OccupantRelationship.Grandparent => "Ông / bà",
        OccupantRelationship.GreatGrandparent => "Cụ",
        OccupantRelationship.Grandchild => "Cháu nội / ngoại",
        OccupantRelationship.GreatGrandchild => "Chắt ruột",
        OccupantRelationship.Sibling => "Anh / chị / em ruột",
        OccupantRelationship.HalfSibling => "Anh / chị / em cùng cha khác mẹ hoặc cùng mẹ khác cha",
        OccupantRelationship.SiblingInLaw => "Anh rể / em rể / chị dâu / em dâu",
        OccupantRelationship.NephewNiece => "Cháu ruột",
        OccupantRelationship.UncleAunt => "Bác / chú / cậu / cô / dì ruột",
        OccupantRelationship.Guardian => "Người giám hộ",
        OccupantRelationship.Ward => "Người được giám hộ",
        OccupantRelationship.CoTenant => "Cùng ở thuê",
        _ => "Khác"
    };

    /// <summary>Nhãn + ghi chú (VD "Khác — bạn học", "Anh / chị / em ruột — em gái").</summary>
    public static string? Describe(OccupantRelationship? relationship, string? note) => (relationship, note) switch
    {
        (null, null) => null,
        (null, var text) => text,
        (OccupantRelationship.Other, var text) when text is not null => $"Khác — {text}",
        (var r, null) => Label(r!.Value),
        var (r, text) => $"{Label(r!.Value)} — {text}"
    };
}
