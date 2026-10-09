namespace renting_room.Application.Common.Interfaces;

/// <param name="RowNumber">Số dòng trong Excel (bắt đầu 1) — để báo lỗi đúng dòng người dùng nhìn thấy.</param>
/// <param name="Cells">Giá trị dạng chữ đã cắt khoảng trắng (ngày ⇒ "yyyy-MM-dd", số ⇒ dạng invariant); ô trống / ô công thức ⇒ null.</param>
/// <param name="HasFormula">Dòng có ô chứa công thức — bị từ chối khi import (RP-BR-02).</param>
public sealed record SheetRow(int RowNumber, IReadOnlyList<string?> Cells, bool HasFormula);

public sealed record SheetData(string Name, IReadOnlyList<SheetRow> Rows);

/// <summary>
/// Đọc file Excel tải lên (PR-UC-11, RT-UC-10). File hỏng / không phải xlsx ⇒ <see cref="InvalidDataException"/>; giải nén quá lớn
/// (zip bomb) ⇒ <see cref="SpreadsheetTooLargeException"/> — kiểm <b>trước</b> khi mở bằng thư viện Excel.
/// </summary>
public interface ISpreadsheetReader
{
    /// <summary>Tổng dung lượng sau giải nén tối đa của một file xlsx.</summary>
    const long MaxUncompressedBytes = 20 * 1024 * 1024;

    IReadOnlyList<SheetData> Read(byte[] content);
}

public sealed class SpreadsheetTooLargeException(string message) : Exception(message);
