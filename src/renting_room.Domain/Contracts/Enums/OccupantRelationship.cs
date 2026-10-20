using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Domain.Contracts;

public enum OccupantRelationship
{
    Wife,
    Husband,
    Father,
    Mother,
    FatherInLaw,      // cha vợ / cha chồng
    MotherInLaw,      // mẹ vợ / mẹ chồng
    AdoptiveFather,
    AdoptiveMother,
    Stepfather,       // cha dượng
    Stepmother,       // mẹ kế
    Child,            // con đẻ
    AdoptedChild,     // con nuôi
    StepChild,        // con riêng của vợ / chồng
    SonInLaw,         // con rể
    DaughterInLaw,    // con dâu
    Grandparent,      // ông / bà nội, ngoại
    GreatGrandparent, // cụ nội / ngoại
    Grandchild,       // cháu nội / ngoại
    GreatGrandchild,  // chắt ruột
    Sibling,          // anh / chị / em ruột
    HalfSibling,      // cùng cha khác mẹ / cùng mẹ khác cha
    SiblingInLaw,     // anh rể, em rể, chị dâu, em dâu
    NephewNiece,      // cháu ruột (con của anh chị em)
    UncleAunt,        // bác / chú / cậu / cô / dì ruột
    Guardian,         // người giám hộ
    Ward,             // người được giám hộ
    CoTenant,         // cùng ở thuê (bạn bè, đồng nghiệp…)
    Other             // khác — bắt buộc ghi rõ
}
