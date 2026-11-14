namespace renting_room.Application.Common.Interfaces;

/// <param name="RowNumber">Số dòng trong Excel (bắt đầu 1) — để báo lỗi đúng dòng người dùng nhìn thấy.</param>
/// <param name="Cells">Giá trị dạng chữ đã cắt khoảng trắng (ngày ⇒ "yyyy-MM-dd", số ⇒ dạng invariant); ô trống / ô công thức ⇒ null.</param>
/// <param name="HasFormula">Dòng có ô chứa công thức — bị từ chối khi import (RP-BR-02).</param>
public sealed record SheetRow(int RowNumber, IReadOnlyList<string?> Cells, bool HasFormula);

/// <param name="Truncated">Sheet còn dòng có dữ liệu sau <c>maxRows</c> dòng đã đọc — không đọc tiếp (H-05).</param>
public sealed record SheetData(string Name, IReadOnlyList<SheetRow> Rows, bool Truncated = false);

/// <summary>
/// Đọc file Excel tải lên (PR-UC-11, RT-UC-10). File hỏng / không phải xlsx ⇒ <see cref="InvalidDataException"/>; giải nén quá lớn
/// (zip bomb) ⇒ <see cref="SpreadsheetTooLargeException"/> — kiểm <b>trước</b> khi mở bằng thư viện Excel.
/// H-05 (06/11/2026): bộ nhớ dùng có trần — chỉ đọc ô có dữ liệu trong <see cref="MaxColumns"/> cột đầu, <see cref="MaxSheets"/> sheet đầu,
/// mỗi sheet tối đa <c>maxRows</c> dòng (dòng chỉ có dữ liệu ngoài <see cref="MaxColumns"/> cột bị bỏ, không tính).
/// </summary>
public interface ISpreadsheetReader
{
    /// <summary>File 500 dòng thật chỉ vài trăm KB; thư viện Excel nạp cả file vào RAM nên trần này cũng là trần bộ nhớ của nó (trước 06/11/2026: 20 MB).</summary>
    const long MaxUncompressedBytes = 5 * 1024 * 1024;

    /// <summary>Mẫu import ≤ 20 cột — dư chỗ cho người dùng chèn / đổi thứ tự cột.</summary>
    const int MaxColumns = 50;

    const int MaxSheets = 10;

    IReadOnlyList<SheetData> Read(byte[] content, int maxRows);
}

public sealed class SpreadsheetTooLargeException(string message) : Exception(message);
