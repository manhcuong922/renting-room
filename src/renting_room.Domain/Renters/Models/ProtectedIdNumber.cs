using renting_room.Domain.Common;

namespace renting_room.Domain.Renters;

/// <summary>Số giấy tờ đã chuẩn hóa → mã hóa (để đọc lại) + hash (để tìm/chống trùng) + 4 số cuối (để hiển thị).</summary>
public sealed record ProtectedIdNumber(IdDocumentType Type, byte[] Encrypted, string Hash, string Last4);
