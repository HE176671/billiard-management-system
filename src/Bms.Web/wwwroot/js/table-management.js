/**
 * BMS - Quản lý bàn & Phiên chơi (Table Management)
 * Lượt 4: Đếm ngược, cảnh báo sắp hết giờ & Gia hạn phiên chơi
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
        sessionMode: null, // "Open" hoặc "Timed"
        startAtUtc: null,
        endAtUtc: null,
        plannedEndAtUtc: null,
        billingStartAtUtc: null,
        billingEndAtUtc: null,
        cardStatus: '',
        cardSessionStart: '',
        cardBillingStart: '',
        cardPlannedEnd: ''
    };

    let lastSelectedCardId = null;     // Lưu ID thẻ bàn vừa chọn để trả focus khi đóng panel

    // Cờ trạng thái điều phối bất đồng bộ
    let isProcessing = false;          // Đang thực hiện mở bàn, đóng phiên hoặc gia hạn
    let isDetailLoading = false;       // Đang gửi request GetTableDetail
    let detailAbortController = null;  // Hủy request chi tiết cũ khi click chọn bàn mới
    let detailRequestId = 0;           // Bộ đếm thế hệ request chi tiết

    // Trạng thái form mở bàn
    let isOpenFormVisible = false;     // Đang hiển thị form cấu hình mở bàn
    let openFormState = {
        mode: 'Open',                  // 'Open' hoặc 'Timed'
        minutes: 60                    // Mặc định 60 phút khi chọn Timed
    };

    // Trạng thái form gia hạn phiên
    let isExtendFormVisible = false;   // Đang hiển thị form gia hạn phiên
    let extendFormState = {
        minutes: 30                    // Mặc định 30 phút
    };

    // Cấu hình & Trạng thái đồng hồ thời gian thực
    let clockTimerId = null;           // Bộ đếm 1 giây duy nhất (chống tạo trùng)
    let deltaOffset = 0;               // ms lệch giữa client browser và server (Clock Skew)
    let isClockSkewCalculated = false; // Đã nhận server time và tính deltaOffset thành công

    // Bộ nhớ theo dõi cảnh báo hết giờ (In-memory Map chống lặp & dọn rác)
    // Key: String(sessionId), Value: { plannedEndAtUtc: string, alertLevel: 'normal'|'warning'|'overdue' }
    const notifiedSessions = new Map();
    let isInitialScan = true;          // Lần quét đầu tiên chỉ chạy khi đã có dữ liệu thật

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

    const vnHourMinuteFormatter = new Intl.DateTimeFormat('vi-VN', {
        timeZone: 'Asia/Ho_Chi_Minh',
        hour: '2-digit',
        minute: '2-digit',
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

    function formatVnTimeOnly(utcDateOrMs) {
        if (!utcDateOrMs) return '--:--';
        try {
            const date = typeof utcDateOrMs === 'number' ? new Date(utcDateOrMs) : new Date(utcDateOrMs);
            if (isNaN(date.getTime())) return '--:--';
            return vnHourMinuteFormatter.format(date);
        } catch {
            return '--:--';
        }
    }

    function formatVnCurrency(amount) {
        if (amount === null || amount === undefined || isNaN(amount)) return '0 ₫';
        return vnCurrencyFormatter.format(amount);
    }

    /**
     * Định dạng tổng số giây thành chuỗi HH:mm:ss (số giờ có thể >= 100)
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

            const diffMs = Math.max(0, endMs - startMs);
            const totalSeconds = Math.floor(diffMs / 1000);
            return formatElapsedSeconds(totalSeconds);
        } catch {
            return '--';
        }
    }

    /**
     * Định dạng số giây thành mm:ss (dùng cho đếm ngược chờ tính giờ)
     */
    function formatSecondsToStart(totalSeconds) {
        if (totalSeconds === null || totalSeconds === undefined || isNaN(totalSeconds) || totalSeconds < 0) {
            return '--:--';
        }
        const minutes = Math.floor(totalSeconds / 60);
        const seconds = totalSeconds % 60;
        const pad = (num) => String(num).padStart(2, '0');
        return `${pad(minutes)}:${pad(seconds)}`;
    }

    /**
     * Hàm trạng thái giờ tính tiền dùng chung duy nhất cho thẻ bàn, panel và xem trước
     * @param {number} billingStartMs - Mốc thời gian tính tiền dạng ms
     * @param {number} nowMs - Thời gian hiện tại theo đồng hồ server (nowServerMs)
     * @returns {{ phase: 'pending' | 'running', secondsToStart: number, elapsedMs: number }}
     */
    function getBillingPhase(billingStartMs, nowMs) {
        if (billingStartMs === null || billingStartMs === undefined || isNaN(billingStartMs) ||
            nowMs === null || nowMs === undefined || isNaN(nowMs)) {
            return {
                phase: 'running',
                secondsToStart: 0,
                elapsedMs: 0
            };
        }

        if (nowMs < billingStartMs) {
            const diffMs = billingStartMs - nowMs;
            const secondsToStart = Math.max(0, Math.ceil(diffMs / 1000));
            return {
                phase: 'pending',
                secondsToStart: secondsToStart,
                elapsedMs: 0
            };
        } else {
            const elapsedMs = Math.max(0, nowMs - billingStartMs);
            return {
                phase: 'running',
                secondsToStart: 0,
                elapsedMs: elapsedMs
            };
        }
    }

    /**
     * Định dạng thời gian đếm ngược (mm:ss hoặc hh:mm:ss khi >= 1 giờ)
     */
    function formatRemainingTime(totalMs) {
        if (totalMs === null || totalMs === undefined || isNaN(totalMs)) return '--';
        const totalSeconds = Math.max(0, Math.floor(totalMs / 1000));
        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.floor((totalSeconds % 3600) / 60);
        const seconds = totalSeconds % 60;
        const pad = (num) => String(num).padStart(2, '0');

        if (hours >= 1) {
            return `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
        }
        return `${pad(minutes)}:${pad(seconds)}`;
    }

    /**
     * Hàm dùng chung duy nhất tính toán trạng thái thời gian và chuỗi hiển thị
     * Cho thẻ bàn, panel chi tiết và xem trước gia hạn
     * @param {number} plannedEndMs - Mốc kết thúc dự kiến dạng ms
     * @param {number} nowMs - Thời gian hiện tại theo đồng hồ server (nowServerMs)
     * @param {number|null} billingStartMs - Mốc bắt đầu tính tiền dạng ms (tùy chọn)
     * @returns {{ state: 'none' | 'pending' | 'normal' | 'warning' | 'overdue', remainingMs: number | null, label: string, formattedTime: string, text: string, panelText: string }}
     */
    function getTimeState(plannedEndMs, nowMs, billingStartMs = null) {
        if (plannedEndMs === null || plannedEndMs === undefined || isNaN(plannedEndMs) ||
            nowMs === null || nowMs === undefined || isNaN(nowMs)) {
            return {
                state: 'none',
                remainingMs: null,
                label: '',
                formattedTime: '--',
                text: '--',
                panelText: '--'
            };
        }

        const hasValidBillingStart = (billingStartMs !== null && billingStartMs !== undefined && !isNaN(billingStartMs));
        const isPending = hasValidBillingStart && (nowMs < billingStartMs);
        const effectiveStartMs = hasValidBillingStart ? Math.max(nowMs, billingStartMs) : nowMs;
        const remainingMs = plannedEndMs - effectiveStartMs;

        if (isPending) {
            const formatted = formatRemainingTime(remainingMs);
            return {
                state: 'pending',
                remainingMs,
                label: 'Còn lại',
                formattedTime: formatted,
                text: `Còn lại ${formatted} (chờ tính giờ)`,
                panelText: `${formatted} (chờ tính giờ)`
            };
        } else if (remainingMs > 600000) {
            const formatted = formatRemainingTime(remainingMs);
            return {
                state: 'normal',
                remainingMs,
                label: 'Còn lại',
                formattedTime: formatted,
                text: `Còn lại ${formatted}`,
                panelText: formatted
            };
        } else if (remainingMs > 0) {
            const formatted = formatRemainingTime(remainingMs);
            return {
                state: 'warning',
                remainingMs,
                label: 'Sắp hết giờ',
                formattedTime: formatted,
                text: `Sắp hết giờ - ${formatted}`,
                panelText: `Sắp hết giờ - ${formatted}`
            };
        } else {
            const overdueMs = Math.abs(remainingMs);
            const formatted = formatRemainingTime(overdueMs);
            return {
                state: 'overdue',
                remainingMs,
                label: 'Hết giờ',
                formattedTime: formatted,
                text: `Hết giờ +${formatted}`,
                panelText: `Hết giờ +${formatted}`
            };
        }
    }

    // =========================================================================
    // Thông báo Toast (Bootstrap 5 Toast - Hỗ trợ tối đa 3 toast cùng lúc)
    // =========================================================================
    function showToast(message, type = 'success', delay = 4500) {
        const container = document.getElementById('bms-toast-container');
        if (!container) return;

        // Giới hạn tối đa 3 toast cùng lúc: gỡ bỏ toast cũ nhất khi có toast thứ 4
        while (container.children.length >= 3) {
            const oldest = container.firstElementChild;
            if (oldest) {
                if (window.bootstrap && window.bootstrap.Toast) {
                    const bsInstance = window.bootstrap.Toast.getInstance(oldest);
                    if (bsInstance) bsInstance.dispose();
                }
                oldest.remove();
            }
        }

        const toastEl = document.createElement('div');
        const bgClass = (type === 'success') ? 'bg-success text-white' :
                        (type === 'warning') ? 'bg-warning text-dark' : 'bg-danger text-white';
        toastEl.className = `toast align-items-center border-0 mb-2 ${bgClass}`;
        toastEl.setAttribute('role', 'alert');
        toastEl.setAttribute('aria-live', 'assertive');
        toastEl.setAttribute('aria-atomic', 'true');

        const flexDiv = document.createElement('div');
        flexDiv.className = 'd-flex';

        const bodyDiv = document.createElement('div');
        bodyDiv.className = 'toast-body';
        bodyDiv.textContent = message; // An toàn chống XSS

        const btnClose = document.createElement('button');
        btnClose.type = 'button';
        btnClose.className = `btn-close ${type === 'warning' ? '' : 'btn-close-white'} me-2 m-auto`;
        btnClose.setAttribute('data-bs-dismiss', 'toast');
        btnClose.setAttribute('aria-label', 'Đóng');

        flexDiv.appendChild(bodyDiv);
        flexDiv.appendChild(btnClose);
        toastEl.appendChild(flexDiv);
        container.appendChild(toastEl);

        if (window.bootstrap && window.bootstrap.Toast) {
            const toast = new window.bootstrap.Toast(toastEl, { delay });
            toast.show();
            toastEl.addEventListener('hidden.bs.toast', () => {
                toastEl.remove();
            });
        } else {
            toastEl.style.display = 'block';
            setTimeout(() => {
                toastEl.remove();
            }, delay);
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
        const btnExtend = document.getElementById('btn-extend-table');
        const btnClose = document.getElementById('btn-close-table');
        const btnConfirmBooking = document.getElementById('btn-confirm-booking');
        const btnTransfer = document.getElementById('btn-transfer');
        const btnSplitMerge = document.getElementById('btn-split-merge');

        // 3 nút theo wireframe chưa có logic: luôn vô hiệu hóa
        if (btnConfirmBooking) btnConfirmBooking.disabled = true;
        if (btnTransfer) btnTransfer.disabled = true;
        if (btnSplitMerge) btnSplitMerge.disabled = true;

        // Nếu bất kỳ form inline nào đang hiện thì vô hiệu hóa OPEN, EXTEND và CLOSE
        if (isOpenFormVisible || isExtendFormVisible) {
            if (btnOpen) btnOpen.disabled = true;
            if (btnExtend) btnExtend.disabled = true;
            if (btnClose) btnClose.disabled = true;
            return;
        }

        // Nếu đang xử lý (mở/đóng/gia hạn) hoặc đang tải dữ liệu chi tiết thì vô hiệu hóa các nút
        if (isProcessing || isDetailLoading) {
            if (btnOpen) btnOpen.disabled = true;
            if (btnExtend) btnExtend.disabled = true;
            if (btnClose) btnClose.disabled = true;
            return;
        }

        // Tính trạng thái dựa trên bàn đang chọn
        const { status, sessionId, sessionMode } = currentSelectedTable;

        // Nút OPEN TABLE chỉ bật khi Status = "Available"
        if (btnOpen) {
            btnOpen.disabled = (status !== 'Available');
        }

        // Nút GIA HẠN chỉ bật khi Status = "InUse", có sessionId và sessionMode = "Timed"
        if (btnExtend) {
            const isTimed = (sessionMode && sessionMode.trim().toLowerCase() === 'timed');
            btnExtend.disabled = !(status === 'InUse' && sessionId && isTimed);
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
            isClockSkewCalculated = true;
        }
    }

    /**
     * Quét và bắn toast cảnh báo hết giờ (chống lặp bằng Map trong bộ nhớ)
     */
    function checkTimeAlerts(cards) {
        if (!isClockSkewCalculated) return;

        const nowServerMs = Date.now() - deltaOffset;
        const currentActiveSessionIds = new Set();

        // 1. Dọn dẹp Map: xóa các phiên không còn trong lưới hoặc đã thay đổi plannedEndAtUtc
        cards.forEach(card => {
            const plannedEndStr = card.getAttribute('data-planned-end');
            if (plannedEndStr) {
                const startStr = card.getAttribute('data-session-start');
                // Khóa nhận diện phiên: sessionId nếu có thẻ hoặc tableId kèm mốc start
                const tableId = card.getAttribute('data-table-id');
                const cardSessionKey = `${tableId}_${startStr}`;
                currentActiveSessionIds.add(cardSessionKey);

                const existingRecord = notifiedSessions.get(cardSessionKey);
                if (existingRecord && existingRecord.plannedEndAtUtc !== plannedEndStr) {
                    // plannedEndAtUtc đã thay đổi (gia hạn) -> xóa để reset chu kỳ cảnh báo
                    notifiedSessions.delete(cardSessionKey);
                }
            }
        });

        for (const [key] of notifiedSessions) {
            if (!currentActiveSessionIds.has(key)) {
                notifiedSessions.delete(key);
            }
        }

        // 2. Lần quét đầu tiên sau khi có dữ liệu thật: gộp toast nếu có bàn warning / overdue (bỏ qua pending)
        if (isInitialScan) {
            let warnCount = 0;
            let overdueCount = 0;

            cards.forEach(card => {
                const plannedEndStr = card.getAttribute('data-planned-end');
                if (!plannedEndStr) return;

                const plannedParsed = Date.parse(plannedEndStr);
                if (isNaN(plannedParsed)) return;

                const billingStartStr = card.getAttribute('data-billing-start');
                const billingStartParsed = billingStartStr ? Date.parse(billingStartStr) : null;

                const startStr = card.getAttribute('data-session-start');
                const tableId = card.getAttribute('data-table-id');
                const cardSessionKey = `${tableId}_${startStr}`;

                const timeInfo = getTimeState(plannedParsed, nowServerMs, billingStartParsed);
                notifiedSessions.set(cardSessionKey, {
                    plannedEndAtUtc: plannedEndStr,
                    alertLevel: timeInfo.state
                });

                if (timeInfo.state === 'warning') {
                    warnCount++;
                } else if (timeInfo.state === 'overdue') {
                    overdueCount++;
                }
            });

            if (warnCount > 0 && overdueCount > 0) {
                showToast(`Có ${warnCount} bàn sắp hết giờ và ${overdueCount} bàn đã hết giờ.`, 'danger', 6000);
            } else if (warnCount > 0) {
                showToast(`Có ${warnCount} bàn sắp hết giờ.`, 'warning', 6000);
            } else if (overdueCount > 0) {
                showToast(`Có ${overdueCount} bàn đã hết giờ.`, 'danger', 6000);
            }

            isInitialScan = false;
            return;
        }

        // 3. Các lần quét định kỳ tiếp theo: bắn toast 1 lần khi chuyển mức
        cards.forEach(card => {
            const plannedEndStr = card.getAttribute('data-planned-end');
            if (!plannedEndStr) return;

            const plannedParsed = Date.parse(plannedEndStr);
            if (isNaN(plannedParsed)) return;

            const billingStartStr = card.getAttribute('data-billing-start');
            const billingStartParsed = billingStartStr ? Date.parse(billingStartStr) : null;

            const startStr = card.getAttribute('data-session-start');
            const tableId = card.getAttribute('data-table-id');
            const tableCode = card.getAttribute('data-table-code') || '';
            const cardSessionKey = `${tableId}_${startStr}`;

            const timeInfo = getTimeState(plannedParsed, nowServerMs, billingStartParsed);
            const prev = notifiedSessions.get(cardSessionKey);

            if (!prev) {
                notifiedSessions.set(cardSessionKey, {
                    plannedEndAtUtc: plannedEndStr,
                    alertLevel: timeInfo.state
                });

                if (timeInfo.state === 'warning') {
                    showToast(`Bàn ${tableCode} sắp hết giờ (còn ${timeInfo.formattedTime})`, 'warning');
                } else if (timeInfo.state === 'overdue') {
                    showToast(`Bàn ${tableCode} đã hết giờ. Hãy gia hạn hoặc đóng bàn.`, 'danger');
                }
            } else {
                if (prev.alertLevel !== timeInfo.state) {
                    if (timeInfo.state === 'warning' && prev.alertLevel !== 'warning') {
                        prev.alertLevel = 'warning';
                        showToast(`Bàn ${tableCode} sắp hết giờ (còn ${timeInfo.formattedTime})`, 'warning');
                    } else if (timeInfo.state === 'overdue' && prev.alertLevel !== 'overdue') {
                        prev.alertLevel = 'overdue';
                        showToast(`Bàn ${tableCode} đã hết giờ. Hãy gia hạn hoặc đóng bàn.`, 'danger');
                    } else {
                        prev.alertLevel = timeInfo.state;
                    }
                }
            }
        });
    }

    /**
     * Cập nhật đồng hồ mỗi giây (bộ đếm giây duy nhất):
     * - Thời gian chơi trên thẻ (.table-clock)
     * - Thời gian còn lại và trạng thái thẻ (.table-remaining, data-time-state)
     * - Dòng Duration và Còn lại của panel chi tiết
     * - Xem trước trên form mở bàn và form gia hạn
     * - Quét cảnh báo hết giờ
     */
    function updateClocks() {
        const nowServerMs = Date.now() - deltaOffset;
        const cards = document.querySelectorAll('#table-grid-container .table-card');

        cards.forEach(card => {
            const status = card.getAttribute('data-status') || '';
            const displayStatus = card.getAttribute('data-display-status') || '';
            const clockEl = card.querySelector('.table-clock');
            const statusTextEl = card.querySelector('.status-text');
            const billingStartStr = card.getAttribute('data-billing-start');
            const sessionStartStr = card.getAttribute('data-session-start');

            // 1. Đồng hồ thời gian thực chơi & nhãn trạng thái giờ tính tiền
            if (status === 'InUse') {
                if (billingStartStr) {
                    const billingStartMs = Date.parse(billingStartStr);
                    if (isNaN(billingStartMs)) {
                        if (clockEl) clockEl.textContent = '--';
                        if (statusTextEl) statusTextEl.textContent = displayStatus || 'Đang chơi';
                        card.removeAttribute('data-billing-phase');
                    } else {
                        const phaseInfo = getBillingPhase(billingStartMs, nowServerMs);
                        if (phaseInfo.phase === 'pending') {
                            if (statusTextEl) statusTextEl.textContent = 'Chờ tính giờ';
                            if (clockEl) clockEl.textContent = `Tính giờ sau ${formatSecondsToStart(phaseInfo.secondsToStart)}`;
                            card.setAttribute('data-billing-phase', 'pending');
                        } else {
                            if (statusTextEl) statusTextEl.textContent = displayStatus || 'Đang chơi';
                            const totalSeconds = Math.floor(phaseInfo.elapsedMs / 1000);
                            if (clockEl) clockEl.textContent = formatElapsedSeconds(totalSeconds);
                            card.setAttribute('data-billing-phase', 'running');
                        }
                    }
                } else if (sessionStartStr) {
                    const startParsed = Date.parse(sessionStartStr);
                    if (isNaN(startParsed)) {
                        if (clockEl) clockEl.textContent = '--';
                    } else {
                        const diffMs = Math.max(0, nowServerMs - startParsed);
                        const totalSeconds = Math.floor(diffMs / 1000);
                        if (clockEl) clockEl.textContent = formatElapsedSeconds(totalSeconds);
                    }
                    if (statusTextEl) statusTextEl.textContent = displayStatus || 'Đang chơi';
                    card.removeAttribute('data-billing-phase');
                } else {
                    if (clockEl) clockEl.textContent = '--';
                    if (statusTextEl) statusTextEl.textContent = displayStatus || 'Đang chơi';
                    card.removeAttribute('data-billing-phase');
                }
            } else {
                if (clockEl) clockEl.textContent = '--';
                if (statusTextEl) statusTextEl.textContent = displayStatus;
                card.removeAttribute('data-billing-phase');
            }

            // 2. Dòng thời gian còn lại & trạng thái cảnh báo/quá hạn
            const plannedEnd = card.getAttribute('data-planned-end');
            const remainingEl = card.querySelector('.table-remaining');

            if (plannedEnd && remainingEl) {
                const plannedParsed = Date.parse(plannedEnd);
                const billingStartParsed = billingStartStr ? Date.parse(billingStartStr) : null;
                const timeInfo = getTimeState(plannedParsed, nowServerMs, billingStartParsed);

                remainingEl.textContent = timeInfo.text;
                if (timeInfo.state === 'normal' || timeInfo.state === 'warning' || timeInfo.state === 'overdue') {
                    card.setAttribute('data-time-state', timeInfo.state);
                } else {
                    card.removeAttribute('data-time-state');
                }
            } else {
                if (remainingEl) remainingEl.textContent = '--';
                card.removeAttribute('data-time-state');
            }
        });

        // 3. Panel chi tiết phải
        const panel = document.getElementById('table-detail-panel');
        const isPanelVisible = panel && !panel.classList.contains('d-none');

        if (isPanelVisible && currentSelectedTable.id) {
            // Cập nhật Status text trên panel
            const elStatus = document.getElementById('detail-status');
            if (elStatus) {
                if (currentSelectedTable.status === 'InUse' && currentSelectedTable.billingStartAtUtc) {
                    const bStartMs = Date.parse(currentSelectedTable.billingStartAtUtc);
                    if (!isNaN(bStartMs)) {
                        const phaseInfo = getBillingPhase(bStartMs, nowServerMs);
                        if (phaseInfo.phase === 'pending') {
                            elStatus.textContent = 'Chờ tính giờ';
                        } else {
                            elStatus.textContent = currentSelectedTable.displayStatus || 'Đang chơi';
                        }
                    } else {
                        elStatus.textContent = currentSelectedTable.displayStatus || '--';
                    }
                } else {
                    elStatus.textContent = currentSelectedTable.displayStatus || '--';
                }
            }

            // Cập nhật Duration trên panel
            const elDuration = document.getElementById('detail-duration');
            if (elDuration) {
                if (currentSelectedTable.status === 'InUse') {
                    if (currentSelectedTable.billingStartAtUtc) {
                        const bStartMs = Date.parse(currentSelectedTable.billingStartAtUtc);
                        if (isNaN(bStartMs)) {
                            elDuration.textContent = '--';
                        } else {
                            const phaseInfo = getBillingPhase(bStartMs, nowServerMs);
                            if (phaseInfo.phase === 'pending') {
                                elDuration.textContent = `Chưa tính (còn ${formatSecondsToStart(phaseInfo.secondsToStart)})`;
                            } else {
                                const totalSeconds = Math.floor(phaseInfo.elapsedMs / 1000);
                                elDuration.textContent = formatElapsedSeconds(totalSeconds);
                            }
                        }
                    } else if (currentSelectedTable.startAtUtc) {
                        const startParsed = Date.parse(currentSelectedTable.startAtUtc);
                        if (isNaN(startParsed)) {
                            elDuration.textContent = '--';
                        } else {
                            const diffMs = Math.max(0, nowServerMs - startParsed);
                            const totalSeconds = Math.floor(diffMs / 1000);
                            elDuration.textContent = formatElapsedSeconds(totalSeconds);
                        }
                    } else {
                        elDuration.textContent = '--';
                    }
                } else if (currentSelectedTable.status === 'AwaitingPayment') {
                    if (currentSelectedTable.billingStartAtUtc && currentSelectedTable.billingEndAtUtc) {
                        elDuration.textContent = formatDuration(currentSelectedTable.billingStartAtUtc, currentSelectedTable.billingEndAtUtc);
                    } else if (currentSelectedTable.startAtUtc && currentSelectedTable.endAtUtc) {
                        elDuration.textContent = formatDuration(currentSelectedTable.startAtUtc, currentSelectedTable.endAtUtc);
                    } else {
                        elDuration.textContent = '--';
                    }
                } else if (currentSelectedTable.startAtUtc && currentSelectedTable.endAtUtc) {
                    elDuration.textContent = formatDuration(currentSelectedTable.startAtUtc, currentSelectedTable.endAtUtc);
                } else {
                    elDuration.textContent = '--';
                }
            }

            // Cập nhật dòng Còn lại
            const elRemaining = document.getElementById('detail-remaining');
            if (elRemaining) {
                const isTimed = (currentSelectedTable.sessionMode && currentSelectedTable.sessionMode.trim().toLowerCase() === 'timed');
                if (isTimed && currentSelectedTable.plannedEndAtUtc && !currentSelectedTable.endAtUtc) {
                    if (!currentSelectedTable.billingStartAtUtc) {
                        elRemaining.textContent = '--';
                    } else {
                        const plannedParsed = Date.parse(currentSelectedTable.plannedEndAtUtc);
                        const billingStartParsed = Date.parse(currentSelectedTable.billingStartAtUtc);
                        const timeInfo = getTimeState(plannedParsed, nowServerMs, billingStartParsed);
                        elRemaining.textContent = timeInfo.panelText;
                    }
                } else {
                    elRemaining.textContent = '--';
                }
            }
        }

        // 4. Cập nhật xem trước form mở bàn
        if (isOpenFormVisible) {
            updateOpenFormPreview();
        }

        // 5. Cập nhật xem trước form gia hạn
        if (isExtendFormVisible) {
            updateExtendFormPreview();
        }

        // 6. Quét cảnh báo thời gian
        checkTimeAlerts(cards);
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
        currentSelectedTable.sessionMode = detail.sessionMode ?? detail.SessionMode ?? null;

        const startUtc = detail.startAtUtc ?? detail.StartAtUtc ?? null;
        const endUtc = detail.endAtUtc ?? detail.EndAtUtc ?? null;
        const plannedEndUtc = detail.plannedEndAtUtc ?? detail.PlannedEndAtUtc ?? null;
        const billingStartUtc = detail.billingStartAtUtc ?? detail.BillingStartAtUtc ?? null;
        const billingEndUtc = detail.billingEndAtUtc ?? detail.BillingEndAtUtc ?? null;

        currentSelectedTable.startAtUtc = startUtc;
        currentSelectedTable.endAtUtc = endUtc;
        currentSelectedTable.plannedEndAtUtc = plannedEndUtc;
        currentSelectedTable.billingStartAtUtc = billingStartUtc;
        currentSelectedTable.billingEndAtUtc = billingEndUtc;

        // Lưu trữ cardStatus, cardSessionStart, cardPlannedEnd, cardBillingStart từ DOM thẻ bàn
        const card = document.querySelector(`.table-card[data-table-id="${currentSelectedTable.id}"]`);
        if (card) {
            currentSelectedTable.cardStatus = card.getAttribute('data-status') || '';
            currentSelectedTable.cardSessionStart = card.getAttribute('data-session-start') || '';
            currentSelectedTable.cardPlannedEnd = card.getAttribute('data-planned-end') || '';
            currentSelectedTable.cardBillingStart = card.getAttribute('data-billing-start') || '';
        } else {
            currentSelectedTable.cardStatus = currentSelectedTable.status;
            currentSelectedTable.cardSessionStart = '';
            currentSelectedTable.cardPlannedEnd = '';
            currentSelectedTable.cardBillingStart = '';
        }

        // Điền các phần tử giao diện bằng textContent (An toàn XSS)
        const elTableName = document.getElementById('detail-table-name');
        const elStatus = document.getElementById('detail-status');
        const elCustomer = document.getElementById('detail-customer');
        const elBookingTime = document.getElementById('detail-booking-time');
        const elSessionMode = document.getElementById('detail-session-mode');
        const elStartTime = document.getElementById('detail-start-time');
        const elEndTime = document.getElementById('detail-end-time');
        const elBillingStart = document.getElementById('detail-billing-start');
        const elPlannedEnd = document.getElementById('detail-planned-end');
        const elDuration = document.getElementById('detail-duration');
        const elRemaining = document.getElementById('detail-remaining');

        if (elTableName) elTableName.textContent = currentSelectedTable.code || '--';

        const customerName = detail.customerFullName ?? detail.CustomerFullName;
        if (elCustomer) elCustomer.textContent = customerName ? customerName : '--';
        if (elBookingTime) elBookingTime.textContent = '--';

        const sessionMode = detail.sessionMode ?? detail.SessionMode;
        if (elSessionMode) {
            if (sessionMode === 'Open') {
                elSessionMode.textContent = 'Không giới hạn';
            } else if (sessionMode === 'Timed') {
                elSessionMode.textContent = 'Đăng ký thời gian';
            } else {
                elSessionMode.textContent = sessionMode || '--';
            }
        }

        if (elStartTime) {
            elStartTime.textContent = startUtc ? formatVnDateTime(startUtc) : '--';
        }
        if (elEndTime) {
            elEndTime.textContent = endUtc ? formatVnDateTime(endUtc) : '--';
        }
        if (elBillingStart) {
            elBillingStart.textContent = billingStartUtc ? formatVnDateTime(billingStartUtc) : '--';
        }
        if (elPlannedEnd) {
            elPlannedEnd.textContent = plannedEndUtc ? formatVnDateTime(plannedEndUtc) : '--';
        }

        // Cập nhật Status & Duration & Remaining theo thời gian thực
        const nowServerMs = Date.now() - deltaOffset;

        if (elStatus) {
            if (currentSelectedTable.status === 'InUse' && billingStartUtc) {
                const bStartMs = Date.parse(billingStartUtc);
                if (!isNaN(bStartMs)) {
                    const phaseInfo = getBillingPhase(bStartMs, nowServerMs);
                    if (phaseInfo.phase === 'pending') {
                        elStatus.textContent = 'Chờ tính giờ';
                    } else {
                        elStatus.textContent = currentSelectedTable.displayStatus || 'Đang chơi';
                    }
                } else {
                    elStatus.textContent = currentSelectedTable.displayStatus || '--';
                }
            } else {
                elStatus.textContent = currentSelectedTable.displayStatus || '--';
            }
        }

        if (elDuration) {
            if (currentSelectedTable.status === 'InUse') {
                if (billingStartUtc) {
                    const bStartMs = Date.parse(billingStartUtc);
                    if (isNaN(bStartMs)) {
                        elDuration.textContent = '--';
                    } else {
                        const phaseInfo = getBillingPhase(bStartMs, nowServerMs);
                        if (phaseInfo.phase === 'pending') {
                            elDuration.textContent = `Chưa tính (còn ${formatSecondsToStart(phaseInfo.secondsToStart)})`;
                        } else {
                            const totalSeconds = Math.floor(phaseInfo.elapsedMs / 1000);
                            elDuration.textContent = formatElapsedSeconds(totalSeconds);
                        }
                    }
                } else if (startUtc) {
                    const startParsed = Date.parse(startUtc);
                    if (isNaN(startParsed)) {
                        elDuration.textContent = '--';
                    } else {
                        const diffMs = Math.max(0, nowServerMs - startParsed);
                        const totalSeconds = Math.floor(diffMs / 1000);
                        elDuration.textContent = formatElapsedSeconds(totalSeconds);
                    }
                } else {
                    elDuration.textContent = '--';
                }
            } else if (currentSelectedTable.status === 'AwaitingPayment') {
                if (billingStartUtc && billingEndUtc) {
                    elDuration.textContent = formatDuration(billingStartUtc, billingEndUtc);
                } else if (startUtc && endUtc) {
                    elDuration.textContent = formatDuration(startUtc, endUtc);
                } else {
                    elDuration.textContent = '--';
                }
            } else if (startUtc && endUtc) {
                elDuration.textContent = formatDuration(startUtc, endUtc);
            } else {
                elDuration.textContent = '--';
            }
        }

        if (elRemaining) {
            const isTimed = (sessionMode && sessionMode.trim().toLowerCase() === 'timed');
            if (isTimed && plannedEndUtc && !endUtc) {
                if (!billingStartUtc) {
                    elRemaining.textContent = '--';
                } else {
                    const plannedParsed = Date.parse(plannedEndUtc);
                    const billingStartParsed = Date.parse(billingStartUtc);
                    const timeInfo = getTimeState(plannedParsed, nowServerMs, billingStartParsed);
                    elRemaining.textContent = timeInfo.panelText;
                }
            } else {
                elRemaining.textContent = '--';
            }
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
        const elSessionMode = document.getElementById('detail-session-mode');
        const elStartTime = document.getElementById('detail-start-time');
        const elEndTime = document.getElementById('detail-end-time');
        const elBillingStart = document.getElementById('detail-billing-start');
        const elPlannedEnd = document.getElementById('detail-planned-end');
        const elDuration = document.getElementById('detail-duration');
        const elRemaining = document.getElementById('detail-remaining');

        if (elTableName) elTableName.textContent = '--';
        if (elStatus) elStatus.textContent = '--';
        if (elCustomer) elCustomer.textContent = '--';
        if (elBookingTime) elBookingTime.textContent = '--';
        if (elSessionMode) elSessionMode.textContent = '--';
        if (elStartTime) elStartTime.textContent = '--';
        if (elEndTime) elEndTime.textContent = '--';
        if (elBillingStart) elBillingStart.textContent = '--';
        if (elPlannedEnd) elPlannedEnd.textContent = '--';
        if (elDuration) elDuration.textContent = '--';
        if (elRemaining) elRemaining.textContent = '--';

        const elPlaytimeRow = document.getElementById('detail-playtime-row');
        const elPlaytimeAmount = document.getElementById('detail-playtime-amount');
        if (elPlaytimeRow) elPlaytimeRow.classList.add('d-none');
        if (elPlaytimeAmount) elPlaytimeAmount.textContent = '--';
    }

    // =========================================================================
    // Đóng panel chi tiết (✕ hoặc Esc hoặc khi bàn biến mất)
    // =========================================================================
    function closeDetailPanel(returnFocus = true) {
        if (isOpenFormVisible) {
            hideOpenForm();
        }
        if (isExtendFormVisible) {
            hideExtendForm(false);
        }

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
            sessionMode: null,
            startAtUtc: null,
            endAtUtc: null,
            plannedEndAtUtc: null,
            billingStartAtUtc: null,
            billingEndAtUtc: null,
            cardStatus: '',
            cardSessionStart: '',
            cardBillingStart: '',
            cardPlannedEnd: ''
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
        if (isOpenFormVisible) {
            hideOpenForm();
        }
        if (isExtendFormVisible) {
            hideExtendForm(false);
        }
        closeDetailPanel(false);
    }

    // =========================================================================
    // Xử lý khi click / phím chọn bàn (handleTableSelect)
    // =========================================================================
    async function handleTableSelect(tableId) {
        if (!tableId) return;

        if (isOpenFormVisible) {
            hideOpenForm();
        }
        if (isExtendFormVisible) {
            hideExtendForm(false);
        }

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
        const elRemaining = document.getElementById('detail-remaining');

        if (elTableName) elTableName.textContent = cardCode || 'Đang tải...';
        if (elStatus) elStatus.textContent = 'Đang tải...';
        if (elCustomer) elCustomer.textContent = 'Đang tải...';
        if (elBookingTime) elBookingTime.textContent = 'Đang tải...';
        if (elStartTime) elStartTime.textContent = 'Đang tải...';
        if (elEndTime) elEndTime.textContent = 'Đang tải...';
        if (elDuration) elDuration.textContent = 'Đang tải...';
        if (elRemaining) elRemaining.textContent = 'Đang tải...';

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
        const prevCardBillingStart = currentSelectedTable.cardBillingStart;
        const prevCardPlannedEnd = currentSelectedTable.cardPlannedEnd;

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
            // Bàn đang chọn không còn trên lưới -> ẩn panel và đóng form
            if (isOpenFormVisible) {
                hideOpenForm();
                showToast(`Bàn ${currentSelectedTable.code} không còn ở trạng thái Trống.`, 'danger');
            }
            if (isExtendFormVisible) {
                hideExtendForm(false);
                showToast(`Bàn ${currentSelectedTable.code} không còn tồn tại.`, 'danger');
            }
            resetDetailPanel();
            return;
        }

        // Gán lại class selected cho thẻ bàn
        card.classList.add('selected');

        const newCardStatus = card.getAttribute('data-status') || '';
        const newCardSessionStart = card.getAttribute('data-session-start') || '';
        const newCardBillingStart = card.getAttribute('data-billing-start') || '';
        const newCardPlannedEnd = card.getAttribute('data-planned-end') || '';
        const newCardMode = card.getAttribute('data-session-mode') || '';

        // Nếu form mở bàn đang mở mà trạng thái bàn chuyển sang không còn là Available
        if (isOpenFormVisible && newCardStatus !== 'Available') {
            hideOpenForm();
            showToast(`Bàn ${currentSelectedTable.code} không còn ở trạng thái Trống.`, 'danger');
        }

        // Nếu form gia hạn đang mở mà bàn không còn InUse hoặc không còn Timed
        if (isExtendFormVisible && (newCardStatus !== 'InUse' || newCardMode.toLowerCase() !== 'timed')) {
            hideExtendForm(false);
            showToast(`Bàn ${currentSelectedTable.code} không còn ở trạng thái đang chơi theo thời gian.`, 'danger');
        }

        // Nếu plannedEnd thay đổi (người khác vừa gia hạn) trong lúc form gia hạn đang mở
        if (isExtendFormVisible && newCardPlannedEnd !== prevCardPlannedEnd) {
            currentSelectedTable.plannedEndAtUtc = newCardPlannedEnd;
            currentSelectedTable.cardPlannedEnd = newCardPlannedEnd;
            updateExtendFormPreview();
        }

        // Chỉ gọi lại GetTableDetail khi data-status hoặc data-session-start hoặc data-billing-start hoặc data-planned-end đổi
        if (newCardStatus !== prevCardStatus || newCardSessionStart !== prevCardSessionStart || newCardBillingStart !== prevCardBillingStart || newCardPlannedEnd !== prevCardPlannedEnd) {
            if (isOpenFormVisible && newCardStatus === 'Available') {
                currentSelectedTable.cardStatus = newCardStatus;
                currentSelectedTable.cardSessionStart = newCardSessionStart;
                currentSelectedTable.cardBillingStart = newCardBillingStart;
                currentSelectedTable.cardPlannedEnd = newCardPlannedEnd;
                updateButtons();
            } else if (isExtendFormVisible && newCardStatus === 'InUse' && newCardMode.toLowerCase() === 'timed') {
                currentSelectedTable.cardStatus = newCardStatus;
                currentSelectedTable.cardSessionStart = newCardSessionStart;
                currentSelectedTable.cardBillingStart = newCardBillingStart;
                currentSelectedTable.cardPlannedEnd = newCardPlannedEnd;
                updateButtons();
            } else {
                await handleTableSelect(selectedId);
            }
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
    // Tải lại lưới bàn sau thao tác (Open/Close/Extend)
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
    // Quản lý Form MỞ BÀN (Open Session Form)
    // =========================================================================
    function showOpenForm() {
        if (currentSelectedTable.status !== 'Available') return;

        if (isExtendFormVisible) {
            hideExtendForm(false);
        }

        isOpenFormVisible = true;
        openFormState.mode = 'Open';
        openFormState.minutes = 60;

        const formEl = document.getElementById('open-session-form');
        const actionsBox = document.getElementById('action-buttons-box');
        if (formEl) formEl.classList.remove('d-none');
        if (actionsBox) actionsBox.classList.add('d-none');

        // Reset radio buttons
        const radioOpen = document.getElementById('radio-mode-open');
        if (radioOpen) radioOpen.checked = true;

        // Reset timed options container
        const timedContainer = document.getElementById('timed-options-container');
        if (timedContainer) timedContainer.classList.add('d-none');

        // Reset ô nhập
        const inputMinutes = document.getElementById('input-planned-minutes');
        if (inputMinutes) {
            inputMinutes.value = '60';
            inputMinutes.classList.remove('is-invalid');
        }

        // Đánh dấu nút 60 phút
        const presetButtons = document.querySelectorAll('.btn-preset-minutes');
        presetButtons.forEach(btn => {
            if (btn.getAttribute('data-minutes') === '60') {
                btn.classList.add('active');
            } else {
                btn.classList.remove('active');
            }
        });

        const errorEl = document.getElementById('open-form-error');
        if (errorEl) {
            errorEl.textContent = '';
            errorEl.classList.add('d-none');
        }

        updateOpenFormPreview();
        validateOpenForm();
        updateButtons();

        if (formEl && typeof formEl.scrollIntoView === 'function') {
            formEl.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    }

    function hideOpenForm(returnFocus = true) {
        isOpenFormVisible = false;
        const formEl = document.getElementById('open-session-form');
        const actionsBox = document.getElementById('action-buttons-box');
        if (formEl) formEl.classList.add('d-none');
        if (actionsBox) actionsBox.classList.remove('d-none');

        updateButtons();

        if (returnFocus) {
            const btnOpen = document.getElementById('btn-open-table');
            if (btnOpen && !btnOpen.disabled && typeof btnOpen.focus === 'function') {
                btnOpen.focus();
            }
        }
    }

    function validateOpenForm() {
        const btnConfirm = document.getElementById('btn-confirm-open');
        const errorEl = document.getElementById('open-form-error');
        const inputMinutes = document.getElementById('input-planned-minutes');

        if (!btnConfirm) return;

        if (isProcessing) {
            btnConfirm.disabled = true;
            return;
        }

        if (openFormState.mode === 'Open') {
            btnConfirm.disabled = false;
            if (errorEl) {
                errorEl.textContent = '';
                errorEl.classList.add('d-none');
            }
            if (inputMinutes) inputMinutes.classList.remove('is-invalid');
            return;
        }

        const rawValue = inputMinutes ? inputMinutes.value.trim() : '';
        if (!rawValue || !/^\d+$/.test(rawValue)) {
            btnConfirm.disabled = true;
            if (errorEl) {
                errorEl.textContent = 'Vui lòng nhập số phút hợp lệ (bội số của 15, từ 15 đến 720).';
                errorEl.classList.remove('d-none');
            }
            if (inputMinutes) inputMinutes.classList.add('is-invalid');
            return;
        }

        const num = Number(rawValue);
        if (!Number.isInteger(num) || num < 15 || num > 720 || num % 15 !== 0) {
            btnConfirm.disabled = true;
            if (errorEl) {
                errorEl.textContent = 'Thời gian đăng ký phải là bội số của 15 phút, từ 15 đến 720 phút.';
                errorEl.classList.remove('d-none');
            }
            if (inputMinutes) inputMinutes.classList.add('is-invalid');
            return;
        }

        openFormState.minutes = num;
        btnConfirm.disabled = false;
        if (errorEl) {
            errorEl.textContent = '';
            errorEl.classList.add('d-none');
        }
        if (inputMinutes) inputMinutes.classList.remove('is-invalid');
    }

    function updateOpenFormPreview() {
        if (!isOpenFormVisible) return;

        const previewBillingEl = document.getElementById('preview-billing-start');
        const previewPlannedEndEl = document.getElementById('preview-planned-end');
        const previewPlannedEndRow = document.getElementById('preview-planned-end-row');

        const nowServerMs = Date.now() - deltaOffset;
        const ceil15Ms = Math.ceil(nowServerMs / 900000) * 900000;
        const billingStartStr = formatVnTimeOnly(ceil15Ms);

        if (previewBillingEl) {
            previewBillingEl.textContent = billingStartStr;
        }

        if (openFormState.mode === 'Timed') {
            const inputMinutes = document.getElementById('input-planned-minutes');
            const rawValue = inputMinutes ? inputMinutes.value.trim() : '';
            const num = Number(rawValue);

            if (Number.isInteger(num) && num >= 15 && num <= 720 && num % 15 === 0) {
                // Dự kiến kết thúc = Giờ tính tiền (làm tròn lên 15 phút) + số phút đăng ký
                const plannedEndMs = ceil15Ms + num * 60000;
                const plannedEndStr = formatVnTimeOnly(plannedEndMs);
                if (previewPlannedEndEl) {
                    previewPlannedEndEl.textContent = plannedEndStr;
                }
                if (previewPlannedEndRow) {
                    previewPlannedEndRow.classList.remove('d-none');
                }
            } else {
                if (previewPlannedEndEl) {
                    previewPlannedEndEl.textContent = '--:--';
                }
                if (previewPlannedEndRow) {
                    previewPlannedEndRow.classList.remove('d-none');
                }
            }
        } else {
            if (previewPlannedEndRow) {
                previewPlannedEndRow.classList.add('d-none');
            }
        }
    }

    // =========================================================================
    // Thao tác MỞ BÀN (OpenSession)
    // =========================================================================
    function handleOpenTable() {
        if (isProcessing) return;
        if (!currentSelectedTable.id || currentSelectedTable.status !== 'Available') return;

        showOpenForm();
    }

    async function handleConfirmOpenSession() {
        if (isProcessing) return;
        if (!currentSelectedTable.id || currentSelectedTable.status !== 'Available') return;

        const token = getVerificationToken();
        if (!token) {
            showToast('Thiếu mã xác thực bảo mật (Anti-forgery token).', 'danger');
            return;
        }

        const mode = openFormState.mode;
        let plannedMinutes = null;

        if (mode === 'Timed') {
            const inputMinutes = document.getElementById('input-planned-minutes');
            const rawValue = inputMinutes ? inputMinutes.value.trim() : '';
            const num = Number(rawValue);
            if (!Number.isInteger(num) || num < 15 || num > 720 || num % 15 !== 0) {
                validateOpenForm();
                return;
            }
            plannedMinutes = num;
        }

        if (pollingAbortController) {
            pollingAbortController.abort();
            pollingAbortController = null;
        }
        pollingRequestId++;
        isPolling = false;

        isProcessing = true;
        updateButtons();
        validateOpenForm();

        const targetTableId = currentSelectedTable.id;
        const targetTableCode = currentSelectedTable.code;

        try {
            const params = new URLSearchParams();
            params.append('TableId', targetTableId);
            params.append('SessionMode', mode);
            if (mode === 'Timed' && plannedMinutes !== null) {
                params.append('PlannedMinutes', plannedMinutes);
            }
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
                hideOpenForm(false);

                const data = result.data ?? result.Data;
                const billingStartUtc = data?.billingStartAtUtc ?? data?.BillingStartAtUtc;
                const plannedEndUtc = data?.plannedEndAtUtc ?? data?.PlannedEndAtUtc;
                const respMode = data?.sessionMode ?? data?.SessionMode ?? mode;

                const formattedBillingStart = billingStartUtc ? formatVnTimeOnly(billingStartUtc) : '--:--';
                let successMsg = `Đã mở bàn ${targetTableCode}. Tính giờ từ ${formattedBillingStart}`;
                if (respMode === 'Timed' && plannedEndUtc) {
                    const formattedPlannedEnd = formatVnTimeOnly(plannedEndUtc);
                    successMsg += ` - Dự kiến kết thúc ${formattedPlannedEnd}`;
                }

                showToast(successMsg, 'success');
                await reloadTableGrid();
                await handleTableSelect(targetTableId);
            } else {
                const errorMsg = serverMessage || 'Thao tác không thành công. Vui lòng thử lại.';
                showToast(errorMsg, 'danger');
                if (autoReload) {
                    hideOpenForm(false);
                    await reloadTableGrid();
                }
            }
        } catch {
            showToast('Không kết nối được máy chủ.', 'danger');
        } finally {
            isProcessing = false;
            validateOpenForm();
            updateButtons();
        }
    }

    // =========================================================================
    // Quản lý Form GIA HẠN PHIÊN (Extend Session Form)
    // =========================================================================
    function showExtendForm() {
        const isTimed = (currentSelectedTable.sessionMode && currentSelectedTable.sessionMode.trim().toLowerCase() === 'timed');
        if (currentSelectedTable.status !== 'InUse' || !currentSelectedTable.sessionId || !isTimed) return;

        if (isOpenFormVisible) {
            hideOpenForm(false);
        }

        isExtendFormVisible = true;
        extendFormState.minutes = 30;

        const formEl = document.getElementById('extend-session-form');
        const actionsBox = document.getElementById('action-buttons-box');
        if (formEl) formEl.classList.remove('d-none');
        if (actionsBox) actionsBox.classList.add('d-none');

        // Reset preset buttons: chọn 30 phút mặc định
        const presetButtons = document.querySelectorAll('.btn-preset-extend-minutes');
        presetButtons.forEach(btn => {
            if (btn.getAttribute('data-minutes') === '30') {
                btn.classList.add('active');
            } else {
                btn.classList.remove('active');
            }
        });

        // Reset ô nhập số phút
        const inputMinutes = document.getElementById('input-extend-minutes');
        if (inputMinutes) {
            inputMinutes.value = '30';
            inputMinutes.classList.remove('is-invalid');
        }

        const errorEl = document.getElementById('extend-form-error');
        if (errorEl) {
            errorEl.textContent = '';
            errorEl.classList.add('d-none');
        }

        updateExtendFormPreview();
        validateExtendForm();
        updateButtons();

        // Focus vào ô nhập số phút
        if (inputMinutes && typeof inputMinutes.focus === 'function') {
            inputMinutes.focus();
            inputMinutes.select();
        } else if (formEl && typeof formEl.scrollIntoView === 'function') {
            formEl.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    }

    function hideExtendForm(returnFocus = true) {
        isExtendFormVisible = false;
        const formEl = document.getElementById('extend-session-form');
        const actionsBox = document.getElementById('action-buttons-box');
        if (formEl) formEl.classList.add('d-none');
        if (actionsBox) actionsBox.classList.remove('d-none');

        updateButtons();

        if (returnFocus) {
            const btnExtend = document.getElementById('btn-extend-table');
            if (btnExtend && !btnExtend.disabled && typeof btnExtend.focus === 'function') {
                btnExtend.focus();
            }
        }
    }

    function validateExtendForm() {
        const btnConfirm = document.getElementById('btn-confirm-extend');
        const errorEl = document.getElementById('extend-form-error');
        const inputMinutes = document.getElementById('input-extend-minutes');

        if (!btnConfirm) return;

        if (isProcessing) {
            btnConfirm.disabled = true;
            return;
        }

        const rawValue = inputMinutes ? inputMinutes.value.trim() : '';
        if (!rawValue || !/^\d+$/.test(rawValue)) {
            btnConfirm.disabled = true;
            if (errorEl) {
                errorEl.textContent = 'Thời gian gia hạn phải là bội số của 15 phút, từ 15 đến 240 phút.';
                errorEl.classList.remove('d-none');
            }
            if (inputMinutes) inputMinutes.classList.add('is-invalid');
            return;
        }

        const num = Number(rawValue);
        if (!Number.isInteger(num) || num < 15 || num > 240 || num % 15 !== 0) {
            btnConfirm.disabled = true;
            if (errorEl) {
                errorEl.textContent = 'Thời gian gia hạn phải là bội số của 15 phút, từ 15 đến 240 phút.';
                errorEl.classList.remove('d-none');
            }
            if (inputMinutes) inputMinutes.classList.add('is-invalid');
            return;
        }

        extendFormState.minutes = num;
        btnConfirm.disabled = false;
        if (errorEl) {
            errorEl.textContent = '';
            errorEl.classList.add('d-none');
        }
        if (inputMinutes) inputMinutes.classList.remove('is-invalid');
    }

    function updateExtendFormPreview() {
        if (!isExtendFormVisible) return;

        const previewPlannedEndEl = document.getElementById('preview-extend-planned-end');
        const previewRemainingEl = document.getElementById('preview-extend-remaining');

        const plannedEndStr = currentSelectedTable.plannedEndAtUtc;
        if (!plannedEndStr) {
            if (previewPlannedEndEl) previewPlannedEndEl.textContent = '--:--';
            if (previewRemainingEl) previewRemainingEl.textContent = '--';
            return;
        }

        const plannedMs = Date.parse(plannedEndStr);
        if (isNaN(plannedMs)) {
            if (previewPlannedEndEl) previewPlannedEndEl.textContent = '--:--';
            if (previewRemainingEl) previewRemainingEl.textContent = '--';
            return;
        }

        const inputMinutes = document.getElementById('input-extend-minutes');
        const rawValue = inputMinutes ? inputMinutes.value.trim() : '';
        const num = Number(rawValue);

        if (Number.isInteger(num) && num >= 15 && num <= 240 && num % 15 === 0) {
            const newPlannedMs = plannedMs + num * 60000;
            const newPlannedStr = formatVnTimeOnly(newPlannedMs);

            if (previewPlannedEndEl) {
                previewPlannedEndEl.textContent = newPlannedStr;
            }

            const nowServerMs = Date.now() - deltaOffset;
            const billingStartMs = currentSelectedTable.billingStartAtUtc
                ? Date.parse(currentSelectedTable.billingStartAtUtc)
                : null;
            const timeInfoAfter = getTimeState(newPlannedMs, nowServerMs, billingStartMs);

            if (previewRemainingEl) {
                let text = timeInfoAfter.text;
                if (timeInfoAfter.state === 'overdue') {
                    text += ' (vẫn quá hạn)';
                }
                previewRemainingEl.textContent = text;
            }
        } else {
            if (previewPlannedEndEl) previewPlannedEndEl.textContent = '--:--';
            if (previewRemainingEl) previewRemainingEl.textContent = '--';
        }
    }

    // =========================================================================
    // Thao tác GIA HẠN PHIÊN (ExtendSession)
    // =========================================================================
    function handleExtendTable() {
        if (isProcessing) return;
        const isTimed = (currentSelectedTable.sessionMode && currentSelectedTable.sessionMode.trim().toLowerCase() === 'timed');
        if (currentSelectedTable.status !== 'InUse' || !currentSelectedTable.sessionId || !isTimed) return;

        showExtendForm();
    }

    async function handleConfirmExtendSession() {
        if (isProcessing) return;
        const isTimed = (currentSelectedTable.sessionMode && currentSelectedTable.sessionMode.trim().toLowerCase() === 'timed');
        if (currentSelectedTable.status !== 'InUse' || !currentSelectedTable.sessionId || !isTimed) return;

        const token = getVerificationToken();
        if (!token) {
            showToast('Thiếu mã xác thực bảo mật (Anti-forgery token).', 'danger');
            return;
        }

        const inputMinutes = document.getElementById('input-extend-minutes');
        const rawValue = inputMinutes ? inputMinutes.value.trim() : '';
        const num = Number(rawValue);
        if (!Number.isInteger(num) || num < 15 || num > 240 || num % 15 !== 0) {
            validateExtendForm();
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
        validateExtendForm();

        const targetSessionId = currentSelectedTable.sessionId;
        const targetTableId = currentSelectedTable.id;
        const addMinutes = num;

        try {
            const params = new URLSearchParams();
            params.append('SessionId', targetSessionId);
            params.append('AddMinutes', addMinutes);
            params.append('__RequestVerificationToken', token);

            const response = await fetch('/Table/ExtendSession', {
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
                hideExtendForm(false);

                const data = result.data ?? result.Data;
                const newPlannedEndUtc = data?.plannedEndAtUtc ?? data?.PlannedEndAtUtc;
                const formattedNewEnd = newPlannedEndUtc ? formatVnTimeOnly(newPlannedEndUtc) : '--:--';

                showToast(`Đã gia hạn thêm ${addMinutes} phút. Dự kiến kết thúc mới ${formattedNewEnd}`, 'success');
                await reloadTableGrid();
                await handleTableSelect(targetTableId);
            } else {
                const errorMsg = serverMessage || 'Gia hạn không thành công. Vui lòng thử lại.';
                showToast(errorMsg, 'danger');
                if (autoReload) {
                    hideExtendForm(false);
                    await reloadTableGrid();
                }
            }
        } catch {
            showToast('Không kết nối được máy chủ.', 'danger');
        } finally {
            isProcessing = false;
            validateExtendForm();
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

        // Phím Escape: nếu form inline đang mở thì đóng form trước, lần sau mới đóng panel
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' || e.key === 'Esc') {
                if (isOpenFormVisible) {
                    hideOpenForm(true);
                    e.preventDefault();
                    return;
                }
                if (isExtendFormVisible) {
                    hideExtendForm(true);
                    e.preventDefault();
                    return;
                }
                const panel = document.getElementById('table-detail-panel');
                if (panel && !panel.classList.contains('d-none')) {
                    closeDetailPanel(true);
                }
            }
        });

        // ---------------------------------------------------------------------
        // Sự kiện cho Form Mở bàn
        // ---------------------------------------------------------------------
        const radioOpen = document.getElementById('radio-mode-open');
        const radioTimed = document.getElementById('radio-mode-timed');
        const timedContainer = document.getElementById('timed-options-container');

        if (radioOpen) {
            radioOpen.addEventListener('change', () => {
                if (radioOpen.checked) {
                    openFormState.mode = 'Open';
                    if (timedContainer) timedContainer.classList.add('d-none');
                    validateOpenForm();
                    updateOpenFormPreview();
                }
            });
        }

        if (radioTimed) {
            radioTimed.addEventListener('change', () => {
                if (radioTimed.checked) {
                    openFormState.mode = 'Timed';
                    if (timedContainer) timedContainer.classList.remove('d-none');
                    validateOpenForm();
                    updateOpenFormPreview();
                }
            });
        }

        const presetButtons = document.querySelectorAll('.btn-preset-minutes');
        presetButtons.forEach(btn => {
            btn.addEventListener('click', () => {
                const minutes = btn.getAttribute('data-minutes');
                const inputMinutes = document.getElementById('input-planned-minutes');
                if (inputMinutes && minutes) {
                    inputMinutes.value = minutes;
                    presetButtons.forEach(b => b.classList.remove('active'));
                    btn.classList.add('active');
                    validateOpenForm();
                    updateOpenFormPreview();
                }
            });
        });

        const inputPlannedMinutes = document.getElementById('input-planned-minutes');
        if (inputPlannedMinutes) {
            inputPlannedMinutes.addEventListener('input', () => {
                const val = inputPlannedMinutes.value.trim();
                const presetBtns = document.querySelectorAll('.btn-preset-minutes');
                presetBtns.forEach(b => {
                    if (b.getAttribute('data-minutes') === val) {
                        b.classList.add('active');
                    } else {
                        b.classList.remove('active');
                    }
                });
                validateOpenForm();
                updateOpenFormPreview();
            });
        }

        const btnConfirmOpen = document.getElementById('btn-confirm-open');
        if (btnConfirmOpen) {
            btnConfirmOpen.addEventListener('click', handleConfirmOpenSession);
        }

        const btnCancelOpen = document.getElementById('btn-cancel-open');
        if (btnCancelOpen) {
            btnCancelOpen.addEventListener('click', () => hideOpenForm(true));
        }

        // ---------------------------------------------------------------------
        // Sự kiện cho Form Gia hạn
        // ---------------------------------------------------------------------
        const presetExtendButtons = document.querySelectorAll('.btn-preset-extend-minutes');
        presetExtendButtons.forEach(btn => {
            btn.addEventListener('click', () => {
                const minutes = btn.getAttribute('data-minutes');
                const inputExtend = document.getElementById('input-extend-minutes');
                if (inputExtend && minutes) {
                    inputExtend.value = minutes;
                    presetExtendButtons.forEach(b => b.classList.remove('active'));
                    btn.classList.add('active');
                    validateExtendForm();
                    updateExtendFormPreview();
                }
            });
        });

        const inputExtendMinutes = document.getElementById('input-extend-minutes');
        if (inputExtendMinutes) {
            inputExtendMinutes.addEventListener('input', () => {
                const val = inputExtendMinutes.value.trim();
                const presetBtns = document.querySelectorAll('.btn-preset-extend-minutes');
                presetBtns.forEach(b => {
                    if (b.getAttribute('data-minutes') === val) {
                        b.classList.add('active');
                    } else {
                        b.classList.remove('active');
                    }
                });
                validateExtendForm();
                updateExtendFormPreview();
            });
        }

        const btnConfirmExtend = document.getElementById('btn-confirm-extend');
        if (btnConfirmExtend) {
            btnConfirmExtend.addEventListener('click', handleConfirmExtendSession);
        }

        const btnCancelExtend = document.getElementById('btn-cancel-extend');
        if (btnCancelExtend) {
            btnCancelExtend.addEventListener('click', () => hideExtendForm(true));
        }

        // ---------------------------------------------------------------------
        // Nút hành động chính
        // ---------------------------------------------------------------------
        const btnOpen = document.getElementById('btn-open-table');
        if (btnOpen) {
            btnOpen.addEventListener('click', handleOpenTable);
        }

        const btnExtend = document.getElementById('btn-extend-table');
        if (btnExtend) {
            btnExtend.addEventListener('click', handleExtendTable);
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
