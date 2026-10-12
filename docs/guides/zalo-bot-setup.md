# Hướng dẫn tạo Zalo Bot và Gmail để gửi phiếu báo tiền

> Dành cho bản **tự dùng**: tạo bot bằng tài khoản Zalo cá nhân, không cần Zalo OA, không cần giấy phép kinh doanh, không cần domain.
> Thiết kế tính năng: [../plans/11-notification.md](../plans/11-notification.md).
> Lệnh viết cho **PowerShell**, chạy ở thư mục gốc của repo.
>
> ⚠️ Phần giao diện và địa chỉ API của Zalo được viết theo tài liệu công khai tại thời điểm 12/10/2026 và **chưa được chạy thử trong dự án này**.
> Nếu màn hình hoặc địa chỉ khác đi, làm theo tài liệu chính thức ở <https://bot.zaloplatforms.com> và sửa lại file này.

## 1. Bạn cần chuẩn bị gì

| Thứ | Bắt buộc? | Ghi chú |
|---|---|---|
| Tài khoản Zalo cá nhân | Có | Dùng luôn Zalo đang dùng. Người thuê chỉ thấy **tên bot**, không thấy tên hay số điện thoại của bạn |
| Bot Token | Có | Lấy ở mục 2. Đây là **mật khẩu của bot** |
| Gmail + App Password | Không | Chỉ cần nếu muốn gửi kèm email (mục 5) |
| Domain / HTTPS / webhook | Không | Hệ thống tự hỏi Zalo bằng polling |
| Zalo OA, ZNS, giấy phép | Không | |

## 2. Tạo bot và lấy token

1. Mở <https://bot.zaloplatforms.com>, đăng nhập bằng Zalo (quét QR bằng app Zalo trên điện thoại).
2. Chọn tạo bot mới. Nếu trang chuyển bạn sang app Zalo (tài khoản **Zalo Bot Manager** → "Tạo bot"), làm tiếp trong app.
3. Đặt tên bot — đây là tên người thuê nhìn thấy, VD `Bot Nha Tro Hoa Mai`. Thêm ảnh đại diện và mô tả ngắn ("Gửi phiếu tiền phòng hàng tháng").
4. Tạo xong, Zalo cấp **Bot Token** dạng `1234567890:abcDEF...` (trên trang, hoặc gửi qua tin nhắn Zalo cho bạn).
5. Lưu lại **link / mã QR của bot** — dùng để in cho người thuê quét.

**Giữ token an toàn**
- Không dán token vào chat, email, ảnh chụp màn hình, file trong repo.
- Lộ token ⇒ người khác nhắn được cho người thuê dưới tên bot của bạn. Khi nghi lộ: vào trang quản lý bot, **tạo lại token**, rồi lưu token mới như mục 3.

## 3. Lưu token vào máy dev

```powershell
dotnet user-secrets set "ZaloBot:Token" "<dán token vào đây>" --project renting_room
```

Kiểm tra đã lưu (lệnh này in token ra màn hình — đừng chụp lại):

```powershell
dotnet user-secrets list --project renting_room
```

Trên máy chủ: đặt biến môi trường `ZaloBot__Token` (hai dấu gạch dưới) thay cho user-secrets.

## 4. Tự kiểm tra bot

Lệnh `Read-Host -AsSecureString` hỏi token mà không ghi vào lịch sử PowerShell.

```powershell
$secure = Read-Host "Bot token" -AsSecureString
$token  = [System.Net.NetworkCredential]::new("", $secure).Password
$api    = "https://bot-api.zaloplatforms.com/bot$token"
```

**4.1 Token có đúng không**

```powershell
Invoke-RestMethod "$api/getMe" -Method Post
```

Đúng ⇒ trả `ok = True` kèm tên bot. Sai token ⇒ lỗi 401.

**4.2 Lấy chat id của chính bạn**

Mở Zalo trên điện thoại, tìm bot vừa tạo (hoặc quét QR của bot), nhắn một tin bất kỳ, VD `xin chao`. Rồi:

```powershell
$updates = Invoke-RestMethod "$api/getUpdates" -Method Post -ContentType "application/json" -Body '{"timeout":10}'
$updates | ConvertTo-Json -Depth 10
```

Trong kết quả, tìm `chat.id` của tin vừa nhắn và gán vào biến:

```powershell
$chatId = "<chat.id vừa thấy>"
```

**4.3 Bot nhắn lại cho bạn**

```powershell
$body = @{ chat_id = $chatId; text = "Thử gửi phiếu: P101 tháng 10/2026 – 3.110.000đ" } | ConvertTo-Json
Invoke-RestMethod "$api/sendMessage" -Method Post -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

Điện thoại nhận được tin ⇒ bot dùng được.

**4.4 Việc cần báo lại** (trả lời câu hỏi mở Q1, Q2 của plan)

| Kiểm tra | Cách làm | Ghi kết quả |
|---|---|---|
| Bot chủ động nhắn sau khi bạn im lặng lâu | **Không nhắn gì cho bot trong 3 ngày**, rồi chạy lại 4.3 | Nhận được / báo lỗi gì |
| Tốc độ gửi | Phần đo sẽ nằm trong spike NT-T00, không cần tự làm | — |
| Trang quản lý bot có nói về giới hạn số tin hay phí không | Đọc trang cài đặt bot | Có / không, nội dung |

Xong thì đóng cửa sổ PowerShell để biến `$token` mất khỏi bộ nhớ.

## 5. Gmail để gửi email (tùy chọn)

1. Vào <https://myaccount.google.com/security>, bật **Xác minh 2 bước**.
2. Vào <https://myaccount.google.com/apppasswords>, tạo App Password tên `renting_room`. Google hiện 16 ký tự — chỉ hiện một lần.
3. Lưu cấu hình:

```powershell
dotnet user-secrets set "Email:Smtp:Host" "smtp.gmail.com" --project renting_room
dotnet user-secrets set "Email:Smtp:Port" "587" --project renting_room
dotnet user-secrets set "Email:Smtp:User" "<địa chỉ gmail>" --project renting_room
dotnet user-secrets set "Email:Smtp:Password" "<app password 16 ký tự>" --project renting_room
dotnet user-secrets set "Email:FromName" "Nhà trọ Hoa Mai" --project renting_room
```

Gmail giới hạn khoảng 500 thư / ngày — đủ cho bản tự dùng. Khi bán cho chủ trọ khác: đổi sang dịch vụ gửi thư riêng (chỉ đổi 4 dòng cấu hình trên, không đổi code).

## 6. Người thuê liên kết với bot thế nào (sau khi tính năng code xong)

1. Bạn mở hồ sơ người thuê trong phần mềm → **Tạo mã liên kết Zalo** → được mã 8 ký tự, hạn 7 ngày.
2. Gửi cho người thuê: QR của bot + mã đó (nhắn tay, hoặc in kèm hợp đồng).
3. Người thuê quét QR, nhắn mã cho bot. Bot trả lời "Đã liên kết phòng P101".
4. Từ đó, mỗi lần bạn chốt phiếu (ở khu đã bật tự gửi), bot tự nhắn phiếu; quá hạn mà còn nợ thì bot nhắc theo số ngày bạn cài.
5. Người thuê nhắn `HUY` cho bot để ngừng nhận tin.

Người thuê chưa liên kết và không có email: phần mềm đưa sẵn nội dung phiếu và link `zalo.me/<sđt>` để bạn bấm gửi tay.

## 7. Sự cố thường gặp

| Hiện tượng | Nguyên nhân hay gặp | Cách xử lý |
|---|---|---|
| `getMe` trả 401 | Token sai, thừa khoảng trắng, hoặc đã tạo lại token | Copy lại token, lưu lại như mục 3 |
| `getUpdates` trả rỗng | Chưa nhắn gì cho bot, hoặc bot đang đặt webhook | Nhắn bot một tin rồi gọi lại; nếu đã từng đặt webhook thì gọi `deleteWebhook` |
| `sendMessage` báo không tìm thấy chat | `chat_id` sai, hoặc người đó chưa từng nhắn bot | Người nhận phải nhắn bot trước ít nhất một lần |
| Tin tiếng Việt bị lỗi dấu | Body không phải UTF-8 | Dùng đúng lệnh ở 4.3 (đổi sang bytes UTF-8) |
| Báo lỗi 429 | Gửi quá nhanh | Chờ theo thời gian Zalo trả về; hệ thống tự làm việc này |
