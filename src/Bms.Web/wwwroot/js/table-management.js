/**
 * BMS - Quản lý bàn & Phiên chơi (Table Management)
 * Lượt 8: Đổi giao diện màn hình Quản lý bàn cho Staff
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
        sessionId: null,
        startAtUtc: null,
        endAtUtc: null,
        cardStatus: '',
        cardSessionStart: ''
    };

    let lastSelectedCardId = null;     // Lưu ID thẻ bàn vừa chọn để trả focus khi đóng panel

    // Cờ trạng thái điều phối bất đồng bộ
    let isProcessing = false;          // Đang thực hiện mở bàn hoặc đóng phiên
    let isDetailLoading = false;       // Đang gửi request GetTableDetail
    let detailAbortController = null;  // Hủy request chi tiết cũ khi click chọn bàn mới
    let detailRequestId = 0;           // Bộ đếm thế hệ request chi tiết

    // Cấu hình & Trạng thái đồng hồ thời gian thực
    let clockTimerId = null;           // Bộ đếm 1 giây duy nhất (chống tạo trùng)
    let deltaOffset = 0;               // ms lệch giữa client browser và server (Clock Skew)

    // Cấu hình & Trạng thái Polling 10 giây
    const POLL_INTERVAL = 10000;
    let pollTimerId = null;
    let isPolling = false;             // Chống gửi chồng request polling
    let isPollingStopped = false;      // Dừng polling khi hết hạn phiên đăng nhập
    let pollingAbortController = null; // Hủy request polling đang bay khi mở/đóng bàn
    let pollingRequestId = 0;          // Bộ đếm thế hệ request polling
    let hasNetworkErrorNotified = false; // Chỉ hiển thị 1 thông báo nhỏ khi lỗi mạng/non-200

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

    /**
     * Định dạng tổng số giây thành chuỗi HH:mm:ss (số giờ có thể >= 100)
     * Trả về '--' nếu đầu vào không hợp lệ hoặc NaN
     */
    function formatElapsedSeconds(totalSeconds) {
        if (totalSeconds === null || totalSeconds === undefined || isNaN(totalSeconds) || totalSeconds < 0) {
            return '--';
        }
        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.floor((totalSeconds % 3600) / 60);
        const seconds = totalSeconds % 60;

        const pad = (num) => String(num).padStart(2, '0');
        return `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
    }

    /**
     * Tính và định dạng khoảng cách thời gian giữa hai mốc UTC cố định
     */
    function formatDuration(startUtcString, endUtcString) {
        if (!startUtcString || !endUtcString) return '--';
        try {
            const startMs = Date.parse(startUtcString);
            const endMs = Date.parse(endUtcString);
            if (isNaN(startMs) || isNaN(endMs)) return '--';

            const diffMs = endMs - startMs;
            if (isNaN(diffMs) || diffMs < 0) return '00:00:00';

            const totalSeconds = Math.floor(diffMs / 1000);
            return formatElapsedSeconds(totalSeconds);
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
    // Khởi tạo & Dọn dẹp Bootstrap Tooltip
    // =========================================================================
    function initTooltips() {
        if (window.bootstrap && window.bootstrap.Tooltip) {
            const tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
            tooltipTriggerList.forEach(tooltipTriggerEl => {
                if (typeof window.bootstrap.Tooltip.getInstance === 'function') {
                    const existing = window.bootstrap.Tooltip.getInstance(tooltipTriggerEl);
                    if (existing) return;
                }
                if (typeof window.bootstrap.Tooltip.getOrCreateInstance === 'function') {
                    window.bootstrap.Tooltip.getOrCreateInstance(tooltipTriggerEl);
                } else {
                    new window.bootstrap.Tooltip(tooltipTriggerEl);
                }
            });
        }
    }

    function disposeContainerTooltips(container) {
        if (!container || !window.bootstrap || !window.bootstrap.Tooltip) return;
        const tooltips = container.querySelectorAll('[data-bs-toggle="tooltip"]');
        tooltips.forEach(el => {
            if (typeof window.bootstrap.Tooltip.getInstance === 'function') {
                const instance = window.bootstrap.Tooltip.getInstance(el);
                if (instance) {
                    instance.dispose();
                }
            }
        });
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

        // Nếu đang xử lý (mở/đóng) hoặc đang tải dữ liệu chi tiết thì vô hiệu hóa cả OPEN và CLOSE
        if (isProcessing || isDetailLoading) {
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
            if (tableId && card.getAttribute('data-table-id') === String(tableId)) {
                card.classList.add('selected');
            } else {
                card.classList.remove('selected');
            }
        });
    }

    // =========================================================================
    // Hiệu chỉnh Clock Skew & Đồng hồ thời gian thực
    // =========================================================================
    function recalculateDeltaOffset() {
        const gridEl = document.getElementById('table-grid');
        if (!gridEl) return;
        const serverTimeStr = gridEl.getAttribute('data-server-time');
        if (!serverTimeStr) return;
        const parsedServerTime = Date.parse(serverTimeStr);
        if (!isNaN(parsedServerTime)) {
            deltaOffset = Date.now() - parsedServerTime;
        }
    }

    /**
     * Cập nhật đồng hồ mỗi giây:
     * - Truy vấn lại DOM trên thẻ bàn: tính thời gian chơi từ data-session-start và deltaOffset
     * - Cập nhật dòng Duration của panel phải nếu bàn đang chọn có phiên Active và panel đang hiển thị
     */
    function updateClocks() {
        const cards = document.querySelectorAll('#table-grid-container .table-card');
        cards.forEach(card => {
            const clockEl = card.querySelector('.table-clock');
            if (!clockEl) return;

            const sessionStart = card.getAttribute('data-session-start');
            if (!sessionStart) {
                clockEl.textContent = '--';
                return;
            }

            const startParsed = Date.parse(sessionStart);
            if (isNaN(startParsed)) {
                clockEl.textContent = '--';
                return;
            }

            const diffMs = (Date.now() - deltaOffset) - startParsed;
            if (isNaN(diffMs)) {
                clockEl.textContent = '--';
                return;
            }

            const totalSeconds = Math.max(0, Math.floor(diffMs / 1000));
            clockEl.textContent = formatElapsedSeconds(totalSeconds);
        });

        // Panel phải: chỉ chạy đồng hồ nếu bàn đang chọn có phiên Active và panel đang mở
        const panel = document.getElementById('table-detail-panel');
        const isPanelVisible = panel && !panel.classList.contains('d-none');

        if (isPanelVisible && currentSelectedTable.id && currentSelectedTable.startAtUtc && !currentSelectedTable.endAtUtc) {
            const elDuration = document.getElementById('detail-duration');
            if (elDuration) {
                const startParsed = Date.parse(currentSelectedTable.startAtUtc);
                if (isNaN(startParsed)) {
                    elDuration.textContent = '--';
                } else {
                    const diffMs = (Date.now() - deltaOffset) - startParsed;
                    if (isNaN(diffMs)) {
                        elDuration.textContent = '--';
                    } else {
                        const totalSeconds = Math.max(0, Math.floor(diffMs / 1000));
                        elDuration.textContent = formatElapsedSeconds(totalSeconds);
                    }
                }
            }
        }
    }

    /**
     * Khởi động bộ đếm 1 giây duy nhất (có cờ chống tạo trùng)
     */
    function startClockTimer() {
        if (clockTimerId !== null) return;
        updateClocks();
        clockTimerId = setInterval(updateClocks, 1000);
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

        const startUtc = detail.startAtUtc ?? detail.StartAtUtc ?? null;
        const endUtc = detail.endAtUtc ?? detail.EndAtUtc ?? null;
        currentSelectedTable.startAtUtc = startUtc;
        currentSelectedTable.endAtUtc = endUtc;

        // Lưu trữ cardStatus và cardSessionStart từ DOM thẻ bàn
        const card = document.querySelector(`.table-card[data-table-id="${currentSelectedTable.id}"]`);
        if (card) {
            currentSelectedTable.cardStatus = card.getAttribute('data-status') || '';
            currentSelectedTable.cardSessionStart = card.getAttribute('data-session-start') || '';
        } else {
            currentSelectedTable.cardStatus = currentSelectedTable.status;
            currentSelectedTable.cardSessionStart = '';
        }

        // Điền các phần tử giao diện bằng textContent (An toàn XSS)
        const elTableName = document.getElementById('detail-table-name');
        const elStatus = document.getElementById('detail-status');
        const elCustomer = document.getElementById('detail-customer');
        const elBookingTime = document.getElementById('detail-booking-time');
        const elStartTime = document.getElementById('detail-start-time');
        const elEndTime = document.getElementById('detail-end-time');
        const elDuration = document.getElementById('detail-duration');

        if (elTableName) elTableName.textContent = currentSelectedTable.code || '--';
        if (elStatus) elStatus.textContent = currentSelectedTable.displayStatus || '--';

        const customerName = detail.customerFullName ?? detail.CustomerFullName;
        if (elCustomer) elCustomer.textContent = customerName ? customerName : '--';
        if (elBookingTime) elBookingTime.textContent = '--';

        // Xử lý hiển thị thời gian Start Time, End Time, Duration
        if (startUtc && !endUtc) {
            if (elStartTime) elStartTime.textContent = formatVnDateTime(startUtc);
            if (elEndTime) elEndTime.textContent = '--';

            const startParsed = Date.parse(startUtc);
            if (isNaN(startParsed)) {
                if (elDuration) elDuration.textContent = '--';
            } else {
                const diffMs = (Date.now() - deltaOffset) - startParsed;
                const totalSeconds = isNaN(diffMs) ? 0 : Math.max(0, Math.floor(diffMs / 1000));
                if (elDuration) elDuration.textContent = formatElapsedSeconds(totalSeconds);
            }
        } else if (startUtc && endUtc) {
            if (elStartTime) elStartTime.textContent = formatVnDateTime(startUtc);
            if (elEndTime) elEndTime.textContent = formatVnDateTime(endUtc);
            if (elDuration) elDuration.textContent = formatDuration(startUtc, endUtc);
        } else {
            if (elStartTime) elStartTime.textContent = '--';
            if (elEndTime) elEndTime.textContent = '--';
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

        isDetailLoading = false;
        updateButtons();
    }

    // =========================================================================
    // Xóa trắng thông tin trên panel chi tiết
    // =========================================================================
    function clearDetailFields() {
        const elTableName = document.getElementById('detail-table-name');
        const elStatus = document.getElementById('detail-status');
        const elCustomer = document.getElementById('detail-customer');
        const elBookingTime = document.getElementById('detail-booking-time');
        const elStartTime = document.getElementById('detail-start-time');
        const elEndTime = document.getElementById('detail-end-time');
        const elDuration = document.getElementById('detail-duration');

        if (elTableName) elTableName.textContent = '--';
        if (elStatus) elStatus.textContent = '--';
        if (elCustomer) elCustomer.textContent = '--';
        if (elBookingTime) elBookingTime.textContent = '--';
        if (elStartTime) elStartTime.textContent = '--';
        if (elEndTime) elEndTime.textContent = '--';
        if (elDuration) elDuration.textContent = '--';

        const elPlaytimeRow = document.getElementById('detail-playtime-row');
        const elPlaytimeAmount = document.getElementById('detail-playtime-amount');
        if (elPlaytimeRow) elPlaytimeRow.classList.add('d-none');
        if (elPlaytimeAmount) elPlaytimeAmount.textContent = '--';
    }

    // =========================================================================
    // Đóng panel chi tiết (✕ hoặc Esc hoặc khi bàn biến mất)
    // =========================================================================
    function closeDetailPanel(returnFocus = true) {
        const panel = document.getElementById('table-detail-panel');
        if (panel) {
            panel.classList.add('d-none');
        }

        // Bỏ highlight thẻ bàn đang chọn
        highlightSelectedCard(null);

        const prevSelectedId = currentSelectedTable.id || lastSelectedCardId;

        // Hủy yêu cầu lấy chi tiết đang bay nếu có
        if (detailAbortController) {
            detailAbortController.abort();
            detailAbortController = null;
        }

        currentSelectedTable = {
            id: null,
            code: '',
            status: '',
            displayStatus: '',
            sessionId: null,
            startAtUtc: null,
            endAtUtc: null,
            cardStatus: '',
            cardSessionStart: ''
        };

        isDetailLoading = false;
        clearDetailFields();
        updateButtons();

        // Trả focus về thẻ vừa chọn nếu còn tồn tại
        if (returnFocus && prevSelectedId) {
            const card = document.querySelector(`.table-card[data-table-id="${prevSelectedId}"]`);
            if (card && typeof card.focus === 'function') {
                card.focus();
            }
        }
    }

    // resetDetailPanel: khi bàn đang chọn biến mất sau polling -> ẩn panel
    function resetDetailPanel() {
        closeDetailPanel(false);
    }

    // =========================================================================
    // Xử lý khi click / phím chọn bàn (handleTableSelect)
    // =========================================================================
    async function handleTableSelect(tableId) {
        if (!tableId) return;

        lastSelectedCardId = tableId;

        // Hiển thị panel ngay lập tức bên cạnh sơ đồ
        const panel = document.getElementById('table-detail-panel');
        if (panel) {
            panel.classList.remove('d-none');
        }

        // Hủy yêu cầu lấy chi tiết trước đó nếu còn đang bay
        if (detailAbortController) {
            detailAbortController.abort();
        }
        detailAbortController = new AbortController();
        const signal = detailAbortController.signal;
        const requestId = ++detailRequestId;

        isDetailLoading = true;

        currentSelectedTable.id = tableId;
        highlightSelectedCard(tableId);

        // Hiển thị trạng thái đang tải ngắn gọn trong lúc đợi phản hồi
        const card = document.querySelector(`.table-card[data-table-id="${tableId}"]`);
        const cardCode = card ? card.getAttribute('data-table-code') : '';

        const elTableName = document.getElementById('detail-table-name');
        const elStatus = document.getElementById('detail-status');
        const elCustomer = document.getElementById('detail-customer');
        const elBookingTime = document.getElementById('detail-booking-time');
        const elStartTime = document.getElementById('detail-start-time');
        const elEndTime = document.getElementById('detail-end-time');
        const elDuration = document.getElementById('detail-duration');

        if (elTableName) elTableName.textContent = cardCode || 'Đang tải...';
        if (elStatus) elStatus.textContent = 'Đang tải...';
        if (elCustomer) elCustomer.textContent = 'Đang tải...';
        if (elBookingTime) elBookingTime.textContent = 'Đang tải...';
        if (elStartTime) elStartTime.textContent = 'Đang tải...';
        if (elEndTime) elEndTime.textContent = 'Đang tải...';
        if (elDuration) elDuration.textContent = 'Đang tải...';

        const elPlaytimeRow = document.getElementById('detail-playtime-row');
        if (elPlaytimeRow) elPlaytimeRow.classList.add('d-none');

        // Vô hiệu hóa các nút hành động trong lúc đang tải
        updateButtons();

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
            const isRedirectToLogin = response.redirected && (
                response.url.includes('/Account/Login') ||
                response.url.includes('/AccessDenied') ||
                response.url.includes('/Account/AccessDenied')
            );
            if (isRedirectToLogin || response.status === 401 || response.status === 403) {
                showToast('Phiên làm việc đã hết hạn, vui lòng tải lại trang.', 'danger');
                stopPolling();
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
                stopPolling();
                return;
            }

            const detail = await response.json();
            if (requestId !== detailRequestId) return;

            // Kiểm tra nếu người dùng đã chủ động đóng panel trong lúc fetch thì không điền dữ liệu
            const currentPanel = document.getElementById('table-detail-panel');
            if (!currentPanel || currentPanel.classList.contains('d-none')) {
                return;
            }

            applyTableDetail(detail);
        } catch (err) {
            if (err.name === 'AbortError') return;
            if (requestId === detailRequestId) {
                showToast('Không kết nối được máy chủ.', 'danger');
            }
        } finally {
            if (requestId === detailRequestId) {
                isDetailLoading = false;
                updateButtons();
            }
        }
    }

    // =========================================================================
    // Áp dụng HTML lưới bàn mới và bảo toàn trạng thái bàn đang chọn
    // =========================================================================
    async function applyGridHtml(html) {
        const parser = new DOMParser();
        const doc = parser.parseFromString(html, 'text/html');
        const newGrid = doc.getElementById('table-grid');
        if (!newGrid) {
            const isLoginPage = doc.querySelector('form[action*="Login"]') ||
                                doc.title?.toLowerCase().includes('đăng nhập') ||
                                doc.title?.toLowerCase().includes('login') ||
                                html.includes('/Account/Login') ||
                                html.includes('/AccessDenied');
            if (isLoginPage) {
                showToast('Phiên đăng nhập đã hết hạn, vui lòng tải lại trang.', 'danger');
                stopPolling();
            } else if (!hasNetworkErrorNotified) {
                showToast('Dữ liệu danh sách bàn không hợp lệ.', 'danger');
                hasNetworkErrorNotified = true;
            }
            return;
        }

        const container = document.getElementById('table-grid-container');
        if (!container) return;

        // Dọn dẹp tooltip cũ trong container
        disposeContainerTooltips(container);

        const selectedId = currentSelectedTable.id;
        const prevCardStatus = currentSelectedTable.cardStatus;
        const prevCardSessionStart = currentSelectedTable.cardSessionStart;

        // Thay thế HTML lưới bàn
        container.innerHTML = html;

        // Tái tính deltaOffset và cập nhật đồng hồ
        recalculateDeltaOffset();
        updateClocks();
        initTooltips();

        const panel = document.getElementById('table-detail-panel');
        const isPanelVisible = panel && !panel.classList.contains('d-none');

        // Nếu không có bàn nào đang chọn hoặc panel đang ẩn thì không hiển thị lại panel
        if (!selectedId || !isPanelVisible) {
            updateButtons();
            return;
        }

        const card = container.querySelector(`.table-card[data-table-id="${selectedId}"]`);
        if (!card) {
            // Bàn đang chọn không còn trên lưới -> ẩn panel và xóa thông tin
            resetDetailPanel();
            return;
        }

        // Gán lại class selected cho thẻ bàn
        card.classList.add('selected');

        const newCardStatus = card.getAttribute('data-status') || '';
        const newCardSessionStart = card.getAttribute('data-session-start') || '';

        // Chỉ gọi lại GetTableDetail khi data-status hoặc data-session-start đổi
        if (newCardStatus !== prevCardStatus || newCardSessionStart !== prevCardSessionStart) {
            await handleTableSelect(selectedId);
        } else {
            updateButtons();
        }
    }

    // =========================================================================
    // Cơ chế Polling 10 giây định kỳ
    // =========================================================================
    function stopPolling() {
        isPollingStopped = true;
        if (pollTimerId !== null) {
            clearInterval(pollTimerId);
            pollTimerId = null;
        }
        if (pollingAbortController) {
            pollingAbortController.abort();
            pollingAbortController = null;
        }
    }

    async function pollTableGrid() {
        if (isPollingStopped) return;
        if (document.hidden) return;

        if (isPolling || isProcessing || isDetailLoading) return;

        if (pollingAbortController) {
            pollingAbortController.abort();
        }
        pollingAbortController = new AbortController();
        const signal = pollingAbortController.signal;
        const requestId = ++pollingRequestId;

        isPolling = true;

        try {
            const response = await fetch('/Table/GetTableCardsPartial', {
                method: 'GET',
                headers: {
                    'Accept': 'text/html'
                },
                credentials: 'same-origin',
                signal
            });

            if (requestId !== pollingRequestId) return;

            const isRedirectToLogin = response.redirected && (
                response.url.includes('/Account/Login') ||
                response.url.includes('/AccessDenied') ||
                response.url.includes('/Account/AccessDenied')
            );
            const isAuthError = (response.status === 401 || response.status === 403);

            if (isRedirectToLogin || isAuthError) {
                showToast('Phiên đăng nhập đã hết hạn, vui lòng tải lại trang.', 'danger');
                stopPolling();
                return;
            }

            if (!response.ok) {
                if (!hasNetworkErrorNotified) {
                    showToast('Không thể kết nối máy chủ để cập nhật danh sách bàn.', 'danger');
                    hasNetworkErrorNotified = true;
                }
                return;
            }

            const contentType = response.headers.get('content-type') || '';
            if (!contentType.includes('text/html')) {
                if (!hasNetworkErrorNotified) {
                    showToast('Định dạng dữ liệu trả về không hợp lệ.', 'danger');
                    hasNetworkErrorNotified = true;
                }
                return;
            }

            const html = await response.text();
            if (requestId !== pollingRequestId) return;

            if (isProcessing) return;

            hasNetworkErrorNotified = false;

            await applyGridHtml(html);
        } catch (err) {
            if (err.name === 'AbortError') return;
            if (!hasNetworkErrorNotified) {
                showToast('Không thể kết nối máy chủ để cập nhật danh sách bàn.', 'danger');
                hasNetworkErrorNotified = true;
            }
        } finally {
            if (requestId === pollingRequestId) {
                isPolling = false;
            }
        }
    }

    function startPolling() {
        if (pollTimerId !== null || isPollingStopped) return;
        pollTimerId = setInterval(pollTableGrid, POLL_INTERVAL);
    }

    // =========================================================================
    // Tải lại lưới bàn sau thao tác (Open/Close)
    // =========================================================================
    async function reloadTableGrid() {
        try {
            const response = await fetch('/Table/GetTableCardsPartial', {
                method: 'GET',
                headers: {
                    'Accept': 'text/html'
                },
                credentials: 'same-origin'
            });

            const isRedirectToLogin = response.redirected && (
                response.url.includes('/Account/Login') ||
                response.url.includes('/AccessDenied') ||
                response.url.includes('/Account/AccessDenied')
            );
            if (isRedirectToLogin || response.status === 401 || response.status === 403) {
                showToast('Phiên đăng nhập đã hết hạn, vui lòng tải lại trang.', 'danger');
                stopPolling();
                return;
            }

            if (!response.ok) {
                showToast('Không thể tải lại danh sách bàn.', 'danger');
                return;
            }

            const html = await response.text();
            await applyGridHtml(html);
        } catch {
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

        if (pollingAbortController) {
            pollingAbortController.abort();
            pollingAbortController = null;
        }
        pollingRequestId++;
        isPolling = false;

        isProcessing = true;
        updateButtons();

        const targetTableId = currentSelectedTable.id;
        const targetTableCode = currentSelectedTable.code;

        try {
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

            const isRedirectToLogin = response.redirected && (
                response.url.includes('/Account/Login') ||
                response.url.includes('/AccessDenied') ||
                response.url.includes('/Account/AccessDenied')
            );
            if (isRedirectToLogin || response.status === 401 || response.status === 403) {
                showToast('Phiên đăng nhập đã hết hạn, vui lòng tải lại trang.', 'danger');
                stopPolling();
                return;
            }

            const contentType = response.headers.get('content-type') || '';
            if (!contentType.includes('application/json')) {
                showToast('Phiên đăng nhập đã hết hạn, vui lòng tải lại trang.', 'danger');
                stopPolling();
                return;
            }

            const result = await response.json();
            const isSuccess = Boolean(result.success ?? result.Success);
            const autoReload = Boolean(result.autoReload ?? result.AutoReload);
            const serverMessage = result.message ?? result.Message;

            if (isSuccess) {
                const successMsg = serverMessage || `Đã mở bàn ${targetTableCode} thành công.`;
                showToast(successMsg, 'success');
                await reloadTableGrid();
            } else {
                const errorMsg = serverMessage || 'Thao tác không thành công. Vui lòng thử lại.';
                showToast(errorMsg, 'danger');
                if (autoReload) {
                    await reloadTableGrid();
                }
            }
        } catch {
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
        const targetTableCode = currentSelectedTable.code;

        const confirmed = window.confirm(`Bạn có chắc chắn muốn đóng phiên cho bàn ${targetTableCode}?`);
        if (!confirmed) {
            return;
        }

        const token = getVerificationToken();
        if (!token) {
            showToast('Thiếu mã xác thực bảo mật (Anti-forgery token).', 'danger');
            return;
        }

        if (pollingAbortController) {
            pollingAbortController.abort();
            pollingAbortController = null;
        }
        pollingRequestId++;
        isPolling = false;

        isProcessing = true;
        updateButtons();

        try {
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

            const isRedirectToLogin = response.redirected && (
                response.url.includes('/Account/Login') ||
                response.url.includes('/AccessDenied') ||
                response.url.includes('/Account/AccessDenied')
            );
            if (isRedirectToLogin || response.status === 401 || response.status === 403) {
                showToast('Phiên đăng nhập đã hết hạn, vui lòng tải lại trang.', 'danger');
                stopPolling();
                return;
            }

            const contentType = response.headers.get('content-type') || '';
            if (!contentType.includes('application/json')) {
                showToast('Phiên đăng nhập đã hết hạn, vui lòng tải lại trang.', 'danger');
                stopPolling();
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
                await reloadTableGrid();
            } else {
                const errorMsg = serverMessage || 'Thao tác không thành công. Vui lòng thử lại.';
                showToast(errorMsg, 'danger');
                if (autoReload) {
                    await reloadTableGrid();
                }
            }
        } catch {
            showToast('Không kết nối được máy chủ.', 'danger');
        } finally {
            isProcessing = false;
            updateButtons();
        }
    }

    // =========================================================================
    // Khởi tạo các sự kiện giao diện (Event Delegation & Keyboard Navigation)
    // =========================================================================
    function initEvents() {
        const gridContainer = document.getElementById('table-grid-container');
        if (gridContainer) {
            // Click chuột vào thẻ bàn
            gridContainer.addEventListener('click', (e) => {
                const card = e.target.closest('.table-card');
                if (!card) return;
                const tableId = card.getAttribute('data-table-id');
                if (tableId) {
                    handleTableSelect(tableId);
                }
            });

            // Chọn thẻ bàn bằng bàn phím (Enter hoặc Space)
            gridContainer.addEventListener('keydown', (e) => {
                const card = e.target.closest('.table-card');
                if (!card) return;
                if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') {
                    e.preventDefault();
                    const tableId = card.getAttribute('data-table-id');
                    if (tableId) {
                        handleTableSelect(tableId);
                    }
                }
            });
        }

        // Nút đóng panel chi tiết (✕)
        const btnCloseDetail = document.getElementById('btn-close-detail');
        if (btnCloseDetail) {
            btnCloseDetail.addEventListener('click', () => {
                closeDetailPanel(true);
            });
        }

        // Phím Escape: chỉ đóng panel khi panel đang hiển thị
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' || e.key === 'Esc') {
                const panel = document.getElementById('table-detail-panel');
                if (panel && !panel.classList.contains('d-none')) {
                    closeDetailPanel(true);
                }
            }
        });

        const btnOpen = document.getElementById('btn-open-table');
        if (btnOpen) {
            btnOpen.addEventListener('click', handleOpenTable);
        }

        const btnClose = document.getElementById('btn-close-table');
        if (btnClose) {
            btnClose.addEventListener('click', handleCloseTable);
        }

        // Page Visibility API: Dừng khi ẩn, cập nhật ngay khi tab hiển thị lại
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden && !isPollingStopped) {
                updateClocks();
                pollTableGrid();
            }
        });
    }

    // =========================================================================
    // Điểm khởi chạy khi DOM sẵn sàng
    // =========================================================================
    document.addEventListener('DOMContentLoaded', () => {
        initEvents();
        initTooltips();
        updateButtons();
        recalculateDeltaOffset();
        startClockTimer();
        startPolling();
    });
})();
