# Hướng dẫn dễ hiểu: menu và số lượng khi gọi món

Tài liệu này giải thích giao diện gọi món hiện tại và những phần cần hiểu trước khi tự chỉnh. **Đây là hướng dẫn, không phải thay đổi code.**

## 1. Đồ ăn và đồ uống được chia như thế nào?

Ứng dụng không tự đoán món nào là đồ ăn hay đồ uống. Mỗi món được gắn với một danh mục trong database:

```text
ProductCategories (danh mục)
    1 ───── nhiều
Products (món)
```

Ví dụ:

| Danh mục | Các món |
|---|---|
| Đồ uống | Nước suối, trà đào |
| Đồ ăn | Khoai tây chiên, xúc xích |

Mỗi dòng món trong `Products` có `CategoryId`, trỏ đến `Id` của danh mục trong `ProductCategories`. Muốn món xuất hiện dưới danh mục khác thì phải đổi quan hệ danh mục của món trong database, hoặc làm màn hình Admin để quản lý danh mục/món. Chỉ đổi tên hiển thị ở giao diện thì không đổi cách phân loại dữ liệu.

### Luồng dữ liệu lên màn hình

1. [`OrderController.Index`](../src/Bms.Web/Controllers/OrderController.cs) lấy danh mục từ `_db.ProductCategories` cùng các món liên quan.
2. Controller chuyển dữ liệu thành `CategoryMenuViewModel` và `ProductMenuItemViewModel`.
3. [`Views/Order/Index.cshtml`](../src/Bms.Web/Views/Order/Index.cshtml) lặp qua từng danh mục, rồi lặp qua các món trong danh mục đó để tạo giao diện.
4. Các nút “Tất cả”, “Đồ uống”, “Đồ ăn” chỉ ẩn/hiện các khu vực đã có trên trang. Nút lọc không sửa database và cũng không tự phân loại món.

Để xem danh mục và món trong database, có thể chạy truy vấn chỉ đọc trong SSMS:

```sql
SELECT c.Id AS CategoryId, c.Name AS CategoryName,
       p.Id AS ProductId, p.Name AS ProductName,
       p.StockQuantity, p.IsActive
FROM dbo.ProductCategories AS c
LEFT JOIN dbo.Products AS p ON p.CategoryId = c.Id
ORDER BY c.Name, p.Name;
```

## 2. Vì sao hiện tại không gõ được số lượng?

Trong thẻ món, giao diện có nút `−`, ô số và nút `+`. Ô số hiện được đặt ở chế độ chỉ đọc, nên chỉ có thể thay đổi bằng hai nút. Phần này nằm trong `Views/Order/Index.cshtml`, tại khu vực dựng product card (`qty-controls`).

Có hai nơi hiển thị số lượng, cần phân biệt:

1. **Số lượng trên thẻ món:** số lượng bạn đang chọn trước khi bấm “Thêm”.
2. **Số lượng trong giỏ:** số lượng hiện đã thêm vào giỏ và sẽ được gửi khi bấm “Gửi đơn”.

Khi bấm “Thêm”, số lượng trên thẻ được cộng vào số lượng món đó trong giỏ. Vì vậy nếu một món đã có trong giỏ rồi, bấm “Thêm” lần nữa sẽ tăng số lượng trong giỏ, không phải mở lại đơn hàng trước.

## 3. Chỉ sửa số lượng trên thẻ món trước khi bấm “Thêm”

Phạm vi bạn muốn sửa là ô số nằm **giữa nút `−` và nút `+` trên từng thẻ món**: click vào ô đang hiện `1` để gõ số lượng muốn mua, hoặc bấm hai nút để tăng/giảm số; sau đó bấm “Thêm” để đưa số lượng đó vào giỏ.

Không cần sửa số lượng trong giỏ cho yêu cầu này. Cụm điều khiển trên thẻ món nằm tại [`Views/Order/Index.cshtml`, dòng 292](../src/Bms.Web/Views/Order/Index.cshtml#L292). Ô số ở dòng 294 đang bị đặt `readonly`, vì vậy hiện chỉ có thể đổi bằng nút `+`/`−`.

### Hành vi mong muốn khi nhập số vượt giới hạn

Giới hạn nghiệp vụ hiện tại là **1–20 món cho mỗi sản phẩm**; số lượng cũng không được vượt quá tồn kho. Ô nhập vẫn cho người dùng gõ số lớn như `100`. Khi bấm **“Thêm”**, giao diện kiểm tra số đã nhập:

- Nếu số lượng hợp lệ (từ 1 đến mức nhỏ hơn giữa 20 và tồn kho), thêm đúng số đó vào giỏ.
- Nếu nhập `100` hoặc vượt tồn kho, **không thêm món vào giỏ** và hiện thông báo rõ ràng, ví dụ: “Số lượng vượt quá số lượng cho phép.” Có thể nêu giới hạn cụ thể để người dùng biết cần nhập tối đa bao nhiêu.
- Không tự âm thầm giảm `100` xuống `20`; cần báo lỗi để người dùng chủ động sửa.

Như vậy bạn có thể nhập nhanh số lượng lớn, nhưng nếu mặt hàng hiện chỉ được đặt tối đa 20 thì ứng dụng sẽ báo vượt giới hạn khi nhấn “Thêm”. Muốn thực sự cho phép đặt 100, phải thay đổi giới hạn nghiệp vụ đã thống nhất trong Spec và đồng bộ server, giao diện và quy tắc tồn kho.

Nút `+`/`−` vẫn cập nhật chính ô nhập. JavaScript xử lý các nút và “Thêm” nằm trong `Views/Order/Index.cshtml`; server vẫn phải kiểm tra lại số lượng khi nhận đơn để tránh người dùng bỏ qua kiểm tra trên trình duyệt.

### Số lượng trong giỏ không thuộc phạm vi thay đổi này

Trong giỏ, nút cộng/trừ gọi `Cart.updateQty(...)`. Giao diện giỏ được dựng lại bởi `Cart.render()`. Hàm này cũng tạo các input ẩn có tên `Items[index].ProductId` và `Items[index].Quantity`; các input đó mới là dữ liệu được gửi đến server.

Yêu cầu hiện tại **không cần đổi** phần giỏ để gõ số trực tiếp. Số lượng trên thẻ là số chuẩn bị thêm; số lượng trong giỏ là số đã thêm. Bấm “Thêm” lần nữa vẫn cộng số mới vào lượng đang có trong giỏ.

## 4. Các giới hạn phải giữ nhất quán

Quy tắc hiện tại là mỗi món đặt từ **1 đến 20**, đồng thời không được vượt quá lượng còn trong kho:

- Quy tắc 1–20 ở `Services/OrderConstants.cs`.
- Khi dựng thẻ món, giới hạn tối đa được tính theo mức nhỏ hơn giữa 20 và tồn kho.
- Khi gửi form, `OrderController.Submit` kiểm tra lại số lượng, trạng thái bán và tồn kho ở phía server.

Kiểm tra trên trình duyệt giúp giao diện dễ sử dụng, nhưng không thay thế kiểm tra ở server. Người dùng có thể tự sửa dữ liệu trình duyệt gửi đi.

Hành vi đã thống nhất cho yêu cầu hiện tại: số vượt giới hạn được gõ vào ô nhưng khi bấm “Thêm” thì báo lỗi và không thêm; không tự giảm số. Các trường hợp còn lại cần có cách xử lý rõ ràng:

| Trường hợp | Cần quyết định |
|---|---|
| Để ô số trống khi đang gõ | Cho phép tạm thời hay khôi phục số cũ khi rời ô? |
| Nhập chữ hoặc số thập phân | Báo lỗi hay chỉ nhận số nguyên? |
| Nhập 0 hoặc số âm | Hiện lỗi, tự sửa thành 1, hay bỏ món khỏi giỏ? |
| Nhập lớn hơn 20 hoặc tồn kho | Đã thống nhất: báo vượt giới hạn khi bấm “Thêm”, không tự giảm và không thêm vào giỏ |
| Bấm “Thêm” món đã có trong giỏ | Cộng thêm số mới vào giỏ hay thay thế số cũ? |
| Giảm số lượng giỏ xuống 0 | Xóa món khỏi giỏ hay giữ tối thiểu 1? |

## 5. Các bước lần theo code theo dòng

Mở lần lượt các vị trí này để thấy dữ liệu đi từ SQL lên giao diện, rồi từ giao diện trở lại server. Liên kết sẽ mở file tại dòng bắt đầu phần liên quan. Số dòng có thể thay đổi nếu code được chỉnh sửa.

### A. Theo dấu cách chia đồ ăn/đồ uống

1. **Xem dữ liệu mẫu tạo danh mục:** mở [`03_DemoData.sql`, dòng 62](../BMS_Database_Starter/03_DemoData.sql#L62) để thấy các tên “Đồ uống” và “Đồ ăn”; dòng 65 thêm món và gắn mỗi món với biến danh mục tương ứng.
2. **Xem cấu trúc bảng:** mở [`01_CreateDatabase.sql`, dòng 197](../BMS_Database_Starter/01_CreateDatabase.sql#L197) cho `ProductCategories`, và [dòng 202](../BMS_Database_Starter/01_CreateDatabase.sql#L202) cho `Products`. Đọc cột `CategoryId` và khóa ngoại để hiểu liên kết giữa món với danh mục.
3. **Xem entity C#:** mở [`BmsEntities.cs`, dòng 89](../src/Bms.Web/Data/BmsEntities.cs#L89) cho lớp `ProductCategory`; tiếp đến [dòng 104](../src/Bms.Web/Data/BmsEntities.cs#L104) cho lớp `Product`, đặc biệt là property `CategoryId`.
4. **Xem quan hệ EF Core:** mở [`ApplicationDbContext.cs`, dòng 16](../src/Bms.Web/Data/ApplicationDbContext.cs#L16) để thấy DbSet danh mục/món, rồi [dòng 60](../src/Bms.Web/Data/ApplicationDbContext.cs#L60) để xem cấu hình quan hệ Category–Products.
5. **Xem Controller truy vấn:** mở [`OrderController.cs`, dòng 36](../src/Bms.Web/Controllers/OrderController.cs#L36) cho action `Index`; đến [dòng 58](../src/Bms.Web/Controllers/OrderController.cs#L58) để xem truy vấn danh mục và món. Tại đây dữ liệu được gom thành ViewModel trước khi gửi sang trang.
6. **Xem View dựng các khu vực:** mở [`Views/Order/Index.cshtml`, dòng 248](../src/Bms.Web/Views/Order/Index.cshtml#L248) cho nút lọc danh mục; [dòng 257](../src/Bms.Web/Views/Order/Index.cshtml#L257) bắt đầu vòng lặp dựng section cho mỗi danh mục; [dòng 264](../src/Bms.Web/Views/Order/Index.cshtml#L264) bắt đầu dựng từng món.
7. **Xem JavaScript lọc:** mở cùng file tại [dòng 538](../src/Bms.Web/Views/Order/Index.cshtml#L538). Đoạn này chỉ ẩn/hiện section theo danh mục được chọn; nó không thay đổi CategoryId trong database.

### B. Theo dấu số lượng từ nút bấm tới database

1. **Ô số và nút trên card món:** mở [`Views/Order/Index.cshtml`, dòng 292](../src/Bms.Web/Views/Order/Index.cshtml#L292). Nút trừ/cộng nằm cạnh ô số; ô hiện là `readonly`, giải thích vì sao chưa gõ được.
2. **JavaScript của nút cộng/trừ:** mở cùng file tại [dòng 555](../src/Bms.Web/Views/Order/Index.cshtml#L555) cho `+`, rồi [dòng 568](../src/Bms.Web/Views/Order/Index.cshtml#L568) cho `−`. Các handler đọc và cập nhật ô số của card.
3. **Nút Thêm vào giỏ:** mở tại [dòng 579](../src/Bms.Web/Views/Order/Index.cshtml#L579). Handler đọc số lượng trên card rồi gọi `Cart.add(...)`.
4. **Trạng thái giỏ và quy tắc cộng dồn:** mở tại [dòng 423](../src/Bms.Web/Views/Order/Index.cshtml#L423) cho đối tượng `Cart`; [dòng 426](../src/Bms.Web/Views/Order/Index.cshtml#L426) xử lý thêm món; [dòng 444](../src/Bms.Web/Views/Order/Index.cshtml#L444) cập nhật số lượng trong giỏ.
5. **Giao diện giỏ và payload gửi đi:** mở [`Cart.render`, dòng 473](../src/Bms.Web/Views/Order/Index.cshtml#L473). Đoạn giao diện cộng/trừ của giỏ nằm ở [dòng 510](../src/Bms.Web/Views/Order/Index.cshtml#L510); input ẩn chứa `ProductId` và `Quantity` được tạo từ [dòng 526](../src/Bms.Web/Views/Order/Index.cshtml#L526). Đây là số lượng sẽ được submit.
6. **Giới hạn server:** mở [`OrderConstants.cs`, dòng 9](../src/Bms.Web/Services/OrderConstants.cs#L9) để xem giới hạn 1–20, và [dòng 43](../src/Bms.Web/Services/OrderConstants.cs#L43) cho hàm kiểm tra.
7. **Server nhận form và xác minh:** mở [`OrderController.Submit`, dòng 116](../src/Bms.Web/Controllers/OrderController.cs#L116); bước xác nhận số lượng nằm gần [dòng 153](../src/Bms.Web/Controllers/OrderController.cs#L153), còn kiểm tra món đủ tồn kho gần [dòng 191](../src/Bms.Web/Controllers/OrderController.cs#L191).
8. **Lưu đơn và trừ kho:** trong cùng action, transaction bắt đầu ở [dòng 182](../src/Bms.Web/Controllers/OrderController.cs#L182), dòng chi tiết và tồn kho được cập nhật gần [dòng 215](../src/Bms.Web/Controllers/OrderController.cs#L215), rồi lưu và commit gần [dòng 231](../src/Bms.Web/Controllers/OrderController.cs#L231).

### C. Các vị trí cần tự đọc để sửa đúng phần ô số trên thẻ món

Khi bắt đầu thực hiện thay đổi sau này, lần lượt xem và hiểu các vị trí sau — không nên chỉ sửa ô HTML:

1. Ô trên thẻ món tại [`Views/Order/Index.cshtml`, dòng 294](../src/Bms.Web/Views/Order/Index.cshtml#L294): đây là chỗ đang khóa nhập bằng bàn phím.
2. Handler cộng/trừ ở cùng file, [dòng 555](../src/Bms.Web/Views/Order/Index.cshtml#L555) và [dòng 568](../src/Bms.Web/Views/Order/Index.cshtml#L568): cần tiếp tục hoạt động khi ô được phép gõ.
3. Handler “Thêm” tại [dòng 579](../src/Bms.Web/Views/Order/Index.cshtml#L579): nó lấy giá trị ô để đưa vào giỏ.
4. `Cart.add` và `Cart.updateQty` tại [dòng 426](../src/Bms.Web/Views/Order/Index.cshtml#L426) và [dòng 444](../src/Bms.Web/Views/Order/Index.cshtml#L444): quyết định số lượng được cộng thêm hay thay thế.
5. `Cart.add` tại [dòng 426](../src/Bms.Web/Views/Order/Index.cshtml#L426): hiểu cách số lượng nhập trên thẻ được cộng vào món đã có trong giỏ.
6. `OrderController.Submit` tại [dòng 116](../src/Bms.Web/Controllers/OrderController.cs#L116): giữ kiểm tra giới hạn và tồn kho ở server vì kiểm tra giao diện có thể bị bỏ qua.

Khi sửa ô nhập trên thẻ, hãy quyết định ô trống hoặc số không hợp lệ sẽ được báo lỗi hay khôi phục về giá trị hợp lệ. Không cần biến ô số lượng trong giỏ thành ô nhập nếu bạn chỉ muốn nhập nhanh số lượng trước khi bấm “Thêm”.

## 6. Bảng tra cứu: muốn tự điều khiển phần nào thì mở code ở đâu?

Các liên kết dưới đây mở **file trong project tại dòng cần xem/sửa**. Hãy mở đoạn đó trong VS Code và tự chỉnh theo giao diện/quy tắc bạn muốn; tài liệu này không đưa sẵn code thay thế.

| Bạn muốn tự thay đổi | File và dòng code cần mở | Đây là phần điều khiển |
|---|---|---|
| Đổi kiểu dáng nút lọc danh mục | [`Views/Order/Index.cshtml`, dòng 25](../src/Bms.Web/Views/Order/Index.cshtml#L25) | CSS của `.category-pills` và `.category-pill-btn` |
| Đổi danh mục/màu/tiêu đề hiển thị trên menu | [`Views/Order/Index.cshtml`, dòng 248](../src/Bms.Web/Views/Order/Index.cshtml#L248) | HTML/Razor tạo nút “Tất cả” và nút theo từng danh mục |
| Đổi cách hiển thị khu vực danh mục và món | [`Views/Order/Index.cshtml`, dòng 257](../src/Bms.Web/Views/Order/Index.cshtml#L257) | Vòng lặp danh mục và vòng lặp món bên trong |
| Đổi logic bấm nút danh mục để lọc | [`Views/Order/Index.cshtml`, dòng 538](../src/Bms.Web/Views/Order/Index.cshtml#L538) | JavaScript ẩn/hiện các section theo danh mục |
| Đổi kích thước/vẻ ngoài nút `+`, `−`, ô số | [`Views/Order/Index.cshtml`, dòng 110](../src/Bms.Web/Views/Order/Index.cshtml#L110) | CSS của `.qty-controls`, `.qty-btn`, `.qty-input` |
| Cho phép gõ số tại thẻ món | [`Views/Order/Index.cshtml`, dòng 292](../src/Bms.Web/Views/Order/Index.cshtml#L292) | HTML của nút trừ, ô số hiện `readonly`, nút cộng |
| Thay đổi cách nút `+` trên thẻ món hoạt động | [`Views/Order/Index.cshtml`, dòng 555](../src/Bms.Web/Views/Order/Index.cshtml#L555) | JavaScript cập nhật số trong ô thẻ món |
| Thay đổi cách nút `−` trên thẻ món hoạt động | [`Views/Order/Index.cshtml`, dòng 568](../src/Bms.Web/Views/Order/Index.cshtml#L568) | JavaScript giảm số trong ô thẻ món |
| Đổi cách bấm “Thêm” đưa món vào giỏ | [`Views/Order/Index.cshtml`, dòng 579](../src/Bms.Web/Views/Order/Index.cshtml#L579) | Đọc số trên thẻ món rồi gọi `Cart.add` |
| Đổi việc thêm món trùng vào giỏ là cộng hay thay thế | [`Views/Order/Index.cshtml`, dòng 426](../src/Bms.Web/Views/Order/Index.cshtml#L426) | `Cart.add` quyết định số lượng mới |
| Cho phép gõ số lượng trực tiếp trong giỏ | [`Views/Order/Index.cshtml`, dòng 473](../src/Bms.Web/Views/Order/Index.cshtml#L473), rồi xem [dòng 510](../src/Bms.Web/Views/Order/Index.cshtml#L510) | `Cart.render` tạo giao diện giỏ; hiện số lượng là text kèm nút `+`/`−` |
| Đổi giới hạn đặt 1–20 món | [`Services/OrderConstants.cs`, dòng 9](../src/Bms.Web/Services/OrderConstants.cs#L9) và [dòng 43](../src/Bms.Web/Services/OrderConstants.cs#L43) | Hằng số và kiểm tra số lượng ở server |
| Đổi mức tối đa trên từng card theo tồn kho | [`Views/Order/Index.cshtml`, dòng 295](../src/Bms.Web/Views/Order/Index.cshtml#L295) | View đặt giới hạn nút theo số nhỏ hơn giữa 20 và tồn kho |
| Đổi kiểm tra tồn kho khi gửi đơn | [`Controllers/OrderController.cs`, dòng 191](../src/Bms.Web/Controllers/OrderController.cs#L191) | Server xác nhận món còn bán và đủ hàng |
| Đổi nơi lấy danh sách danh mục/món từ database | [`Controllers/OrderController.cs`, dòng 58](../src/Bms.Web/Controllers/OrderController.cs#L58) | Truy vấn EF Core, chuyển kết quả thành dữ liệu cho View |
| Đổi nội dung danh mục hoặc món mẫu trong SQL | [`BMS_Database_Starter/03_DemoData.sql`, dòng 62](../BMS_Database_Starter/03_DemoData.sql#L62) và [dòng 65](../BMS_Database_Starter/03_DemoData.sql#L65) | Dữ liệu seed cho database khởi tạo mới; sửa file này không tự cập nhật database đã có |

**Cách dùng:** bấm liên kết để mở file, hoặc mở file trong VS Code rồi nhấn **Ctrl+G** và nhập số dòng. Khi đổi giới hạn hoặc hành vi số lượng, xem cả giao diện JavaScript lẫn kiểm tra server để chúng không áp dụng quy tắc khác nhau.

## 7. Nên mở file nào khi tìm hiểu hoặc tự sửa?

| Muốn tìm hiểu | File/vị trí |
|---|---|
| Entity danh mục và món, `CategoryId`, `StockQuantity` | [`Data/BmsEntities.cs`](../src/Bms.Web/Data/BmsEntities.cs) |
| EF Core khai báo các bảng | [`Data/ApplicationDbContext.cs`](../src/Bms.Web/Data/ApplicationDbContext.cs) |
| Tải danh mục và món lên trang | [`Controllers/OrderController.cs`](../src/Bms.Web/Controllers/OrderController.cs), action `Index` |
| Tạo nút danh mục và thẻ món | [`Views/Order/Index.cshtml`](../src/Bms.Web/Views/Order/Index.cshtml), phần menu |
| Nút cộng/trừ, thêm vào giỏ, dựng giỏ | Cùng file view, phần Scripts: `Cart`, `Cart.render`, `btn-item-plus`, `btn-item-minus` |
| Tên/số lượng món gửi lên server | `Cart.render`, các input ẩn `Items[index]...` |
| Kiểm tra cuối cùng trước khi lưu đơn | [`Controllers/OrderController.cs`](../src/Bms.Web/Controllers/OrderController.cs), action `Submit` |
| Giới hạn số lượng và quy tắc còn hàng | [`Services/OrderConstants.cs`](../src/Bms.Web/Services/OrderConstants.cs) |

## 8. Tóm tắt

- **Danh mục** nằm trong `ProductCategories`; món thuộc danh mục qua `Products.CategoryId`.
- **Nút danh mục** chỉ lọc cách hiển thị; muốn phân loại khác cần thay quan hệ dữ liệu hoặc xây chức năng quản lý món.
- **Thẻ món và giỏ hàng là hai trạng thái số lượng riêng.**
- Muốn gõ số trực tiếp, cần đồng bộ ô nhập, nút cộng/trừ, trạng thái giỏ, input gửi form và kiểm tra server.
- Hiện chưa có màn hình Admin quản lý món/tồn kho; sửa dữ liệu danh mục/tồn kho có thể cần thao tác database local hoặc bổ sung chức năng Admin.
