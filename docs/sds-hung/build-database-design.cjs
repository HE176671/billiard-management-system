// Read-only SQL parsing; writes documentation only. Never connects to SQL Server.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const sqlPath = path.join(root, 'BMS_Database_Starter/BilliardDB_Full.sql');
const sql = fs.readFileSync(sqlPath, 'utf8');
function closeParen(text, start) {
  let depth = 0, quoted = false;
  for (let i = start; i < text.length; i++) {
    if (text[i] === "'") {
      if (quoted && text[i + 1] === "'") { i++; continue; }
      quoted = !quoted;
    }
    if (quoted) continue;
    if (text[i] === '(') depth++;
    if (text[i] === ')' && --depth === 0) return i;
  }
  throw Error('Unbalanced SQL parentheses');
}
function splitColumns(body) {
  const parts = []; let start = 0, depth = 0, quoted = false;
  for (let i = 0; i < body.length; i++) {
    if (body[i] === "'") {
      if (quoted && body[i + 1] === "'") { i++; continue; }
      quoted = !quoted;
    }
    if (quoted) continue;
    if (body[i] === '(') depth++;
    if (body[i] === ')') depth--;
    if (body[i] === ',' && depth === 0) { parts.push(body.slice(start, i).trim()); start = i + 1; }
  }
  parts.push(body.slice(start).trim());
  return parts;
}
const tables = new Map();
for (const match of sql.matchAll(/CREATE TABLE dbo\.(\w+)\s*\(/g)) {
  const start = match.index + match[0].length - 1;
  const items = splitColumns(sql.slice(start + 1, closeParen(sql, start)));
  const columns = items.filter(s => !s.startsWith('CONSTRAINT ')).map(s => {
    const m = /^(\w+)\s+([\s\S]+)$/.exec(s);
    if (!m) throw Error('Cannot parse column: ' + s);
    return { name: m[1], definition: m[2].replace(/\s+/g, ' ').trim() };
  });
  const constraints = items.filter(s => s.startsWith('CONSTRAINT ')).map(s => s.replace(/\s+/g, ' ').trim());
  const pk = items.find(s => /PRIMARY KEY/.test(s));
  const pkCols = /^CONSTRAINT/.test(pk) ? /PRIMARY KEY\s*\(([^)]+)\)/.exec(pk)[1].split(',').map(s => s.trim()) : [pk.split(/\s+/)[0]];
  const fks = constraints.filter(s => /FOREIGN KEY/.test(s)).map(s => {
    const m = /CONSTRAINT (\w+) FOREIGN KEY \((\w+)\) REFERENCES dbo\.(\w+)\((\w+)\)/.exec(s);
    if (!m) throw Error('Cannot parse FK: ' + s);
    return { name: m[1], column: m[2], parent: m[3], parentColumn: m[4], cascade: /ON DELETE CASCADE/.test(s) };
  });
  const checks = constraints.filter(s => / CHECK /.test(s)).map(s => {
    const m = /CONSTRAINT (\w+) CHECK \(([\s\S]+)\)$/.exec(s);
    if (!m) throw Error('Cannot parse CHECK: ' + s);
    return { name: m[1], expression: m[2], columns: columns.filter(c => new RegExp('\\b' + c.name + '\\b').test(m[2])).map(c => c.name) };
  });
  const indexes = [...sql.matchAll(new RegExp('CREATE (UNIQUE )?INDEX (\\w+) ON dbo\\.' + match[1] + '\\(([^)]+)\\)([^;]*);', 'g'))].map(m => ({ unique: !!m[1], name: m[2], columns: m[3].split(',').map(s => s.trim()), suffix: m[4].trim().replace(/\s+/g, ' ') }));
  tables.set(match[1], { name: match[1], columns, pkCols, fks, checks, indexes });
}
const descriptions = {
  AspNetUsers: 'Nguồn tài khoản chung cho Admin, Staff, Customer; thông tin nhân viên, bảo mật và hạng thành viên.',
  AspNetRoles: 'Danh mục vai trò và quyền ở mức role: Admin, Staff, Customer.',
  AspNetUserRoles: 'Bảng nối nhiều–nhiều giữa tài khoản và vai trò.',
  AspNetUserClaims: 'Các claim bổ sung gắn với từng tài khoản theo cơ chế Identity.',
  AspNetRoleClaims: 'Các claim được cấp thông qua vai trò theo cơ chế Identity.',
  AspNetUserLogins: 'Liên kết tài khoản BMS với định danh của nhà cung cấp đăng nhập ngoài.',
  AspNetUserTokens: 'Lưu token theo tài khoản, nhà cung cấp và tên token; không phải bảng phiên chơi.',
  MembershipTiers: 'Danh mục hạng thành viên và phần trăm ưu đãi.',
  TableTypes: 'Danh mục loại bàn và đơn giá giờ cơ sở.',
  BilliardTables: 'Thông tin bàn bida vật lý, vị trí và trạng thái hiện tại.',
  PricingConfigs: 'Cấu hình đơn giá theo khoảng giờ cho từng loại bàn.',
  Bookings: 'Lượt đặt bàn, khách đặt, khoảng thời gian, check-in và hủy đặt.',
  PlaySessions: 'Phiên chơi thực tế, người mở/đóng và giá giờ được ghi nhận lúc mở.',
  ProductCategories: 'Danh mục nhóm đồ ăn và nước uống.',
  Products: 'Món đang bán, giá, tồn kho và trạng thái kinh doanh.',
  Orders: 'Lượt gọi món F&B gắn với phiên chơi.',
  OrderDetails: 'Chi tiết món, số lượng và giá tại thời điểm gọi trong một đơn F&B.',
  Combos: 'Danh mục gói gồm giờ chơi và các món đi kèm.',
  ComboItems: 'Các món và số lượng cho mỗi đơn vị combo trong danh mục hiện tại.',
  SessionCombos: 'Lượt mua combo của phiên chơi, số gói, tên/giá/giờ tại thời điểm mua.',
  SessionComboItems: 'Thành phần món được lưu tại thời điểm mua combo để giữ lịch sử.',
  Invoices: 'Hóa đơn của phiên chơi: tiền giờ, F&B, giảm giá và trạng thái thanh toán.',
  PaymentTransactions: 'Các giao dịch thanh toán của hóa đơn, phương thức và mã tham chiếu.',
  WorkShifts: 'Ca làm của nhân viên, thời điểm và số tiền đầu/cuối ca.'
};
const notes = {
  AspNetUsers: {
    Id: 'Khóa chuỗi Identity; không phải số tự tăng.', UserName: 'Tên đăng nhập gốc.', NormalizedUserName: 'Tên đăng nhập chuẩn hóa do Identity quản lý.',
    Email: 'Địa chỉ email gốc.', NormalizedEmail: 'Email chuẩn hóa; schema BMS yêu cầu không trùng khi có giá trị.', EmailConfirmed: 'Cờ xác nhận email.',
    PasswordHash: 'Hash mật khẩu do Identity tạo; seed NULL, chưa có mật khẩu để đăng nhập.', SecurityStamp: 'Dấu thay đổi bảo mật phục vụ kiểm tra phiên đăng nhập.',
    ConcurrencyStamp: 'Token kiểm soát cập nhật đồng thời của Identity; không phải rowversion.', PhoneNumber: 'Số điện thoại; cần chuẩn hóa nhất quán ở ứng dụng.',
    PhoneNumberConfirmed: 'Cờ xác nhận số điện thoại.', TwoFactorEnabled: 'Cờ sử dụng xác thực hai yếu tố; không tự chứng minh đã có luồng 2FA.',
    LockoutEnd: 'Thời điểm kết thúc khóa tạm; khác khóa quản trị IsActive.', LockoutEnabled: 'Cho phép áp dụng khóa tạm.', AccessFailedCount: 'Số lần đăng nhập thất bại.',
    FullName: 'Họ tên hiển thị.', IsActive: 'Trạng thái cho phép sử dụng tài khoản; backend/cookie phải kiểm tra.', CreatedAtUtc: 'Thời điểm tạo tài khoản, lưu UTC.',
    EmployeeCode: 'Mã nhân viên; tính duy nhất áp dụng toàn bảng.', RewardPoints: 'Điểm tích lũy; chưa có CHECK >= 0 hoặc lịch sử điểm.', TierId: 'Hạng thành viên; NULL nghĩa chưa gán hạng.', HireDate: 'Ngày vào làm; là date, không đổi múi giờ.'
  },
  AspNetRoles: { Id: 'Khóa chuỗi vai trò.', Name: 'Tên vai trò, ví dụ Admin, Staff, Customer.', NormalizedName: 'Tên vai trò chuẩn hóa, ví dụ ADMIN.', ConcurrencyStamp: 'Token kiểm soát sửa role đồng thời.' },
  AspNetUserRoles: { UserId: 'Tài khoản được gán vai trò.', RoleId: 'Vai trò được cấp; PK ghép ngăn trùng một cặp user–role.' },
  AspNetUserClaims: { Id: 'Khóa của claim.', UserId: 'Tài khoản sở hữu claim.', ClaimType: 'Loại claim.', ClaimValue: 'Giá trị claim; Identity quản lý.' },
  AspNetRoleClaims: { Id: 'Khóa của claim vai trò.', RoleId: 'Vai trò sở hữu claim.', ClaimType: 'Loại claim của role.', ClaimValue: 'Giá trị claim được cấp qua role.' },
  AspNetUserLogins: { LoginProvider: 'Tên nhà cung cấp đăng nhập ngoài.', ProviderKey: 'Định danh user tại nhà cung cấp; cùng LoginProvider tạo PK.', ProviderDisplayName: 'Tên nhà cung cấp để hiển thị.', UserId: 'Tài khoản BMS được liên kết; có bảng không có nghĩa đã tích hợp Google/Facebook.' },
  AspNetUserTokens: { UserId: 'Tài khoản sở hữu token.', LoginProvider: 'Nhà cung cấp của token.', Name: 'Tên/loại token; cùng UserId và LoginProvider tạo PK.', Value: 'Giá trị token; không đưa vào giao diện hoặc log công khai.' },
  MembershipTiers: { Id: 'Khóa hạng thành viên tự tăng.', TierName: 'Tên hạng; seed hiện chưa định nghĩa các hạng cụ thể.', DiscountPercent: 'Phần trăm ưu đãi; 10.00 nghĩa là 10%, không phải hệ số 0.10.' },
  TableTypes: { Id: 'Khóa loại bàn.', Name: 'Tên loại bàn, ví dụ Pool hoặc Carom.', HourlyRate: 'Đơn giá cơ sở cho một giờ; số tiền theo VND.' },
  BilliardTables: { Id: 'Khóa bàn.', TableCode: 'Mã bàn duy nhất, ví dụ B01.', TableTypeId: 'Loại bàn tương ứng.', FloorNumber: 'Tầng/khu vực bàn; schema cho phép 0.', Status: 'Trạng thái vật lý; đặt lịch tương lai không tự biến bàn thành Reserved.', RowVersion: 'Token SQL Server tự sinh để phát hiện cập nhật đồng thời; không phải ngày giờ.' },
  PricingConfigs: { Id: 'Khóa cấu hình giá.', TableTypeId: 'Loại bàn áp dụng.', HourlyRate: 'Giá giờ cho khoảng thời gian này.', StartHour: 'Giờ bắt đầu trong ngày; chưa có quy tắc ngày trong tuần.', EndHour: 'Giờ kết thúc; CHECK hiện không cho khoảng qua nửa đêm.' },
  Bookings: { Id: 'Khóa lượt đặt.', CustomerId: 'Khách đặt; FK chỉ kiểm tra tồn tại, role Customer do procedure/backend kiểm tra.', TableId: 'Bàn được đặt.', StartAtUtc: 'Thời điểm bắt đầu dự kiến, UTC.', EndAtUtc: 'Thời điểm kết thúc dự kiến, UTC; phải sau giờ bắt đầu.', Status: 'Trạng thái lượt đặt theo danh sách CHECK.', CreatedAtUtc: 'Thời điểm tạo lượt đặt, UTC.', CheckedInAtUtc: 'Thời điểm xác nhận khách đến; phải có cùng CheckedInById.', CheckedInById: 'Người xác nhận check-in; quyền Staff/Admin do nghiệp vụ kiểm tra.', CancelledAtUtc: 'Thời điểm hủy; phải có khi Status=Cancelled.', CancelledById: 'Người hủy; phải có khi Status=Cancelled.', CancellationReason: 'Lý do hủy, có thể NULL theo DDL.', Notes: 'Ghi chú lượt đặt.', RowVersion: 'Token phát hiện cập nhật đồng thời.' },
  PlaySessions: { Id: 'Khóa phiên chơi.', TableId: 'Bàn được sử dụng; mỗi bàn tối đa một phiên Active.', BookingId: 'Lượt đặt tương ứng, có thể NULL cho walk-in; một booking tối đa một phiên.', CustomerId: 'Khách chơi; schema cho phép khách vãng lai chưa gắn tài khoản.', OpenedById: 'Người mở phiên; lấy định danh đã xác thực.', ClosedById: 'Người đóng phiên; NULL khi Active, bắt buộc khi Closed.', StartAtUtc: 'Thời điểm bắt đầu thực tế, UTC.', EndAtUtc: 'Thời điểm kết thúc thực tế, UTC; trạng thái Closed mới có.', HourlyRateSnapshot: 'Giá giờ tại lúc mở; đổi giá danh mục không làm đổi phiên cũ.', PlaytimeAmount: 'Tiền giờ khi đóng phiên; Active để NULL.', Status: 'Active hoặc Closed; phải khớp điều kiện vòng đời.', RowVersion: 'Token phát hiện cập nhật đồng thời.' },
  ProductCategories: { Id: 'Khóa danh mục món.', Name: 'Tên nhóm, ví dụ Đồ uống hoặc Đồ ăn.' },
  Products: { Id: 'Khóa sản phẩm.', CategoryId: 'Danh mục sản phẩm.', Name: 'Tên món duy nhất.', Price: 'Giá món lẻ theo VND; phải lớn hơn 0.', StockQuantity: 'Tồn kho không âm; thao tác bán cần transaction để chống bán vượt tồn.', IsActive: 'Còn kinh doanh hay đã ngừng bán; khác với còn/hết hàng.', RowVersion: 'Token phát hiện cập nhật đồng thời.' },
  Orders: { Id: 'Khóa đơn gọi món.', SessionId: 'Phiên chơi phát sinh đơn.', OrderTimeUtc: 'Thời điểm gọi món, UTC.', Status: 'Pending, Preparing, Served hoặc Cancelled.' },
  OrderDetails: { Id: 'Khóa dòng món.', OrderId: 'Đơn chứa dòng món; FK hiện có cascade khi xóa đơn.', ProductId: 'Sản phẩm được gọi; không cascade xóa sản phẩm.', Quantity: 'Số lượng món, phải lớn hơn 0.', UnitPrice: 'Giá một món tại thời điểm gọi; DDL cho phép 0.', Subtotal: 'Thành tiền SQL tự tính bằng Quantity × UnitPrice; không nhập trực tiếp.' },
  Combos: { Id: 'Khóa combo.', Name: 'Tên gói duy nhất.', Price: 'Giá một gói; DDL cho phép 0.', PlaytimeHours: 'Số giờ chơi trong một gói.', IsActive: 'Gói đang bán hay đã ngừng bán.' },
  ComboItems: { ComboId: 'Combo trong danh mục.', ProductId: 'Món đi kèm; PK ghép ngăn lặp một món trong cùng combo.', Quantity: 'Số lượng món cho MỘT gói; chưa phải tổng số lượng phiên mua.' },
  SessionCombos: { Id: 'Khóa lượt mua combo.', SessionId: 'Phiên mua gói; schema cho phép nhiều lượt mua trong một phiên.', ComboId: 'Combo nguồn; không tự cập nhật thông tin đã mua theo danh mục mới.', Quantity: 'Số gói trong lượt mua.', ComboNameSnapshot: 'Tên combo tại lúc mua.', UnitPriceSnapshot: 'Giá MỘT gói tại lúc mua; tổng giá gói = Quantity × UnitPriceSnapshot.', PlaytimeHoursSnapshot: 'Giờ chơi MỘT gói tại lúc mua; tổng giờ = Quantity × PlaytimeHoursSnapshot.', PurchasedAtUtc: 'Thời điểm ghi nhận mua, UTC.', CreatedById: 'Người ghi nhận; backend kiểm tra quyền và lấy ID từ phiên đăng nhập.' },
  SessionComboItems: { SessionComboId: 'Lượt mua combo chứa quyền lợi món.', ProductId: 'Sản phẩm đi kèm.', ProductNameSnapshot: 'Tên món tại lúc mua, giữ nguyên khi đổi tên danh mục.', QuantityPerCombo: 'Số món cho MỘT gói tại lúc mua; tổng món = QuantityPerCombo × SessionCombos.Quantity.' },
  Invoices: { Id: 'Khóa hóa đơn.', SessionId: 'Phiên được lập hóa đơn; UNIQUE quy định tối đa một hóa đơn/phiên.', CustomerId: 'Khách gắn với hóa đơn; có thể NULL.', TotalPlaytime: 'Tổng tiền giờ; DDL chưa CHECK không âm.', TotalFandB: 'Tổng tiền món; DDL chưa CHECK không âm.', DiscountAmount: 'Số tiền giảm, không phải phần trăm; chưa có CHECK giới hạn mức giảm.', FinalAmount: 'SQL tự tính TotalPlaytime + TotalFandB − DiscountAmount; schema chưa chặn kết quả âm.', Status: 'Unpaid hoặc Paid; nghiệp vụ phải đối chiếu giao dịch để cập nhật.', CreatedAtUtc: 'Thời điểm tạo hóa đơn, UTC.' },
  PaymentTransactions: { Id: 'Khóa giao dịch.', InvoiceId: 'Hóa đơn nhận thanh toán; FK hiện có cascade khi xóa hóa đơn.', PaymentMethod: 'Cash, VNPay, SePay hoặc Transfer theo CHECK; enum không có nghĩa đã tích hợp các cổng.', AmountPaid: 'Số tiền giao dịch theo VND, lớn hơn 0.', GatewayRefCode: 'Mã tham chiếu cổng/ngân hàng; có thể NULL và chưa có UNIQUE.', TransactionTimeUtc: 'Thời điểm ghi nhận giao dịch, UTC.' },
  WorkShifts: { Id: 'Khóa ca làm.', StaffId: 'Tài khoản nhân viên; FK không tự kiểm tra role Staff.', StartTimeUtc: 'Thời điểm mở ca, UTC.', EndTimeUtc: 'Thời điểm đóng ca, UTC; có thể NULL.', StartingCash: 'Tiền đầu ca; chưa có CHECK không âm.', EndingCash: 'Tiền cuối ca; NULL nếu chưa ghi nhận, chưa có CHECK không âm.', Status: 'Active hoặc Closed; DDL chưa bắt buộc Closed có EndTimeUtc.', Notes: 'Ghi chú ca làm.' }
};
const tableNotes = {
  AspNetUsers: 'Không có bảng Employee tách riêng. Staff được xác định qua role Staff. UserManager quản lý password/normalized fields/security stamp. SQL-first: không áp dụng migration khởi tạo lên bảng đã có. FK đến tier không cấp quyền quản trị.',
  AspNetRoles: 'Vai trò là phân quyền, khác với MembershipTiers. NULL ở DDL không có nghĩa ứng dụng nên tạo role không tên.',
  AspNetUserRoles: 'SQL hỗ trợ nhiều role/user. Quy tắc một role khi tạo tài khoản là quy tắc ứng dụng, không phải UNIQUE(UserId).',
  AspNetUserClaims: 'Claim không thay thế xác thực danh tính. Cascade chỉ mô tả DDL; không cho phép xóa tài khoản có lịch sử nghiệp vụ.',
  AspNetRoleClaims: 'Bảng này có trong schema nhưng chưa được giao rõ trong phân công 20 bảng ban đầu; cần nhóm chốt người phụ trách.',
  AspNetUserLogins: 'BMS hiện dùng đăng nhập Identity; cần triển khai cấu hình provider trước khi hỗ trợ đăng nhập ngoài.',
  AspNetUserTokens: 'Không lưu mật khẩu rõ vào Value. Quy trình phát hành/thu hồi token thuộc Identity và ứng dụng.',
  MembershipTiers: 'Chưa có MinPoints, thứ tự hạng hoặc lịch sử điểm. Ngưỡng nâng hạng và phạm vi ưu đãi cần TV3/TV4 thống nhất; không tự ghi các cột chưa có vào SDS.',
  BilliardTables: 'Chỉ cập nhật trạng thái theo vòng đời nghiệp vụ. Đặt bàn theo lịch và trạng thái vật lý là hai thông tin khác nhau.',
  PricingConfigs: 'Chưa có constraint chặn các khoảng cấu hình giá chồng nhau. usp_OpenSession hiện đọc TableTypes.HourlyRate, chưa áp dụng PricingConfigs; không mô tả giá theo khung giờ là đã chạy.',
  Bookings: 'CHECK thời gian không chặn các booking trùng lịch. Procedure serialize theo bàn và kiểm tra khoảng [StartAtUtc, EndAtUtc); không bỏ qua bằng cách ghi EF trực tiếp mà thiếu kiểm tra tương đương. Check-in chưa tự mở phiên.',
  PlaySessions: 'usp_CloseSession hiện tính toàn bộ giờ theo giá snapshot và chuyển bàn sang AwaitingPayment. Chưa trừ giờ combo, thanh toán hoặc trả bàn về Available.',
  Products: 'Món đã có lịch sử nên ngừng bán bằng IsActive. Phân quyền và đồng bộ trừ tồn không được FK/CHECK này tự thực hiện.',
  Orders: 'Chưa có nhân viên ghi nhận hoặc liên kết nguồn combo trong bảng Orders hiện tại. Chưa có constraint bắt buộc phiên đang Active khi gọi món.',
  OrderDetails: 'DDL chưa UNIQUE(OrderId, ProductId), nên có thể có nhiều dòng cùng món. Dòng món combo chưa có liên kết nguồn; không tính các quyền lợi combo như món lẻ để thu tiền/trừ tồn hai lần.',
  Combos: 'Một gói gồm giờ và các món tại ComboItems. Schema không tự bắt buộc gói có ít nhất một món; chưa có loại bàn/khung giờ áp dụng.',
  ComboItems: 'Danh mục có thể thay đổi; thông tin đã mua phải đọc từ SessionCombos/SessionComboItems. FK đều NO ACTION khi xóa.',
  SessionCombos: 'Backend sao chép giá/tên/giờ từ database trong cùng transaction với các món; không tin giá từ form. Schema chưa có idempotency key, hủy/hoàn hoặc trạng thái giao món.',
  SessionComboItems: 'Lưu quyền lợi đã mua, chưa mô tả món đã giao. Snapshot còn cần service bảo vệ khỏi UPDATE trực tiếp. FK đều NO ACTION khi xóa.',
  Invoices: 'Chưa có thành phần tiền combo riêng, hóa đơn âm chưa bị CHECK chặn. Công thức kết hợp combo/giờ vượt/F&B cần thống nhất trước khi bán; không suy ra tổng đúng từ việc schema có bảng.',
  PaymentTransactions: 'Chưa có UNIQUE cho mã cổng hoặc xử lý IPN lặp trong schema. DDL có cascade nhưng nghiệp vụ cần giữ lịch sử thanh toán, không xóa hóa đơn đã thanh toán tùy ý.',
  WorkShifts: 'Chưa có unique filtered index giới hạn một ca Active/nhân viên, chưa có liên kết ca với Orders/Invoices/PaymentTransactions; chưa có CHECK ràng buộc trạng thái với thời điểm đóng.'
};
const order = Object.keys(descriptions);
if (tables.size !== 24 || order.length !== 24 || order.some(n => !tables.has(n))) throw Error('Expected exactly the documented 24 tables');
for (const t of tables.values()) {
  for (const c of t.columns) if (!notes[t.name]?.[c.name]) throw Error('Missing column explanation: ' + t.name + '.' + c.name);
  for (const fk of t.fks) if (!tables.get(fk.parent)?.columns.some(c => c.name === fk.parentColumn)) throw Error('Unknown FK target');
}
function mdCode(s) { return '`' + s.replaceAll('|', '&#124;') + '`'; }
function isUniqueColumn(t, c) {
  return /\bUNIQUE\b/.test(c.definition) || (t.pkCols.length === 1 && t.pkCols[0] === c.name)
    || t.indexes.some(i => i.unique && i.columns.length === 1 && i.columns[0] === c.name && (!i.suffix || i.suffix === 'WHERE ' + c.name + ' IS NOT NULL'));
}
function constraintCell(t, c) {
  if (c.definition.startsWith('AS ')) return mdCode(c.definition) + '; cột tính toán, không nhập trực tiếp';
  const type = /^(\w+(?:\([^)]*\))?)/.exec(c.definition)[1];
  const bits = [type, /\bNOT NULL\b/.test(c.definition) ? 'NOT NULL' : 'NULL'];
  if (/\bIDENTITY\b/.test(c.definition)) bits.push('IDENTITY');
  if (t.pkCols.includes(c.name)) bits.push(t.pkCols.length > 1 ? 'thuộc PK ghép' : 'PK');
  if (/\bUNIQUE\b/.test(c.definition)) bits.push('UNIQUE');
  const def = /\bDEFAULT (.+)$/.exec(c.definition); if (def) bits.push('DEFAULT ' + def[1]);
  let cell = bits.map(mdCode).join('; ');
  for (const fk of t.fks.filter(f => f.column === c.name)) cell += '; FK → ' + mdCode(fk.parent + '.' + fk.parentColumn) + '; ON DELETE ' + (fk.cascade ? 'CASCADE' : 'NO ACTION');
  for (const check of t.checks.filter(ch => ch.columns.includes(c.name))) cell += '; ' + (check.columns.length === 1 ? 'CHECK ' + mdCode(check.expression) : 'tham gia ' + mdCode(check.name) + ' (xem dưới)');
  for (const ix of t.indexes.filter(i => i.unique && i.columns.includes(c.name))) cell += '; ' + mdCode('UNIQUE INDEX ' + ix.name + '(' + ix.columns.join(', ') + ')' + (ix.suffix ? ' ' + ix.suffix : ''));
  return cell;
}
const er = ['erDiagram', '    direction LR'];
for (const name of order) {
  const t = tables.get(name);
  er.push('    ' + name + ' {');
  for (const c of t.columns.filter(c => t.pkCols.includes(c.name) || t.fks.some(f => f.column === c.name))) {
    const type = /^(\w+)/.exec(c.definition)[1];
    const keys = [];
    if (t.pkCols.includes(c.name)) keys.push('PK');
    if (t.fks.some(f => f.column === c.name)) keys.push('FK');
    if (isUniqueColumn(t, c) && !t.pkCols.includes(c.name)) keys.push('UK');
    er.push('        ' + type + ' ' + c.name + ' ' + keys.join(','));
  }
  er.push('    }');
}
let fkCount = 0, columnCount = 0;
for (const name of order) {
  const t = tables.get(name); columnCount += t.columns.length;
  for (const fk of t.fks) {
    fkCount++;
    const c = t.columns.find(c => c.name === fk.column);
    const parent = /\bNOT NULL\b/.test(c.definition) ? '||' : 'o|';
    const child = isUniqueColumn(t, c) ? 'o|' : 'o{';
    er.push('    ' + fk.parent + ' ' + parent + (t.pkCols.includes(fk.column) ? '--' : '..') + child + ' ' + t.name + ' : "' + fk.column + '"');
  }
}
const lines = [
  '## 3. Database Design — Toàn bộ BilliardDB', '',
  '**Ngày cập nhật:** 01/10/2026. **Nguồn:** `BMS_Database_Starter/BilliardDB_Full.sql` tại commit database `4448bb2`. **Phạm vi:** ' + tables.size + ' bảng nghiệp vụ, ' + columnCount + ' cột và ' + fkCount + ' khóa ngoại.', '',
  'Phần này thay thế mục Database Design bốn bảng trước đây để dùng trong báo cáo SDS chung của nhóm. Nội dung phản ánh DDL hiện có; không tự bổ sung các cột/ràng buộc chưa có. Bộ schema và seed đã được thực thi trên SQL Server; script nâng cấp combo đã áp dụng trên localhost / BilliardDB. Có bảng không có nghĩa mọi luồng nghiệp vụ đã được code.', '',
  '### 3.0 Sơ đồ database tổng thể', '',
  'Sơ đồ có đủ 24 bảng và toàn bộ FK. Mỗi hộp rút gọn chỉ hiện các cột khóa để dễ đọc; các bảng bên dưới mô tả đầy đủ mọi cột. `dbo.sysdiagrams` do SSMS tạo để lưu sơ đồ, không thuộc 24 bảng nghiệp vụ và không đưa vào SDS.', '',
  '```mermaid', ...er, '```', '',
  '**Hình I.3 — Database Diagram toàn hệ thống BMS (24 bảng).** PK là khóa chính; FK là khóa ngoại; UK là tính duy nhất ở cột. Đầu quan hệ 0..1 thể hiện FK có thể NULL hoặc phía con có FK duy nhất; 0..n thể hiện nhiều bản ghi. Nét liền là quan hệ định danh (FK thuộc PK), nét đứt là quan hệ không định danh. Điều kiện chỉ một phiên Active/bàn là unique index có lọc, không biến toàn bộ lịch sử phiên thành quan hệ một–một.', '',
  '**Danh mục bảng:**', '', '| No | Table | Description |', '|---|---|---|',
  ...order.map((n, i) => '| ' + (i + 1) + ' | [' + n + '](#3-' + (i + 1) + '-' + n.toLowerCase() + ') | ' + descriptions[n] + ' |'), '',
  'Sơ đồ lớn: kéo thanh cuộn để xem từng vùng, hoặc mở [ảnh SVG riêng](sds-hung/03-database.svg) để phóng to. Bấm tên bảng trong danh sách để đến bảng mô tả.', '',
  '**Quy ước đọc các bảng:**', '',
  '- `NULL`/`NOT NULL`, `DEFAULT`, `CHECK`, PK, FK và UNIQUE dưới đây lấy từ SQL. Một cột NULL ở SQL vẫn có thể bắt buộc ở form theo nghiệp vụ.',
  '- PK ghép yêu cầu duy nhất cả nhóm cột, không phải từng cột riêng. Unique filtered index chỉ áp dụng các dòng thỏa WHERE.',
  '- Cột `rowversion` do SQL Server sinh, phục vụ kiểm soát đồng thời; không lưu ngày giờ. Các cột có hậu tố Utc lưu UTC; khi hiển thị đổi sang giờ Việt Nam. `HireDate` là ngày, không đổi múi giờ.',
  '- Các số tiền dùng decimal; đơn vị nghiệp vụ hiện là VND. `DiscountPercent` là phần trăm; `DiscountAmount` là số tiền.',
  '- Cột `AS (...) PERSISTED` do SQL Server tính và lưu; DDL không khai báo riêng kiểu dữ liệu hoặc NOT NULL cho các cột tính toán này.',
  '- FK không ghi ON DELETE CASCADE có hành vi NO ACTION. FK không tự xác minh role, phiên đang mở, tồn kho hoặc chống xử lý yêu cầu lặp.', ''
];
for (let i = 0; i < order.length; i++) {
  const t = tables.get(order[i]);
  lines.push('### 3.' + (i + 1) + ' ' + t.name, '', '**Mục đích:** ' + descriptions[t.name] + ' **Primary key:** ' + mdCode('(' + t.pkCols.join(', ') + ')') + '.', '', '| Column | Constraints | Notes |', '|---|---|---|');
  for (const c of t.columns) lines.push('| ' + mdCode(c.name) + ' | ' + constraintCell(t, c) + ' | ' + notes[t.name][c.name] + ' |');
  lines.push('');
  const multi = t.checks.filter(ch => ch.columns.length > 1);
  if (multi.length) {
    lines.push('**CHECK phối hợp nhiều cột:**', '');
    for (const ch of multi) lines.push('- ' + mdCode(ch.name) + ': ' + mdCode(ch.expression) + '.');
    lines.push('');
  }
  if (t.indexes.length) {
    lines.push('**Indexes khai báo riêng:**', '');
    for (const ix of t.indexes) lines.push('- ' + mdCode(ix.name) + ': ' + mdCode((ix.unique ? 'UNIQUE ' : '') + '(' + ix.columns.join(', ') + ')' + (ix.suffix ? ' ' + ix.suffix : '')) + '.');
    lines.push('');
  }
  if (tableNotes[t.name]) lines.push('**Lưu ý nghiệp vụ:** ' + tableNotes[t.name], '');
}
lines.push('### 3.25 Các quan hệ và giới hạn cần thống nhất', '',
  '- Tài khoản → role là nhiều–nhiều qua AspNetUserRoles. MembershipTiers là hạng ưu đãi, không phải role bảo mật.',
  '- Một bàn có nhiều lượt đặt và nhiều phiên trong lịch sử. Mỗi booking tối đa một phiên; mỗi phiên tối đa một hóa đơn. Phiên walk-in có thể không có booking/tài khoản khách.',
  '- Một phiên có nhiều đơn F&B, nhiều lượt mua combo; một hóa đơn có nhiều giao dịch. Những quan hệ này lấy từ FK/UNIQUE, không tự xác nhận luồng thanh toán đã triển khai.',
  '- ComboItems là danh mục hiện tại; SessionCombos và SessionComboItems là thông tin lúc mua. Tổng giá gói = số gói × giá một gói; tổng giờ/món nhân theo số gói. Chưa có liên kết nguồn giao món combo trong OrderDetails hoặc thành phần tiền combo riêng trong Invoices.',
  '- Những điểm chưa có ràng buộc SQL: tiền hóa đơn/tiền ca không âm, GatewayRefCode duy nhất, một ca Active/nhân viên, khoảng cấu hình giá không trùng, ngưỡng nâng hạng và lịch sử điểm. Đây là phần cần thiết kế tiếp, không mô tả là đã tồn tại.',
  '- Tổng hợp CASCADE hiện có: AspNetUserRoles từ User/Role; AspNetUserClaims, AspNetUserLogins, AspNetUserTokens từ User; AspNetRoleClaims từ Role; OrderDetails từ Orders; PaymentTransactions từ Invoices. Các FK còn lại dùng NO ACTION. Không dùng khả năng cascade để xóa lịch sử nhân viên/hóa đơn trái quy tắc ứng dụng.', '',
  '**Cài đặt/kiểm tra:** xem `BMS_Database_Starter/README_VI.md`. Không chạy Full hoặc seed lại trên database đang có dữ liệu. Phần này là tài liệu thiết kế, không phải script thay đổi database.', ''
);
const database = lines.join('\n');
fs.writeFileSync(path.join(root, 'docs/DATABASE_DESIGN.md'), '# BMS — Database Design đầy đủ\n\n' + database);
const sdsPath = path.join(root, 'docs/SDS_HUNG.md');
let sds = fs.readFileSync(sdsPath, 'utf8');
const start = sds.indexOf('## 3. Database Design');
const end = sds.indexOf('# II. Detailed Code Design', start);
if (start < 0 || end < 0) throw Error('SDS section markers not found');
sds = sds.slice(0, start) + database + '\n' + sds.slice(end);
fs.writeFileSync(sdsPath, sds);
fs.writeFileSync(path.join(__dirname, '03-database.mmd'), er.join('\n') + '\n');
fs.writeFileSync(path.join(__dirname, 'database-schema-manifest.json'), JSON.stringify({ source: 'BMS_Database_Starter/BilliardDB_Full.sql', tableCount: tables.size, columnCount, foreignKeyCount: fkCount, tables: order.map(n => ({ name: n, columns: tables.get(n).columns.map(c => c.name), primaryKey: tables.get(n).pkCols, foreignKeys: tables.get(n).fks })) }, null, 2) + '\n');
console.log('Documented ' + tables.size + ' tables, ' + columnCount + ' columns and ' + fkCount + ' foreign keys. No SQL executed.');
