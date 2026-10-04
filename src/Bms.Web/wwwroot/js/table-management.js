/**
 * BMS - Quản lý bàn & Phiên chơi (Table Management)
 * Lượt 5: JavaScript chọn bàn, mở và đóng phiên chơi
 */
(() => {
    'use strict';

    // =========================================================================
    // Trạng thái cục bộ (Local State)
    // =========================================================================
    let currentSelectedTable = {
        id: null,
        code: '',
        status: '',        // DB status: "Available", "InUse", "AwaitingPayment", "Maintenance", "Inactive"
        displayStatus: '', // Vietnamese text: "Trống", "Đang chơi", ...
        sessionId: null
    };

    let isProcessing = false;
    let detailAbortController = null;
    let detailRequestId = 0;

    // =========================================================================
    // Định dạng ngày giờ và tiền tệ (Intl Formatters)
    // =========================================================================
    const vnDateTimeFormatter = new Intl.DateTimeFormat('vi-VN', {
        timeZone: 'Asia/Ho_Chi_Minh',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hour12: false
    });

    const vnCurrencyFormatter = new Intl.NumberFormat('vi-VN', {
        style: 'currency',
        currency: 'VND'
    });

    function formatVnDateTime(utcDateString) {
        if (!utcDateString) return '--';
        try {
            const date = new Date(utcDateString);
            if (isNaN(date.getTime())) return '--';
            return vnDateTimeFormatter.format(date);
        } catch {
            return '--';
        }
    }

    function formatVnCurrency(amount) {
        if (amount === null || amount === undefined || isNaN(amount)) return '0 ₫';
        return vnCurrencyFormatter.format(amount);
    }

    function formatDuration(startUtcString, endUtcString) {
        if (!startUtcString || !endUtcString) return '--';
        try {
            const startDate = new Date(startUtcString);
            const endDate = new Date(endUtcString);
            if (isNaN(startDate.getTime()) || isNaN(endDate.getTime())) return '--';

            const diffMs = endDate.getTime() - startDate.getTime();
            if (diffMs < 0) return '00:00:00';

            const totalSeconds = Math.floor(diffMs / 1000);
            const hours = Math.floor(totalSeconds / 3600);
            const minutes = Math.floor((totalSeconds % 3600) / 60);
            const seconds = totalSeconds % 60;

            const pad = (num) => String(num).padStart(2, '0');
            return `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
        } catch {
            return '--';
        }
    }

    // =========================================================================
    // Thông báo Toast (Bootstrap 5 Toast)
    // =========================================================================
    function showToast(message, type = 'success') {
        const toastEl = document.getElementById('bms-toast');
        const msgEl = document.getElementById('bms-toast-message');
        if (!toastEl || !msgEl) return;

        // An toàn: hiển thị bằng textContent chống XSS
        msgEl.textContent = message;

        toastEl.classList.remove('bg-success', 'bg-danger', 'bg-warning', 'bg-info');
        if (type === 'success') {
            toastEl.classList.add('bg-success');
        } else {
            toastEl.classList.add('bg-danger');
        }

        if (window.bootstrap && window.bootstrap.Toast) {
            const toast = window.bootstrap.Toast.getOrCreateInstance(toastEl, { delay: 4500 });
            toast.show();
        } else {
            toastEl.style.display = 'block';
            setTimeout(() => {
                toastEl.style.display = 'none';
            }, 4500);
        }
    }

    // =========================================================================
    // Lấy Anti-Forgery Token
    // =========================================================================
    function getVerificationToken() {
        const holder = document.getElementById('antiforgery-holder');
        const input = holder ? holder.querySelector('input[name="__RequestVerificationToken"]') : null;
        return input ? input.value : '';
    }

    // =========================================================================
    // Cập nhật trạng thái các nút hành động (updateButtons)
    // =========================================================================
    function updateButtons() {
        const btnOpen = document.getElementById('btn-open-table');
        const btnClose = document.getElementById('btn-close-table');
        const btnConfirmBooking = document.getElementById('btn-confirm-booking');
        const btnTransfer = document.getElementById('btn-transfer');
        const btnSplitMerge = document.getElementById('btn-split-merge');

        // 3 nút theo wireframe chưa có logic: luôn vô hiệu hóa
        if (btnConfirmBooking) btnConfirmBooking.disabled = true;
        if (btnTransfer) btnTransfer.disabled = true;
        if (btnSplitMerge) btnSplitMerge.disabled = true;

        // Nếu đang xử lý request thì vô hiệu hóa cả OPEN và CLOSE để chống bấm đúp
        if (isProcessing) {
            if (btnOpen) btnOpen.disabled = true;
            if (btnClose) btnClose.disabled = true;
            return;
        }

        // Tính trạng thái dựa trên bàn đang chọn
        const { status, sessionId } = currentSelectedTable;

        // Nút OPEN TABLE chỉ bật khi Status = "Available"
        if (btnOpen) {
            btnOpen.disabled = (status !== 'Available');
        }

        // Nút CLOSE TABLE chỉ bật khi Status = "InUse" và có SessionId
        if (btnClose) {
            btnClose.disabled = !(status === 'InUse' && sessionId);
        }
    }

    // =========================================================================
    // Đánh dấu thẻ bàn đang chọn trên giao diện
    // =========================================================================
    function highlightSelectedCard(tableId) {
        const container = document.getElementById('table-grid-container');
        if (!container) return;

        const cards = container.querySelectorAll('.table-card');
        cards.forEach(card => {
            if (card.getAttribute('data-table-id') === String(tableId)) {
                card.classList.add('selected');
            } else {
                card.classList.remove('selected');
            }
        });
    }

    // =========================================================================
    // Hiển thị chi tiết bàn lên panel phải
    // =========================================================================
    function applyTableDetail(detail) {
        currentSelectedTable.id = detail.tableId ?? detail.TableId;
        currentSelectedTable.code = detail.tableCode ?? detail.TableCode ?? '';
        currentSelectedTable.status = detail.status ?? detail.Status ?? '';
        currentSelectedTable.displayStatus = detail.displayStatus ?? detail.DisplayStatus ?? '';
        currentSelectedTable.sessionId = detail.sessionId ?? detail.SessionId ?? null;

        // Điền các phần tử giao diện bằng textContent (An toàn XSS)
        const elTableName = document.getElementById('detail-table-name');
        const elStatus = document.getElementById('detail-status');
        const elBadge = document.getElementById('detail-panel-badge');
        const elCustomer = document.getElementById('detail-customer');
        const elBookingTime = document.getElementById('detail-booking-time');
        const elStartTime = document.getElementById('detail-start-time');
        const elEndTime = document.getElementById('detail-end-time');
        const elDuration = document.getElementById('detail-duration');

        if (elTableName) elTableName.textContent = currentSelectedTable.code || '--';
        if (elStatus) elStatus.textContent = currentSelectedTable.displayStatus || '--';
        if (elBadge) elBadge.textContent = currentSelectedTable.displayStatus || 'Chưa chọn bàn';

        const customerName = detail.customerFullName ?? detail.CustomerFullName;
        if (elCustomer) elCustomer.textContent = customerName ? customerName : '--';
        if (elBookingTime) elBookingTime.textContent = '--';

        const startUtc = detail.startAtUtc ?? detail.StartAtUtc;
        const endUtc = detail.endAtUtc ?? detail.EndAtUtc;
        if (elStartTime) elStartTime.textContent = formatVnDateTime(startUtc);
        if (elEndTime) elEndTime.textContent = formatVnDateTime(endUtc);

        if (endUtc && startUtc) {
            if (elDuration) elDuration.textContent = formatDuration(startUtc, endUtc);
        } else {
            if (elDuration) elDuration.textContent = '--';
        }

        const elPlaytimeRow = document.getElementById('detail-playtime-row');
        const elPlaytimeAmount = document.getElementById('detail-playtime-amount');
        const playtimeAmount = detail.playtimeAmount ?? detail.PlaytimeAmount;

        if (playtimeAmount !== null && playtimeAmount !== undefined) {
            if (elPlaytimeAmount) elPlaytimeAmount.textContent = formatVnCurrency(playtimeAmount);
            if (elPlaytimeRow) elPlaytimeRow.classList.remove('d-none');
        } else {
            if (elPlaytimeRow) elPlaytimeRow.classList.add('d-none');
            if (elPlaytimeAmount) elPlaytimeAmount.textContent = '--';
        }

        updateButtons();
    }

    // =========================================================================
    // Xóa trắng thông tin trên panel phải khi không chọn bàn nào
    // =========================================================================
    function resetDetailPanel() {
        currentSelectedTable = {
            id: null,
            code: '',
            status: '',
            displayStatus: '',
            sessionId: null
        };

        const elTableName = document.getElementById('detail-table-name');
        const elStatus = document.getElementById('detail-status');
        const elBadge = document.getElementById('detail-panel-badge');
        const elCustomer = document.getElementById('detail-customer');
        const elBookingTime = document.getElementById('detail-booking-time');
        const elStartTime = document.getElementById('detail-start-time');
        const elEndTime = document.getElementById('detail-end-time');
        const elDuration = document.getElementById('detail-duration');

        if (elTableName) elTableName.textContent = '--';
        if (elStatus) elStatus.textContent = '--';
        if (elBadge) elBadge.textContent = 'Chưa chọn bàn';
        if (elCustomer) elCustomer.textContent = '--';
        if (elBookingTime) elBookingTime.textContent = '--';
        if (elStartTime) elStartTime.textContent = '--';
        if (elEndTime) elEndTime.textContent = '--';
        if (elDuration) elDuration.textContent = '--';

        const elPlaytimeRow = document.getElementById('detail-playtime-row');
        const elPlaytimeAmount = document.getElementById('detail-playtime-amount');
        if (elPlaytimeRow) elPlaytimeRow.classList.add('d-none');
        if (elPlaytimeAmount) elPlaytimeAmount.textContent = '--';

        updateButtons();
    }

    // =========================================================================
    // Xử lý khi click chọn bàn (handleTableSelect)
    // Chống phản hồi sai thứ tự bằng AbortController và bộ đếm yêu cầu
    // =========================================================================
    async function handleTableSelect(tableId) {
        if (!tableId) return;

        // Hủy yêu cầu lấy chi tiết trước đó nếu còn đang bay
        if (detailAbortController) {
            detailAbortController.abort();
        }
        detailAbortController = new AbortController();
        const signal = detailAbortController.signal;
        const requestId = ++detailRequestId;

        // Đánh dấu thẻ bàn được chọn ngay lập tức
        highlightSelectedCard(tableId);

        try {
            const response = await fetch(`/Table/GetTableDetail?tableId=${encodeURIComponent(tableId)}`, {
                method: 'GET',
                headers: {
                    'Accept': 'application/json'
                },
                credentials: 'same-origin',
                signal
            });

            // Nếu đây không phải yêu cầu mới nhất thì bỏ qua
            if (requestId !== detailRequestId) return;

            // Kiểm tra chuyển hướng hoặc hết phiên làm việc
            if (response.redirected || response.status === 401 || response.status === 403) {
                showToast('Phiên làm việc đã hết hạn, vui lòng tải lại trang.', 'danger');
                return;
            }

            if (!response.ok) {
                if (response.status === 404) {
                    showToast('Không tìm thấy thông tin bàn.', 'danger');
                } else {
                    showToast('Không thể lấy chi tiết bàn. Vui lòng thử lại.', 'danger');
                }
                return;
            }

            const contentType = response.headers.get('content-type') || '';
            if (!contentType.includes('application/json')) {
                showToast('Phiên làm việc đã hết hạn, vui lòng tải lại trang.', 'danger');
                return;
            }

            const detail = await response.json();
            if (requestId !== detailRequestId) return;

            applyTableDetail(detail);
        } catch (err) {
            if (err.name === 'AbortError') {
                // Đã bị hủy bởi lần click chọn bàn mới hơn, không coi là lỗi
                return;
            }
            if (requestId === detailRequestId) {
                showToast('Không kết nối được máy chủ.', 'danger');
            }
        }
    }

    // =========================================================================
    // Tải lại lưới bàn (GetTableCardsPartial) và giữ lại bàn đang chọn
    // =========================================================================
    async function reloadGridAndReselect(targetTableId) {
        try {
            const response = await fetch('/Table/GetTableCardsPartial', {
                method: 'GET',
                credentials: 'same-origin'
            });

            if (response.redirected || response.status === 401 || response.status === 403) {
                showToast('Phiên làm việc đã hết hạn, vui lòng tải lại trang.', 'danger');
                return;
            }

            if (!response.ok) {
                showToast('Không thể tải lại danh sách bàn.', 'danger');
                return;
            }

            const html = await response.text();
            const container = document.getElementById('table-grid-container');
            if (!container) return;

            // Chỉ thay thế HTML của partial lưới bàn bên trong container
            container.innerHTML = html;

            // Tái khởi tạo tooltip nếu cần
            initTooltips();

            // Kiểm tra xem bàn đang chọn còn tồn tại trong lưới mới không
            const cardToReselect = targetTableId
                ? container.querySelector(`.table-card[data-table-id="${targetTableId}"]`)
                : null;

            if (cardToReselect) {
                highlightSelectedCard(targetTableId);
                await handleTableSelect(targetTableId);
            } else {
                resetDetailPanel();
            }
        } catch (err) {
            showToast('Không kết nối được máy chủ khi tải danh sách bàn.', 'danger');
        }
    }

    // =========================================================================
    // Thao tác MỞ BÀN (OpenSession)
    // =========================================================================
    async function handleOpenTable() {
        if (isProcessing) return;
        if (!currentSelectedTable.id || currentSelectedTable.status !== 'Available') return;

        const token = getVerificationToken();
        if (!token) {
            showToast('Thiếu mã xác thực bảo mật (Anti-forgery token).', 'danger');
            return;
        }

        // Chống bấm đúp: vô hiệu nút và bật cờ xử lý
        isProcessing = true;
        updateButtons();

        const targetTableId = currentSelectedTable.id;
        const targetTableCode = currentSelectedTable.code;

        try {
            // Gửi dữ liệu form urlencoded, KHÔNG dùng header token, KHÔNG gửi JSON
            const params = new URLSearchParams();
            params.append('TableId', targetTableId);
            params.append('__RequestVerificationToken', token);

            const response = await fetch('/Table/OpenSession', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded'
                },
                body: params,
                credentials: 'same-origin'
            });

            if (response.redirected || response.status === 401 || response.status === 403) {
                showToast('Phiên làm việc đã hết hạn, vui lòng tải lại trang.', 'danger');
                return;
            }

            const contentType = response.headers.get('content-type') || '';
            if (!contentType.includes('application/json')) {
                showToast('Phiên làm việc đã hết hạn, vui lòng tải lại trang.', 'danger');
                return;
            }

            const result = await response.json();
            const isSuccess = Boolean(result.success ?? result.Success);
            const autoReload = Boolean(result.autoReload ?? result.AutoReload);
            const serverMessage = result.message ?? result.Message;

            if (isSuccess) {
                const successMsg = serverMessage || `Đã mở bàn ${targetTableCode} thành công.`;
                showToast(successMsg, 'success');
                await reloadGridAndReselect(targetTableId);
            } else {
                const errorMsg = serverMessage || 'Thao tác không thành công. Vui lòng thử lại.';
                showToast(errorMsg, 'danger');
                if (autoReload) {
                    await reloadGridAndReselect(targetTableId);
                }
            }
        } catch (err) {
            showToast('Không kết nối được máy chủ.', 'danger');
        } finally {
            isProcessing = false;
            updateButtons();
        }
    }

    // =========================================================================
    // Thao tác ĐÓNG PHIÊN (CloseSession)
    // =========================================================================
    async function handleCloseTable() {
        if (isProcessing) return;
        if (!currentSelectedTable.sessionId || currentSelectedTable.status !== 'InUse') return;

        const targetSessionId = currentSelectedTable.sessionId;
        const targetTableId = currentSelectedTable.id;
        const targetTableCode = currentSelectedTable.code;

        // Hộp xác nhận có tên bàn trước khi gửi
        const confirmed = window.confirm(`Bạn có chắc chắn muốn đóng phiên cho bàn ${targetTableCode}?`);
        if (!confirmed) {
            return;
        }

        const token = getVerificationToken();
        if (!token) {
            showToast('Thiếu mã xác thực bảo mật (Anti-forgery token).', 'danger');
            return;
        }

        // Chống bấm đúp: vô hiệu nút và bật cờ xử lý
        isProcessing = true;
        updateButtons();

        try {
            // Gửi dữ liệu form urlencoded, KHÔNG dùng header token, KHÔNG gửi JSON
            const params = new URLSearchParams();
            params.append('SessionId', targetSessionId);
            params.append('__RequestVerificationToken', token);

            const response = await fetch('/Table/CloseSession', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded'
                },
                body: params,
                credentials: 'same-origin'
            });

            if (response.redirected || response.status === 401 || response.status === 403) {
                showToast('Phiên làm việc đã hết hạn, vui lòng tải lại trang.', 'danger');
                return;
            }

            const contentType = response.headers.get('content-type') || '';
            if (!contentType.includes('application/json')) {
                showToast('Phiên làm việc đã hết hạn, vui lòng tải lại trang.', 'danger');
                return;
            }

            const result = await response.json();
            const isSuccess = Boolean(result.success ?? result.Success);
            const autoReload = Boolean(result.autoReload ?? result.AutoReload);
            const serverMessage = result.message ?? result.Message;

            if (isSuccess) {
                const data = result.data ?? result.Data;
                const playtimeAmount = data?.playtimeAmount ?? data?.PlaytimeAmount ?? 0;
                const formattedAmount = formatVnCurrency(playtimeAmount);
                const successMsg = `Đã đóng phiên bàn ${targetTableCode} thành công. Tổng tiền giờ: ${formattedAmount}`;
                showToast(successMsg, 'success');
                await reloadGridAndReselect(targetTableId);
            } else {
                const errorMsg = serverMessage || 'Thao tác không thành công. Vui lòng thử lại.';
                showToast(errorMsg, 'danger');
                if (autoReload) {
                    await reloadGridAndReselect(targetTableId);
                }
            }
        } catch (err) {
            showToast('Không kết nối được máy chủ.', 'danger');
        } finally {
            isProcessing = false;
            updateButtons();
        }
    }

    // =========================================================================
    // Khởi tạo Bootstrap Tooltip
    // =========================================================================
    function initTooltips() {
        if (window.bootstrap && window.bootstrap.Tooltip) {
            const tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
            tooltipTriggerList.forEach(tooltipTriggerEl => {
                if (typeof window.bootstrap.Tooltip.getOrCreateInstance === 'function') {
                    window.bootstrap.Tooltip.getOrCreateInstance(tooltipTriggerEl);
                } else {
                    new window.bootstrap.Tooltip(tooltipTriggerEl);
                }
            });
        }
    }

    // =========================================================================
    // Khởi tạo các sự kiện giao diện (Event Delegation)
    // =========================================================================
    function initEvents() {
        // Lắng nghe trên container cha #table-grid-container để khi partial view tải lại vẫn nhận event
        const gridContainer = document.getElementById('table-grid-container');
        if (gridContainer) {
            gridContainer.addEventListener('click', (e) => {
                const card = e.target.closest('.table-card');
                if (!card) return;
                const tableId = card.getAttribute('data-table-id');
                if (tableId) {
                    handleTableSelect(tableId);
                }
            });
        }

        const btnOpen = document.getElementById('btn-open-table');
        if (btnOpen) {
            btnOpen.addEventListener('click', handleOpenTable);
        }

        const btnClose = document.getElementById('btn-close-table');
        if (btnClose) {
            btnClose.addEventListener('click', handleCloseTable);
        }
    }

    // =========================================================================
    // Điểm khởi chạy khi DOM sẵn sàng
    // =========================================================================
    document.addEventListener('DOMContentLoaded', () => {
        initEvents();
        initTooltips();
        updateButtons();
    });
})();
