# Mxx — <Tên module>

> Khuôn chuẩn cho mọi plan module. Giữ nguyên thứ tự mục; mục nào không áp dụng ghi "Không áp dụng" kèm lý do.
> Quy ước dùng chung (C-xx) và quy tắc pháp lý (LEG-xx) **tham chiếu**, không định nghĩa lại.
> Plan mẫu đầy đủ nhất: [01-identity-access.md](01-identity-access.md).

## 1. Mục tiêu & phạm vi
- **Mục tiêu**: 1–2 câu, module chịu trách nhiệm gì.
- **Trong phạm vi** / **Ngoài phạm vi**: gạch đầu dòng.
- **Phase**: P0/P1/P2/P3.

## 2. Thuật ngữ
| Thuật ngữ (VI) | Tên trong code | Định nghĩa |

## 3. Nghiệp vụ
### 3.1 Use case (góc nhìn người dùng)
| ID | Actor | Mô tả | Kết quả |
### 3.2 Quy tắc nghiệp vụ
| Mã (`<MOD>-BR-xx`) | Quy tắc | Nơi kiểm tra (Domain / Validator / DB constraint) |
### 3.3 Vòng đời trạng thái
Sơ đồ mermaid `stateDiagram-v2` + bảng chuyển trạng thái (từ → đến, lệnh, điều kiện, tác động phụ).

## 4. Dữ liệu
### 4.1 Entity / bảng
Mỗi bảng: cột | kiểu PostgreSQL | null | mặc định | ràng buộc | ghi chú.
### 4.2 Ràng buộc & index
CHECK, UNIQUE (partial), FK composite (C-01), EXCLUDE, index phục vụ truy vấn chính.
### 4.3 Dữ liệu dẫn xuất
Giá trị tính toán — không lưu, hoặc lưu cache + quy tắc cập nhật trong cùng transaction.

## 5. Domain model
Aggregate root, method nghiệp vụ, invariant được bảo vệ trong entity.

## 6. Application — Commands / Queries
| Use case | Loại | Input | Output | Lỗi nghiệp vụ |

## 7. API
| Method | Route | Quyền | Request | Response | Mã lỗi |
Kèm ví dụ JSON cho endpoint chính.

## 8. Validation
| Field | Quy tắc | Mã lỗi |
Phân biệt: validation định dạng (FluentValidation, 400) vs quy tắc nghiệp vụ (Domain, 409/422).

## 9. Phân quyền
| Hành động | SystemAdmin | OrgOwner | OrgManager (P3) |

## 10. Toàn vẹn dữ liệu & concurrency
Các kịch bản race condition / bất đồng bộ và cách chặn (transaction, lock, unique index, xmin).

## 11. Audit & bảo mật
Entity nào audit, dữ liệu nhạy cảm, rate limit.

## 12. Kế hoạch test
- Unit (Domain), Integration (API + Testcontainers), E2E (luồng chính).
- Danh sách test case bắt buộc (gồm test cô lập tổ chức C-01).

## 13. Phụ thuộc
Module phụ thuộc / bị phụ thuộc; sự kiện/tác động chéo.

## 14. Task breakdown
| ID | Task | Lớp | Ước lượng | Phụ thuộc |

## 15. Câu hỏi mở
Quyết định chưa chốt + đề xuất mặc định.

## 16. Checklist review
- [ ] Mọi bảng có `organization_id` + FK composite
- [ ] Không có dữ liệu tài chính bị UPDATE/DELETE sau khi chốt
- [ ] Mọi quy tắc BR có nơi kiểm tra cụ thể và test
- [ ] Mọi lệnh tài chính có idempotency + transaction
- [ ] Số tiền dùng `decimal`, làm tròn đúng C-03
- [ ] Ngày nghiệp vụ dùng `TimeProvider` VN (C-04)
- [ ] Mã lỗi đã liệt kê đầy đủ ở mục 7–8
