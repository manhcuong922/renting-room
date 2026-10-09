# Nhật ký thao tác

Chủ trọ xem **ai đã làm gì, với đối tượng nào, lúc nào** — gồm việc của chính mình và của phó quản lý.
Hệ thống tự ghi mọi thao tác tạo / sửa / xóa dữ liệu, và các lần xem / in / xuất số giấy tờ đầy đủ.

Quyền: **chỉ chủ trọ** (phó quản lý → 403). Chỉ thấy nhật ký của tổ chức mình.

## Danh sách

`GET /audit-logs?entityType=&entityId=&userId=&action=&from=&to=&page=1&pageSize=20`

| Tham số | Ý nghĩa | Ví dụ |
|---------|---------|-------|
| `entityType` | Loại đối tượng (tên entity) | `Invoice`, `InvoiceLine`, `Contract`, `Renter`, `Payment`, `FeePrice`, `Room` |
| `entityId` | Một đối tượng cụ thể — xem **lịch sử** của nó | id phiếu |
| `userId` | Việc của một người | id phó quản lý |
| `action` | `Created`, `Updated`, `Deleted`, `RevealIdNumber`, `PrintWithIdNumbers`, `ExportWithIdNumbers`, `Anonymized`, `Import` | |
| `from` / `to` | Khoảng ngày (giờ Việt Nam, tính cả 2 đầu), `yyyy-MM-dd` | `2026-11-01` |

→ **200**, mới nhất trước:

```json
{
  "items": [
    {
      "id": "…", "occurredAt": "2026-11-05T02:13:44Z", "userId": "…", "userName": "Phó Quản Lý",
      "action": "Updated", "entityType": "InvoiceLine", "entityId": "…",
      "changes": { "amount": { "old": 3500000, "new": 3000000 }, "isManuallyEdited": { "old": false, "new": true } },
      "ipAddress": "203.0.113.7"
    }
  ],
  "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1
}
```

- `changes`: sửa ⇒ chỉ các trường đổi `{ "field": { "old": …, "new": … } }`; tạo / xóa ⇒ giá trị các trường; sự kiện xem / in / xuất ⇒ chi tiết.
  Số giấy tờ, mật khẩu, token luôn là `"[redacted]"`.
- Lỗi: 400 `INVALID_DATE_RANGE` (`from` > `to`).

## Gợi ý giao diện

| Màn hình | Gọi |
|----------|-----|
| Tab **"Lịch sử"** trong chi tiết phiếu / hợp đồng / người thuê | `entityType` + `entityId` |
| Trang **"Nhật ký"** của chủ trọ, lọc theo người và ngày | `userId`, `from`, `to` |
| Ai đã xem số CCCD của người thuê X | `entityType=Renter&entityId=…&action=RevealIdNumber` |

Dữ liệu có thông tin cá nhân — không lưu cache trình duyệt (`Cache-Control: no-store`).
