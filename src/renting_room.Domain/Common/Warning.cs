namespace renting_room.Domain.Common;

/// <summary>Cảnh báo mềm — thao tác vẫn thành công, UI hiển thị để người dùng kiểm tra lại.</summary>
public sealed record Warning(string Code, string Message);
