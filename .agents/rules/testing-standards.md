# Quy tắc bắt buộc: Luôn kiểm tra toàn bộ test sau khi hoàn thành công việc

## 1. Nguyên tắc cốt lõi
- Sau mỗi lần chỉnh sửa code, thêm tính năng, sửa bug, hoặc merge nhánh, **BẮT BUỘC** phải chạy kiểm tra toàn bộ hệ thống (build & smoke tests).
- Tuyệt đối không bàn giao kết quả khi chưa chạy kiểm thử và xác nhận 100% test PASS.

## 2. Quy trình kiểm tra chuẩn
Chạy script kiểm tra tự động:
```powershell
powershell -ExecutionPolicy Bypass -File tests/run-all-tests.ps1
```

Script này tự động:
1. `dotnet build src/Bms.Web/Bms.Web.csproj` -> Yêu cầu 0 Warnings, 0 Errors.
2. `dotnet build tests/Bms.Smoke/Bms.Smoke.csproj` -> Yêu cầu 0 Warnings, 0 Errors.
3. Chạy toàn bộ 36 kịch bản kiểm thử tích hợp (Tier, Pricing, Identity, Customer, Revenue, Dashboard, Shift, Payment reconciliation) -> Yêu cầu 100% PASS và rollback an toàn các fixture.

## 3. Báo cáo kết quả
Luôn đính kèm trạng thái test trong câu trả lời cuối cùng để người dùng nắm rõ độ tin cậy của mã nguồn.
