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
