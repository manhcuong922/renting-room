# Quy trình sửa nghiệp vụ backend (bắt buộc mỗi session)

Mọi thay đổi nghiệp vụ / sửa issue làm **đúng thứ tự** sau — không code trước rồi mới sửa plan:

1. **Sửa plan** (`docs/plans/0x-*.md`): quy tắc / use case / bảng dữ liệu theo quyết định mới. Phần cũ không còn đúng ⇒ sửa hoặc xóa,
   chỉ giữ 1 dòng "trước dd/MM/yyyy … — đã bỏ" khi cần cho lịch sử. Không để plan mâu thuẫn với code.
2. **Lên kịch bản test** trong mục test của plan (số liệu cụ thể: ngày, số tiền, mã lỗi mong đợi).
3. **Viết test trước** theo kịch bản (domain / application unit test + integration test), chạy thấy **thất bại** (RED).
4. **Code** cho test đạt (GREEN). Migration nếu đổi schema (`dotnet ef migrations add … --project src/renting_room.Infrastructure --startup-project src/renting_room.Api`).
5. **Bổ sung seeder**: mỗi case nghiệp vụ mới ⇒ thêm kịch bản vào `src/renting_room.Infrastructure/Seeding/DemoDataSeeder.*.cs`,
   thêm dòng vào bảng `docs/guides/demo-data.md`, thêm kiểm tra vào `DemoDataSeederTests`. Case cũ bị đổi hành vi ⇒ sửa kịch bản + bảng + test tương ứng.
6. **Cập nhật docs API** (`docs/api/*.md`, mã lỗi trong `errors.md`, enum trong `conventions.md`) và snapshot OpenAPI
   (`UPDATE_OPENAPI_SNAPSHOT=1 dotnet test tests/renting_room.IntegrationTests --filter OpenApiSnapshotTests`).
7. **Chạy toàn bộ test** (`dotnet test renting_room.sln`) + build 0 lỗi trước khi báo xong; báo đúng số test đạt / lỗi.
8. **Cập nhật `docs/plans/98-open-issues.md`**: xong issue ⇒ xóa issue; xong cả nhóm ⇒ xóa nhóm. Chốt "không làm / chấp nhận" ⇒ xóa ở 98,
   ghi 1 dòng vào `99-self-review.md` mục C.

Không commit khi người dùng chưa yêu cầu.
