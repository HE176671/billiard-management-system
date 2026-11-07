from pathlib import Path
from xml.sax.saxutils import escape
import re, json, os, zipfile
from PIL import ImageFont, Image

ROOT = Path(__file__).parent
FONT = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 19)
MAPS = {
'05-login': {
'Browser / Login View':'Trang đăng nhập', 'Identity EF / SQL Server':'Cơ sở dữ liệu',
'Nhập username, password, RememberMe':'Nhập tên đăng nhập và mật khẩu',
'POST /Account/Login + anti-forgery token':'Gửi thông tin đăng nhập',
'Framework từ chối token không hợp lệ trước action':'Kiểm tra yêu cầu hợp lệ trước khi xử lý',
'ModelState không hợp lệ':'Thông tin nhập chưa hợp lệ', 'ModelState hợp lệ':'Thông tin nhập hợp lệ',
'Trả form và lỗi validation':'Hiện lỗi để người dùng nhập lại',
'FindByNameAsync(Username)':'Tìm tài khoản theo tên đăng nhập',
'Tra NormalizedUserName':'Tra cứu tài khoản', 'User hoặc null':'Có tài khoản hoặc không tìm thấy',
'Không có user hoặc IsActive=false':'Không tìm thấy tài khoản hoặc bị vô hiệu hóa',
'Thông báo đăng nhập không hợp lệ':'Báo đăng nhập không thành công',
'PasswordSignInAsync(user, password, RememberMe, true)':'Yêu cầu kiểm tra đăng nhập',
'Đọc / cập nhật trạng thái bảo mật khi cần':'Đọc và cập nhật thông tin bảo mật',
'Kết quả lưu / đọc':'Trả kết quả xử lý',
'Ghi cookie qua HTTP response':'Ghi nhận đăng nhập trên trình duyệt', 'Succeeded':'Đăng nhập thành công',
'IsInRoleAsync khi không có returnUrl local':'Lấy quyền khi cần chọn trang mở',
'Vai trò':'Quyền của tài khoản',
'Redirect URL local hoặc Employee / Staff / Customer':'Mở trang nội bộ phù hợp',
'IsLockedOut':'Tài khoản bị khóa tạm',
'Thông báo khóa tạm, không cấp cookie':'Báo khóa tạm, không cho đăng nhập',
'Sai mật khẩu hoặc trạng thái chưa hỗ trợ':'Sai mật khẩu hoặc chưa được phép đăng nhập',
'Thông báo chung, không cấp cookie':'Báo lỗi, không cho đăng nhập',
},
'06-create-staff': {
'Admin':'Người quản trị', 'SQL Server':'Cơ sở dữ liệu',
'POST /Employee/Create + form + token':'Gửi thông tin nhân viên mới',
'Authorize Admin và anti-forgery phải hợp lệ':'Chỉ người quản trị được tạo; kiểm tra yêu cầu hợp lệ',
'Dữ liệu form không hợp lệ':'Thông tin nhập chưa hợp lệ',
'Trả form với lỗi validation':'Hiện lỗi để sửa thông tin',
'Trim dữ liệu, chuẩn hóa mã, tạo ApplicationUser':'Chuẩn bị thông tin tài khoản',
'BeginTransactionAsync()':'Bắt đầu nhóm các thay đổi',
'BEGIN TRANSACTION':'Giữ các thay đổi trong cùng một lần xử lý',
'CreateAsync(user, password)':'Tạo tài khoản nhân viên',
'Qua EF store cùng context: INSERT user có hash':'Lưu tài khoản và giá trị bảo vệ mật khẩu',
'IdentityResult hoặc exception':'Trả kết quả hoặc báo lỗi',
'Tạo user thất bại':'Tạo tài khoản thất bại', 'Tạo user thành công':'Tạo tài khoản thành công',
'RollbackAsync()':'Hủy các thay đổi của lần tạo', 'ROLLBACK':'Hủy các thay đổi chưa xác nhận',
'Báo lỗi hợp lệ / lỗi hệ thống đã được che chi tiết':'Báo lỗi, không lộ chi tiết hệ thống',
'AddToRoleAsync(user, Staff)':'Cấp quyền nhân viên (Staff)',
'INSERT AspNetUserRoles':'Lưu quyền nhân viên cho tài khoản',
'Gán role thất bại':'Cấp quyền thất bại', 'Gán role thành công':'Cấp quyền thành công',
'ROLLBACK cả user và role link':'Hủy cả tài khoản mới và quyền vừa cấp',
'Báo không tạo được nhân viên':'Báo không tạo được nhân viên',
'CommitAsync()':'Xác nhận lưu cả hai bước', 'COMMIT':'Lưu toàn bộ thay đổi',
'Thông báo thành công và redirect /Employee':'Báo thành công và mở danh sách nhân viên',
},
'07-change-password': {
'Identity EF / SQL Server':'Cơ sở dữ liệu',
'POST ChangePassword + current/new/confirm + token':'Gửi mật khẩu hiện tại, mới và xác nhận',
'Authorize và anti-forgery, kiểm tra active theo thiết kế bảo mật':'Yêu cầu đăng nhập và kiểm tra yêu cầu hợp lệ',
'ModelState không hợp lệ':'Thông tin nhập chưa hợp lệ', 'Form hợp lệ':'Thông tin nhập hợp lệ',
'Trả lỗi required / độ dài / xác nhận không khớp':'Báo thiếu thông tin hoặc mật khẩu mới chưa hợp lệ',
'GetUserAsync(HttpContext.User)':'Lấy tài khoản đang đăng nhập',
'Tìm tài khoản theo user ID trong principal':'Tìm tài khoản từ thông tin đăng nhập',
'User hoặc null':'Có tài khoản hoặc không tìm thấy',
'User không còn tồn tại':'Không tìm thấy tài khoản', 'Có user':'Tìm thấy tài khoản',
'Challenge đăng nhập lại':'Yêu cầu đăng nhập lại',
'ChangePasswordAsync(user, CurrentPassword, NewPassword)':'Yêu cầu đổi mật khẩu',
'Xác minh mật khẩu cũ và chính sách mật khẩu mới':'Kiểm tra mật khẩu hiện tại và mật khẩu mới',
'Mật khẩu cũ sai / mới không hợp lệ':'Mật khẩu hiện tại sai hoặc mật khẩu mới chưa đạt yêu cầu',
'IdentityResult thất bại':'Đổi mật khẩu không thành công',
'Trả lỗi, mật khẩu cũ không đổi':'Báo lỗi, giữ mật khẩu hiện tại',
'Lưu hash mới và dấu bảo mật, kiểm tra concurrency':'Lưu giá trị bảo vệ mật khẩu mới và thông tin bảo mật',
'IdentityResult':'Trả kết quả đổi mật khẩu',
'Báo lỗi, không thông báo thành công':'Báo không lưu được thay đổi',
'RefreshSignInAsync(user)':'Cập nhật thông tin đăng nhập hiện tại',
'Phát lại cookie cho phiên hiện tại':'Ghi nhận lại đăng nhập trên trình duyệt',
'Redirect ChangePassword với thông báo thành công':'Mở lại trang đổi mật khẩu và báo thành công',
}}

def wrap(txt, width):
    lines, line = [], ''
    for word in txt.split():
        test = (line+' '+word).strip()
        if FONT.getlength(test) > width and line:
            lines.append(line); line=word
        else: line=test
    if line: lines.append(line)
    return lines

def render(src, title, out):
    participants, events = [], []
    for raw in src.splitlines():
        line=raw.strip()
        m=re.match(r'(actor|participant) (\w+) as (.+)',line)
        if m: participants.append((m[2],m[3])); continue
        m=re.match(r'(\w+)(-->>|->>)(\w+): (.+)',line)
        if m: events.append(('message',m[1],m[3],m[4],m[2]=='-->>')); continue
        m=re.match(r'Note over (\w+),(\w+): (.+)',line)
        if m: events.append(('note',m[1],m[2],m[3])); continue
        if line.startswith('alt '): events.append(('alt',line[4:])); continue
        if line.startswith('else '): events.append(('else',line[5:])); continue
        if line=='end': events.append(('end',))
    W=1800; xs={p[0]:130+i*1540/(len(participants)-1) for i,p in enumerate(participants)}
    layers=[]; y=180; seq=0; frames=[]; stack=[]
    def text(x,y,txt,size=19,color='#20364d',anchor='middle',weight='normal'):
        layers.append(f'<text x="{x}" y="{y}" font-family="Arial" font-size="{size}" fill="{color}" text-anchor="{anchor}" font-weight="{weight}">{escape(txt)}</text>')
    for e in events:
        if e[0]=='message':
            _,a,b,label,dashed=e; x1,x2=xs[a],xs[b]; seq+=1
            width=max(220,abs(x2-x1)-35) if a!=b else 380
            lines=wrap(f'{seq}. {label}',width); y+=len(lines)*23+14
            center=(x1+x2)/2 if a!=b else min(x1+200,W-220)
            for k,l in enumerate(lines): text(center,y-14-(len(lines)-1-k)*23,l)
            dash=' stroke-dasharray="7 5"' if dashed else ''
            if a!=b: layers.append(f'<path d="M{x1},{y} L{x2},{y}" fill="none" stroke="#365f83" stroke-width="2"{dash} marker-end="url(#arrow)"/>')
            else: layers.append(f'<path d="M{x1},{y} h80 v18 h-80" fill="none" stroke="#365f83" stroke-width="2" marker-end="url(#arrow)"/>'); y+=18
            y+=25
        elif e[0]=='note':
            width=1100; lines=wrap(e[3],width-40); hh=26*len(lines)+16
            layers.append(f'<rect x="350" y="{y}" width="{width}" height="{hh}" rx="5" fill="#f4f7fa" stroke="#b1bfce"/>')
            for k,l in enumerate(lines): text(900,y+27+26*k,l,18)
            y+=hh+25
        elif e[0]=='alt':
            stack.append((y,len(stack))); text(80+len(stack)*14,y+24,'alt — '+e[1],18,anchor='start',weight='bold'); y+=45
        elif e[0]=='else':
            x=58+(len(stack)-1)*14
            layers.append(f'<line x1="{x}" y1="{y}" x2="{W-58}" y2="{y}" stroke="#7c95ae" stroke-dasharray="6 4"/>')
            text(x+15,y+25,'Nếu '+e[1].lower(),18,anchor='start',weight='bold');y+=45
        elif e[0]=='end':
            start,depth=stack.pop(); frames.append(f'<rect x="{58+depth*14}" y="{start}" width="{W-116-depth*14}" height="{y-start}" fill="none" stroke="#7c95ae" stroke-width="1.5"/>'); y+=18
    H=y+130
    svg=[f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">', '<defs><marker id="arrow" markerWidth="10" markerHeight="10" refX="9" refY="5" orient="auto"><path d="M0,0 L10,5 L0,10" fill="#365f83"/></marker></defs>',f'<rect width="{W}" height="{H}" fill="white"/>',f'<text x="900" y="44" text-anchor="middle" font-family="Arial" font-size="30" font-weight="bold" fill="#20364d">{escape(title)}</text>']
    for pid,label in participants:
        x=xs[pid]
        svg.append(f'<line x1="{x}" y1="145" x2="{x}" y2="{y+15}" stroke="#a5b6c7" stroke-dasharray="6 5"/>')
        for yy in [77,y+25]:
            svg.append(f'<rect x="{x-125}" y="{yy}" width="250" height="68" rx="6" fill="#eef4fa" stroke="#7893ad"/>')
            for i,l in enumerate(wrap(label,225)):svg.append(f'<text x="{x}" y="{yy+29+i*24}" text-anchor="middle" font-family="Arial" font-size="19" fill="#20364d">{escape(l)}</text>')
    svg+=layers+frames+['</svg>']; out.write_text(''.join(svg),encoding='utf-8')

titles=['ĐĂNG NHẬP','TẠO TÀI KHOẢN NHÂN VIÊN','ĐỔI MẬT KHẨU']
for (name,mapping),title in zip(MAPS.items(),titles):
    src=(ROOT.parent/(name+'.mmd')).read_text(encoding='utf-8')
    # Replace longer phrases first to preserve their meaning.
    for old,new in sorted(mapping.items(),key=lambda x:-len(x[0])): src=src.replace(old,new)
    (ROOT/(name+'.mmd')).write_text(src,encoding='utf-8')
    render(src,title,ROOT/(name+'.svg'))
print('Created three editable Vietnamese sequence diagrams.')
