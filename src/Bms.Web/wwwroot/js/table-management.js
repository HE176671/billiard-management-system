/**
 * BMS - Quản lý bàn & Phiên chơi (Table Management)
 * Phiên chơi tính tiền theo phút + Gói Combo theo giờ (trừ giờ FIFO qua từng chặng bàn, vượt giờ tính theo phút)
 */
(() => {
    'use strict';

    // =========================================================================
    // PHẦN 1: BIẾN TRẠNG THÁI (STATE) & CẤU HÌNH ĐỊNH DẠNG (FORMATTERS)
    // =========================================================================

    let currentSelectedTable = {
        id: null,
        code: '',
        status: '',
        displayStatus: '',
        sessionId: null,
        startAtUtc: null,
        endAtUtc: null,
        hasCombo: false,
        totalComboMinutes: 0,
        comboAmount: 0,
        overtimeAmount: 0,
        cardStatus: '',
        cardSessionStart: '',
        cardHasCombo: '',
        cardComboMinutes: '',
        segments: [],
        combos: [],
        hourlyRate: 0
    };

    let lastSelectedCardId = null;
    let isProcessing = false;
    let isDetailLoading = false;
    let detailAbortController = null;
    let detailRequestId = 0;

    let isOpenFormVisible = false;
    let isCloseFormVisible = false;
    let isCloseSummaryLoading = false;
    let closeSummaryRequestId = 0;
    let closeSummaryTimerSeconds = 0;

    let isTransferFormVisible = false;
    let lastAvailableSignature = '';

    let isComboFormVisible = false;
    let isComboListLoading = false;
    let activeCombosCatalog = [];

    let customerSearchTimeoutId = null;
    let customerSearchRequestId = 0;

    let detailEstimatedAmountSeconds = 0;
    let isEstimatedAmountRefreshing = false;
    let estimatedAmountRequestId = 0;

    let clockTimerId = null;
    let deltaOffset = 0;
    let isClockSkewCalculated = false;

    const notifiedComboStates = new Map();
    let isInitialScan = true;

    const POLL_INTERVAL = 10000;
    let pollTimerId = null;
    let isPolling = false;
    let isPollingStopped = false;
    let pollingAbortController = null;
    let pollingRequestId = 0;

    let currentStatusFilter = 'all';

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

    const vnTimeWithSecondsFormatter = new Intl.DateTimeFormat('vi-VN', {
        timeZone: 'Asia/Ho_Chi_Minh',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hour12: false
    });

    const vnNumberFormatter = new Intl.NumberFormat('vi-VN', {
        maximumFractionDigits: 0
    });

    // =========================================================================
    // PHẦN 2: HÀM TIỆN ÍCH ĐỊNH DẠNG THỜI GIAN & TIỀN TỆ
    // =========================================================================

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

    function formatVnTimeWithSeconds(utcDateOrMs) {
        if (!utcDateOrMs) return '--:--:--';
        try {
            const date = typeof utcDateOrMs === 'number' ? new Date(utcDateOrMs) : new Date(utcDateOrMs);
            if (isNaN(date.getTime())) return '--:--:--';
            return vnTimeWithSecondsFormatter.format(date);
        } catch {
            return '--:--:--';
        }
    }

    function formatVnCurrency(amount) {
        if (amount === null || amount === undefined || isNaN(amount)) return '0 VND';
        return `${vnNumberFormatter.format(Math.round(Number(amount)))} VND`;
    }

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

    // =========================================================================
    // PHẦN 3: TÍNH TOÁN THỜI GIAN COMBO & THÔNG BÁO (TOAST / ANTI-FORGERY)
    // =========================================================================

    /**
     * Tính trạng thái thời gian Combo của phiên đang sử dụng
     */
    function getComboTimeState(startMs, comboMinutes, nowMs, tableStatus) {
        if (!startMs || isNaN(startMs) || !comboMinutes || comboMinutes <= 0 || !nowMs || isNaN(nowMs) || tableStatus !== 'InUse') {
            return {
                state: 'none',
                cardText: '--',
                panelText: '--'
            };
        }

        const comboEndMs = startMs + comboMinutes * 60000;
        const remainingMs = comboEndMs - nowMs;

        if (remainingMs > 0) {
            return {
                state: 'normal',
                cardText: `Còn ${formatRemainingTime(remainingMs)}`,
                panelText: `Còn ${formatRemainingTime(remainingMs)} (trong gói ${comboMinutes}p)`
            };
        }

        const overtimeMs = Math.abs(remainingMs);
        return {
            state: 'overdue',
            cardText: `Vượt +${formatRemainingTime(overtimeMs)}`,
            panelText: `Đã hết giờ Combo — Vượt +${formatRemainingTime(overtimeMs)} (tính vượt giờ)`
        };
    }

    function showToast(message, type = 'success', delay = 4500) {
        const container = document.getElementById('bms-toast-container');
        if (!container) return;

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
        bodyDiv.textContent = message;

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
            toastEl.addEventListener('hidden.bs.toast', () => toastEl.remove());
        } else {
            toastEl.style.display = 'block';
            setTimeout(() => toastEl.remove(), delay);
        }
    }

    function getVerificationToken() {
        const holder = document.getElementById('antiforgery-holder');
        const input = holder ? holder.querySelector('input[name="__RequestVerificationToken"]') : null;
        return input ? input.value : '';
    }

    // =========================================================================
    // PHẦN 4: ĐIỀU KHIỂN NÚT BẤM & ĐỒNG HỒ ĐẾM GIÂY TRỰC TIẾP (UPDATECLOCKS)
    // =========================================================================

    function updateButtons() {
        const btnOpen = document.getElementById('btn-open-table');
        const btnClose = document.getElementById('btn-close-table');
        const btnTransfer = document.getElementById('btn-transfer');
        const btnCombo = document.getElementById('btn-select-combo');

        if (isOpenFormVisible || isCloseFormVisible || isTransferFormVisible || isComboFormVisible || isProcessing || isDetailLoading) {
            if (btnOpen) btnOpen.disabled = true;
            if (btnClose) btnClose.disabled = true;
            if (btnTransfer) btnTransfer.disabled = true;
            if (btnCombo) btnCombo.disabled = true;
            return;
        }

        const { status, sessionId } = currentSelectedTable;

        if (btnOpen) {
            btnOpen.disabled = (status !== 'Available');
        }
        if (btnClose) {
            btnClose.disabled = !(status === 'InUse' && sessionId);
        }
        if (btnTransfer) {
            btnTransfer.disabled = !(status === 'InUse' && sessionId);
        }
        if (btnCombo) {
            btnCombo.disabled = !(status === 'InUse' && sessionId);
        }
    }

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

    function checkComboAlerts(cards) {
        if (!isClockSkewCalculated) return;
        const nowServerMs = Date.now() - deltaOffset;

        cards.forEach(card => {
            const status = card.getAttribute('data-status') || '';
            const hasCombo = card.getAttribute('data-has-combo') === 'true';
            const comboMinutes = parseInt(card.getAttribute('data-combo-minutes') || '0', 10);
            const sessionStartStr = card.getAttribute('data-session-start');

            if (status !== 'InUse' || !hasCombo || comboMinutes <= 0 || !sessionStartStr) return;

            const startMs = Date.parse(sessionStartStr);
            if (isNaN(startMs)) return;

            const tableId = card.getAttribute('data-table-id');
            const tableCode = card.getAttribute('data-table-code') || '';
            const key = `${tableId}_${sessionStartStr}_${comboMinutes}`;

            const info = getComboTimeState(startMs, comboMinutes, nowServerMs, status);
            const prev = notifiedComboStates.get(key);

            if (!prev || prev !== info.state) {
                notifiedComboStates.set(key, info.state);
                if (!isInitialScan && info.state === 'overdue') {
                    showToast(`Bàn ${tableCode} đã hết giờ Combo (chuyển sang tính vượt giờ theo phút)!`, 'warning', 6500);
                }
            }
        });

        isInitialScan = false;
    }

    function updateClocks() {
        const nowServerMs = Date.now() - deltaOffset;
        const cards = document.querySelectorAll('#table-grid-container .table-card');
        let timeStateChanged = false;

        cards.forEach(card => {
            const status = card.getAttribute('data-status') || '';
            const prevTimeState = card.getAttribute('data-time-state') || '';
            let displayStatus = card.getAttribute('data-display-status') || '';
            const clockEl = card.querySelector('.table-clock');
            const statusTextEl = card.querySelector('.status-text');
            const sessionStartStr = card.getAttribute('data-session-start');
            const hasCombo = card.getAttribute('data-has-combo') === 'true';
            const comboMinutes = parseInt(card.getAttribute('data-combo-minutes') || '0', 10);
            const remainingEl = card.querySelector('.table-remaining');
            const tableCode = card.getAttribute('data-table-code') || '';

            if (status === 'InUse' && sessionStartStr) {
                const startParsed = Date.parse(sessionStartStr);
                if (!isNaN(startParsed)) {
                    const diffMs = Math.max(0, nowServerMs - startParsed);
                    const totalSeconds = Math.floor(diffMs / 1000);
                    if (clockEl) clockEl.textContent = formatElapsedSeconds(totalSeconds);

                    if (hasCombo && comboMinutes > 0 && remainingEl) {
                        const comboInfo = getComboTimeState(startParsed, comboMinutes, nowServerMs, status);
                        remainingEl.textContent = comboInfo.cardText;
                        if (comboInfo.state === 'overdue') {
                            card.setAttribute('data-time-state', 'overdue');
                            displayStatus = 'Đã hết giờ Combo (Tính vượt giờ)';
                        } else {
                            card.removeAttribute('data-time-state');
                            displayStatus = 'Đang sử dụng';
                        }
                    } else {
                        if (remainingEl) remainingEl.textContent = '--';
                        card.removeAttribute('data-time-state');
                        displayStatus = 'Đang sử dụng';
                    }
                } else {
                    if (clockEl) clockEl.textContent = '--';
                    if (remainingEl) remainingEl.textContent = '--';
                    card.removeAttribute('data-time-state');
                }
            } else {
                if (clockEl) clockEl.textContent = '--';
                if (remainingEl) remainingEl.textContent = '--';
                card.removeAttribute('data-time-state');
            }

            card.setAttribute('data-display-status', displayStatus);
            if (tableCode && displayStatus) {
                card.setAttribute('aria-label', `Bàn ${tableCode}, ${displayStatus}`);
            }
            if (statusTextEl) {
                statusTextEl.textContent = displayStatus;
            }

            const nextTimeState = card.getAttribute('data-time-state') || '';
            if (prevTimeState !== nextTimeState) {
                timeStateChanged = true;
            }
        });

        if (timeStateChanged) {
            updateTableFilterAndCounts(false);
        }

        const panel = document.getElementById('table-detail-panel');
        const isPanelVisible = panel && !panel.classList.contains('d-none');

        if (isPanelVisible && currentSelectedTable.id) {
            const elDuration = document.getElementById('detail-duration');
            if (elDuration) {
                if (currentSelectedTable.status === 'InUse' && currentSelectedTable.startAtUtc) {
                    const startParsed = Date.parse(currentSelectedTable.startAtUtc);
                    if (!isNaN(startParsed)) {
                        const diffMs = Math.max(0, nowServerMs - startParsed);
                        elDuration.textContent = formatElapsedSeconds(Math.floor(diffMs / 1000));
                    } else {
                        elDuration.textContent = '--';
                    }
                } else if (currentSelectedTable.startAtUtc && currentSelectedTable.endAtUtc) {
                    elDuration.textContent = formatDuration(currentSelectedTable.startAtUtc, currentSelectedTable.endAtUtc);
                } else {
                    elDuration.textContent = '--';
                }
            }

            const elComboRemaining = document.getElementById('detail-combo-remaining');
            const rowComboRemaining = document.getElementById('detail-combo-remaining-row');
            const elStatus = document.getElementById('detail-status');
            if (elComboRemaining && rowComboRemaining) {
                if (currentSelectedTable.hasCombo && currentSelectedTable.totalComboMinutes > 0 && currentSelectedTable.status === 'InUse' && currentSelectedTable.startAtUtc) {
                    const startMs = Date.parse(currentSelectedTable.startAtUtc);
                    const info = getComboTimeState(startMs, currentSelectedTable.totalComboMinutes, nowServerMs, currentSelectedTable.status);
                    elComboRemaining.textContent = info.panelText;
                    elComboRemaining.className = 'detail-val duration-val ' + (info.state === 'overdue' ? 'text-danger fw-bold' : 'text-success');
                    rowComboRemaining.classList.remove('d-none');

                    const liveStatus = info.state === 'overdue' ? 'Đã hết giờ Combo (Tính vượt giờ)' : 'Đang sử dụng';
                    currentSelectedTable.displayStatus = liveStatus;
                    if (elStatus) elStatus.textContent = liveStatus;
                } else {
                    rowComboRemaining.classList.add('d-none');
                }
            }
        }

        if (isOpenFormVisible) {
            updateOpenFormPreview();
        }

        checkComboAlerts(cards);

        if (isCloseFormVisible && currentSelectedTable.sessionId) {
            closeSummaryTimerSeconds++;
            if (closeSummaryTimerSeconds >= 10) {
                closeSummaryTimerSeconds = 0;
                if (!isCloseSummaryLoading && !document.hidden) {
                    loadCloseSummary(currentSelectedTable.sessionId, false);
                }
            }
        }

        if (isPanelVisible && currentSelectedTable.id && currentSelectedTable.status === 'InUse' &&
            !isOpenFormVisible && !isCloseFormVisible && !isTransferFormVisible && !isComboFormVisible) {
            detailEstimatedAmountSeconds++;
            if (detailEstimatedAmountSeconds >= 15) {
                detailEstimatedAmountSeconds = 0;
                if (!isDetailLoading && !isEstimatedAmountRefreshing && !document.hidden) {
                    refreshEstimatedAmount(currentSelectedTable.id);
                }
            }
        }
    }

    function startClockTimer() {
        if (clockTimerId !== null) return;
        updateClocks();
        clockTimerId = setInterval(updateClocks, 1000);
    }

    // =========================================================================
    // PHẦN 5: LÀM MỚI TIỀN TẠM TÍNH (15S) & HIỂN THỊ CHI TIẾT PHIÊN CHƠI
    // =========================================================================

    function updatePricingBoxDisplay(detail) {
        const boxPricing = document.getElementById('detail-pricing-box');
        const rowComboAmt = document.getElementById('detail-combo-amount-row');
        const elComboAmt = document.getElementById('detail-combo-amount');
        const rowOvertimeAmt = document.getElementById('detail-overtime-amount-row');
        const elOvertimeAmt = document.getElementById('detail-overtime-amount');
        const elPlaytimeLabel = document.getElementById('detail-playtime-label');
        const elPlaytimeAmount = document.getElementById('detail-playtime-amount');

        const status = detail.status ?? detail.Status ?? currentSelectedTable.status;
        const hasCombo = Boolean(detail.hasCombo ?? detail.HasCombo);
        const comboAmount = Number(detail.comboAmount ?? detail.ComboAmount ?? 0);
        const overtimeAmount = Number(detail.overtimeAmount ?? detail.OvertimeAmount ?? 0);
        const estimatedAmount = detail.estimatedAmount ?? detail.EstimatedAmount;
        const serverTime = detail.serverTimeUtc ?? detail.ServerTimeUtc;

        if (status !== 'InUse') {
            if (boxPricing) boxPricing.classList.add('d-none');
            return;
        }

        if (hasCombo) {
            if (elComboAmt) elComboAmt.textContent = formatVnCurrency(comboAmount);
            if (rowComboAmt) rowComboAmt.classList.remove('d-none');

            if (elOvertimeAmt) {
                elOvertimeAmt.textContent = overtimeAmount > 0
                    ? formatVnCurrency(overtimeAmount)
                    : '0 VND (Trong giờ Combo)';
            }
            if (rowOvertimeAmt) rowOvertimeAmt.classList.remove('d-none');
        } else {
            if (rowComboAmt) rowComboAmt.classList.add('d-none');
            if (rowOvertimeAmt) rowOvertimeAmt.classList.add('d-none');
        }

        if (elPlaytimeLabel) elPlaytimeLabel.textContent = hasCombo ? 'Tổng tiền bàn (tạm tính):' : 'Tạm tính:';
        if (elPlaytimeAmount) {
            if (estimatedAmount !== null && estimatedAmount !== undefined) {
                const timeStr = serverTime ? ` (cập nhật ${formatVnTimeWithSeconds(serverTime)})` : '';
                elPlaytimeAmount.textContent = `${formatVnCurrency(estimatedAmount)}${timeStr}`;
            } else {
                elPlaytimeAmount.textContent = '--';
            }
        }

        if (boxPricing) boxPricing.classList.remove('d-none');
    }

    function updateFandBBoxDisplay(detail) {
        const boxFnb = document.getElementById('detail-fnb-box');
        const listContainer = document.getElementById('detail-fnb-orders-list');
        const elFnbTotal = document.getElementById('detail-fnb-total');
        const elGrandTotal = document.getElementById('detail-grand-total');

        const status = detail.status ?? detail.Status ?? currentSelectedTable.status;
        const orders = detail.orders ?? detail.Orders ?? [];
        const fnbAmount = Number(detail.fandBAmount ?? detail.FandBAmount ?? 0);
        const estimatedAmount = Number(detail.estimatedAmount ?? detail.EstimatedAmount ?? 0);

        if (status !== 'InUse') {
            if (boxFnb) boxFnb.classList.add('d-none');
            return;
        }

        if (boxFnb) boxFnb.classList.remove('d-none');

        if (elFnbTotal) elFnbTotal.textContent = formatVnCurrency(fnbAmount);
        if (elGrandTotal) elGrandTotal.textContent = formatVnCurrency(estimatedAmount + fnbAmount);

        if (!listContainer) return;

        if (orders.length === 0) {
            listContainer.innerHTML = '<div class="text-muted small py-1 fst-italic">Chưa gọi món nào.</div>';
            return;
        }

        let html = '<div class="fnb-items-scroll small" style="max-height:160px; overflow-y:auto;">';
        orders.forEach(order => {
            const isCompleted = order.status === 'Completed';
            const details = order.details ?? order.Details ?? [];
            details.forEach(item => {
                const activeQty = item.quantity - item.cancelledQuantity;
                const isCancelled = item.cancelledQuantity >= item.quantity;
                html += `
                    <div class="d-flex justify-content-between align-items-center py-1 border-bottom ${isCancelled ? 'text-decoration-line-through text-muted' : ''}">
                        <div>
                            <strong>${escapeHtml(item.productName)}</strong>
                            <span class="text-muted">x${item.quantity}</span>
                            ${item.cancelledQuantity > 0 ? `<span class="badge bg-danger-subtle text-danger ms-1">Đã hủy ${item.cancelledQuantity}</span>` : ''}
                        </div>
                        <div class="d-flex align-items-center gap-1">
                            <span class="fw-semibold ${isCancelled ? 'text-muted' : 'text-success'}">${formatVnCurrency(item.subtotal)}</span>
                            ${isCompleted && activeQty > 0 ? `
                                <button type="button" class="btn btn-outline-danger btn-xs py-0 px-1" title="Hủy món" 
                                        onclick="window.openCancelItemModal(${item.id}, '${escapeHtml(item.productName)}', ${activeQty})">
                                    <i class="bi bi-x-circle" style="font-size:0.75rem;"></i>
                                </button>
                            ` : ''}
                        </div>
                    </div>
                `;
            });
        });
        html += '</div>';
        listContainer.innerHTML = html;
    }

    async function refreshEstimatedAmount(tableId) {
        if (!tableId || isEstimatedAmountRefreshing) return;
        const requestId = ++estimatedAmountRequestId;
        isEstimatedAmountRefreshing = true;

        try {
            const response = await fetch(`/Table/GetTableDetail?tableId=${encodeURIComponent(tableId)}`, {
                method: 'GET',
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin'
            });

            if (requestId !== estimatedAmountRequestId || !response.ok) return;
            const detail = await response.json();
            if (requestId !== estimatedAmountRequestId) return;

            const panel = document.getElementById('table-detail-panel');
            if (!panel || panel.classList.contains('d-none')) return;
            if (currentSelectedTable.id !== tableId || currentSelectedTable.status !== 'InUse') return;

            currentSelectedTable.hasCombo = Boolean(detail.hasCombo ?? detail.HasCombo);
            currentSelectedTable.totalComboMinutes = Number(detail.totalComboMinutes ?? detail.TotalComboMinutes ?? 0);
            currentSelectedTable.comboAmount = Number(detail.comboAmount ?? detail.ComboAmount ?? 0);
            currentSelectedTable.overtimeAmount = Number(detail.overtimeAmount ?? detail.OvertimeAmount ?? 0);

            updatePricingBoxDisplay(detail);
        } catch {
        } finally {
            if (requestId === estimatedAmountRequestId) {
                isEstimatedAmountRefreshing = false;
            }
        }
    }

    function applyTableDetail(detail) {
        currentSelectedTable.id = detail.tableId ?? detail.TableId;
        currentSelectedTable.code = detail.tableCode ?? detail.TableCode ?? '';
        currentSelectedTable.status = detail.status ?? detail.Status ?? '';
        currentSelectedTable.displayStatus = detail.displayStatus ?? detail.DisplayStatus ?? '';
        currentSelectedTable.sessionId = detail.sessionId ?? detail.SessionId ?? null;
        currentSelectedTable.startAtUtc = detail.startAtUtc ?? detail.StartAtUtc ?? null;
        currentSelectedTable.endAtUtc = detail.endAtUtc ?? detail.EndAtUtc ?? null;
        currentSelectedTable.hasCombo = Boolean(detail.hasCombo ?? detail.HasCombo);
        currentSelectedTable.totalComboMinutes = Number(detail.totalComboMinutes ?? detail.TotalComboMinutes ?? 0);
        currentSelectedTable.comboAmount = Number(detail.comboAmount ?? detail.ComboAmount ?? 0);
        currentSelectedTable.overtimeAmount = Number(detail.overtimeAmount ?? detail.OvertimeAmount ?? 0);
        const snapshotRate = detail.hourlyRateSnapshot ?? detail.HourlyRateSnapshot;
        currentSelectedTable.hourlyRate = (snapshotRate !== null && snapshotRate !== undefined && Number(snapshotRate) > 0)
            ? Number(snapshotRate)
            : Number(detail.hourlyRate ?? detail.HourlyRate ?? 0);
        currentSelectedTable.segments = detail.segments ?? detail.Segments ?? [];
        currentSelectedTable.combos = detail.combos ?? detail.Combos ?? [];

        const card = document.querySelector(`.table-card[data-table-id="${currentSelectedTable.id}"]`);
        if (card) {
            currentSelectedTable.cardStatus = card.getAttribute('data-status') || '';
            currentSelectedTable.cardSessionStart = card.getAttribute('data-session-start') || '';
            currentSelectedTable.cardHasCombo = card.getAttribute('data-has-combo') || '';
            currentSelectedTable.cardComboMinutes = card.getAttribute('data-combo-minutes') || '';
        }

        const elTableName = document.getElementById('detail-table-name');
        const elStatus = document.getElementById('detail-status');
        const elHourlyRate = document.getElementById('detail-hourly-rate');
        const elStartTime = document.getElementById('detail-start-time');
        const elEndTime = document.getElementById('detail-end-time');
        const rowEndTime = document.getElementById('detail-end-time-row');

        const tableTypeName = detail.tableTypeName ?? detail.TableTypeName ?? '';
        if (elTableName) elTableName.textContent = tableTypeName ? `${currentSelectedTable.code} (${tableTypeName})` : (currentSelectedTable.code || '--');
        if (elStatus) elStatus.textContent = currentSelectedTable.displayStatus || '--';
        if (elHourlyRate) elHourlyRate.textContent = `${formatVnCurrency(currentSelectedTable.hourlyRate)}/giờ`;

        if (elStartTime) {
            elStartTime.textContent = currentSelectedTable.startAtUtc ? formatVnDateTime(currentSelectedTable.startAtUtc) : '--';
        }
        if (elEndTime) {
            elEndTime.textContent = currentSelectedTable.endAtUtc ? formatVnDateTime(currentSelectedTable.endAtUtc) : '--';
        }
        if (rowEndTime) {
            if (currentSelectedTable.endAtUtc) {
                rowEndTime.classList.remove('d-none');
            } else {
                rowEndTime.classList.add('d-none');
            }
        }

        updatePricingBoxDisplay(detail);
        updateFandBBoxDisplay(detail);
        renderSessionCombos(currentSelectedTable.combos, currentSelectedTable.totalComboMinutes);
        renderTableSegments(currentSelectedTable.segments);
        updateClocks();

        isDetailLoading = false;
        updateButtons();
    }

    function renderSessionCombos(combos, totalComboMinutes) {
        const boxEl = document.getElementById('detail-combos-box');
        const listEl = document.getElementById('detail-combos-list');
        const badgeEl = document.getElementById('detail-combos-hours-badge');
        if (!boxEl || !listEl) return;

        listEl.replaceChildren();
        if (!combos || combos.length === 0) {
            boxEl.classList.add('d-none');
            return;
        }

        boxEl.classList.remove('d-none');
        if (badgeEl) {
            const hoursVal = (totalComboMinutes / 60).toFixed(2).replace(/\.00$/, '').replace(/(\.\d)0$/, '$1');
            badgeEl.textContent = `Tổng ${hoursVal} giờ (${totalComboMinutes} phút)`;
        }

        combos.forEach(c => {
            const card = document.createElement('div');
            card.className = 'combo-purchase-card';

            const header = document.createElement('div');
            header.className = 'combo-purchase-header';

            const nameSpan = document.createElement('span');
            nameSpan.className = 'combo-purchase-name';
            const cName = c.comboName ?? c.ComboName ?? 'Combo';
            nameSpan.textContent = cName;

            const priceSpan = document.createElement('span');
            priceSpan.className = 'combo-purchase-price';
            const subtotal = c.subtotal ?? c.Subtotal ?? (c.unitPrice ?? c.UnitPrice ?? 0);
            priceSpan.textContent = formatVnCurrency(subtotal);

            header.appendChild(nameSpan);
            header.appendChild(priceSpan);
            card.appendChild(header);

            const metaDiv = document.createElement('div');
            metaDiv.className = 'combo-purchase-meta';
            const totalMins = c.totalMinutes ?? c.TotalMinutes ?? 0;
            const purchasedAt = c.purchasedAtUtc ?? c.PurchasedAtUtc;
            metaDiv.textContent = `Giờ chơi: ${totalMins} phút · Mua lúc ${purchasedAt ? formatVnTimeOnly(purchasedAt) : '--:--'}`;
            card.appendChild(metaDiv);

            const items = c.items ?? c.Items ?? [];
            if (items.length > 0) {
                const itemsDiv = document.createElement('div');
                itemsDiv.className = 'combo-purchase-items';
                const itemTexts = items.map(i => {
                    const pName = i.productName ?? i.ProductName ?? '';
                    const tQty = i.totalQuantity ?? i.TotalQuantity ?? (i.quantityPerCombo ?? i.QuantityPerCombo ?? 1);
                    return `${pName} ×${tQty}`;
                });
                itemsDiv.textContent = `Món kèm: ${itemTexts.join(', ')}`;
                card.appendChild(itemsDiv);
            }

            listEl.appendChild(card);
        });
    }

    function renderTableSegments(segments) {
        const boxEl = document.getElementById('detail-segments-box');
        const listEl = document.getElementById('detail-segments-list');
        if (!boxEl || !listEl) return;

        listEl.replaceChildren();
        if (!segments || segments.length < 2) {
            boxEl.classList.add('d-none');
            return;
        }

        boxEl.classList.remove('d-none');
        segments.forEach(seg => {
            const row = document.createElement('div');
            row.className = 'segment-item-row';

            const leftDiv = document.createElement('div');
            leftDiv.className = 'segment-item-left';
            const tableSpan = document.createElement('span');
            tableSpan.className = 'segment-item-table';
            tableSpan.textContent = seg.tableCode ?? seg.TableCode ?? '--';
            const typeSpan = document.createElement('span');
            typeSpan.className = 'segment-item-type';
            typeSpan.textContent = seg.tableTypeName ?? seg.TableTypeName ?? '';
            leftDiv.appendChild(tableSpan);
            if (typeSpan.textContent) leftDiv.appendChild(typeSpan);

            const rightDiv = document.createElement('div');
            rightDiv.className = 'segment-item-right';
            const startUtc = seg.startAtUtc ?? seg.StartAtUtc;
            const endUtc = seg.endAtUtc ?? seg.EndAtUtc;
            const startTimeStr = startUtc ? formatVnTimeOnly(startUtc) : '--:--';
            const endTimeStr = endUtc ? formatVnTimeOnly(endUtc) : 'đang sử dụng';

            const timeSpan = document.createElement('span');
            timeSpan.className = 'segment-item-time';
            timeSpan.textContent = `${startTimeStr} - ${endTimeStr}`;

            const rate = seg.hourlyRate ?? seg.HourlyRate ?? 0;
            const rateSpan = document.createElement('span');
            rateSpan.className = 'segment-item-rate';
            rateSpan.textContent = `${formatVnCurrency(rate)}/giờ`;

            rightDiv.appendChild(timeSpan);
            rightDiv.appendChild(rateSpan);
            row.appendChild(leftDiv);
            row.appendChild(rightDiv);
            listEl.appendChild(row);
        });
    }

    function clearDetailFields() {
        const ids = ['detail-table-name', 'detail-status', 'detail-hourly-rate', 'detail-customer', 'detail-start-time', 'detail-end-time', 'detail-duration', 'detail-combo-remaining'];
        ids.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.textContent = '--';
        });
        const hideIds = ['detail-end-time-row', 'detail-combo-remaining-row', 'detail-pricing-box', 'detail-combos-box', 'detail-fnb-box'];
        hideIds.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.classList.add('d-none');
        });
        renderSessionCombos([], 0);
        renderTableSegments([]);
    }

    // =========================================================================
    // PHẦN 6: CHỌN BÀN, LỌC TRẠNG THÁI, ĐÓNG PANEL & TỰ ĐỘNG ĐỒNG BỘ SƠ ĐỒ BÀN
    // =========================================================================

    function isCardMatchingFilter(status, isOverdue, filterMode) {
        if (filterMode === 'inuse') {
            return status === 'InUse' && !isOverdue;
        }
        if (filterMode === 'available') {
            return status === 'Available';
        }
        if (filterMode === 'overdue') {
            return status === 'InUse' && isOverdue;
        }
        if (filterMode === 'maintenance') {
            return status === 'Maintenance';
        }
        return true;
    }

    function updateTableFilterAndCounts(closeHiddenSelection = false) {
        const cards = document.querySelectorAll('#table-grid-container .table-card');
        let totalCount = 0;
        let inUseCount = 0;
        let availableCount = 0;
        let overdueCount = 0;
        let maintenanceCount = 0;
        let visibleCount = 0;

        cards.forEach(card => {
            const status = card.getAttribute('data-status') || '';
            const isOverdue = status === 'InUse' && (
                card.getAttribute('data-time-state') === 'overdue' ||
                card.getAttribute('data-display-status') === 'Đã hết giờ Combo (Tính vượt giờ)'
            );

            totalCount++;
            if (status === 'InUse' && !isOverdue) inUseCount++;
            else if (status === 'Available') availableCount++;
            else if (status === 'InUse' && isOverdue) overdueCount++;
            else if (status === 'Maintenance') maintenanceCount++;

            const matches = isCardMatchingFilter(status, isOverdue, currentStatusFilter);
            card.classList.toggle('d-none', !matches);
            if (matches) visibleCount++;
        });

        const elAll = document.getElementById('filter-count-all');
        const elInUse = document.getElementById('filter-count-inuse');
        const elAvailable = document.getElementById('filter-count-available');
        const elOverdue = document.getElementById('filter-count-overdue');
        const elMaintenance = document.getElementById('filter-count-maintenance');
        if (elAll) elAll.textContent = String(totalCount);
        if (elInUse) elInUse.textContent = String(inUseCount);
        if (elAvailable) elAvailable.textContent = String(availableCount);
        if (elOverdue) elOverdue.textContent = String(overdueCount);
        if (elMaintenance) elMaintenance.textContent = String(maintenanceCount);

        document.querySelectorAll('.quick-filter-radio').forEach(lbl => {
            const radio = lbl.querySelector('input[name="tableQuickFilter"]');
            lbl.classList.toggle('active', Boolean(radio && radio.value === currentStatusFilter));
        });

        const emptyFilterEl = document.getElementById('table-filter-empty');
        const gridEl = document.getElementById('table-grid');
        if (emptyFilterEl) {
            const shouldShowEmpty = totalCount > 0 && visibleCount === 0;
            emptyFilterEl.classList.toggle('d-none', !shouldShowEmpty);
            if (gridEl) gridEl.classList.toggle('d-none', shouldShowEmpty);
        }

        if (closeHiddenSelection && currentSelectedTable.id) {
            const selectedCard = document.querySelector(`#table-grid-container .table-card[data-table-id="${currentSelectedTable.id}"]`);
            if (selectedCard && selectedCard.classList.contains('d-none')) {
                closeDetailPanel(false);
            }
        }
    }

    function closeDetailPanel(returnFocus = true) {
        if (isOpenFormVisible) hideOpenForm(false);
        if (isCloseFormVisible) hideCloseForm(false);
        if (isTransferFormVisible) hideTransferForm(false);
        if (isComboFormVisible) hideComboForm(false);

        const panel = document.getElementById('table-detail-panel');
        if (panel) panel.classList.add('d-none');

        highlightSelectedCard(null);
        const prevSelectedId = currentSelectedTable.id || lastSelectedCardId;

        if (detailAbortController) {
            detailAbortController.abort();
            detailAbortController = null;
        }

        currentSelectedTable = {
            id: null, code: '', status: '', displayStatus: '', sessionId: null,
            startAtUtc: null, endAtUtc: null, hasCombo: false, totalComboMinutes: 0,
            comboAmount: 0, overtimeAmount: 0,
            cardStatus: '', cardSessionStart: '', cardHasCombo: '', cardComboMinutes: '',
            segments: [], combos: [], hourlyRate: 0
        };

        isDetailLoading = false;
        clearDetailFields();
        updateButtons();

        if (returnFocus && prevSelectedId) {
            const card = document.querySelector(`.table-card[data-table-id="${prevSelectedId}"]`);
            if (card && typeof card.focus === 'function') card.focus();
        }
    }

    async function handleTableSelect(tableId) {
        if (!tableId) return;
        if (isOpenFormVisible) hideOpenForm(false);
        if (isCloseFormVisible) hideCloseForm(false);
        if (isTransferFormVisible) hideTransferForm(false);
        if (isComboFormVisible) hideComboForm(false);

        lastSelectedCardId = tableId;
        const panel = document.getElementById('table-detail-panel');
        if (panel) panel.classList.remove('d-none');

        if (detailAbortController) detailAbortController.abort();
        detailAbortController = new AbortController();
        const signal = detailAbortController.signal;
        const requestId = ++detailRequestId;

        isDetailLoading = true;
        currentSelectedTable.id = tableId;
        highlightSelectedCard(tableId);
        updateButtons();

        try {
            const response = await fetch(`/Table/GetTableDetail?tableId=${encodeURIComponent(tableId)}`, {
                method: 'GET',
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin',
                signal
            });

            if (requestId !== detailRequestId) return;
            if (!response.ok) {
                showToast('Không thể lấy chi tiết bàn.', 'danger');
                return;
            }
            const detail = await response.json();
            if (requestId !== detailRequestId) return;
            applyTableDetail(detail);
        } catch (err) {
            if (err.name === 'AbortError') return;
            if (requestId === detailRequestId) showToast('Không kết nối được máy chủ.', 'danger');
        } finally {
            if (requestId === detailRequestId) {
                isDetailLoading = false;
                updateButtons();
            }
        }
    }

    async function applyGridHtml(html) {
        const parser = new DOMParser();
        const doc = parser.parseFromString(html, 'text/html');
        const newGrid = doc.getElementById('table-grid');
        if (!newGrid) return;

        const container = document.getElementById('table-grid-container');
        if (!container) return;

        const selectedId = currentSelectedTable.id;
        const prevCardStatus = currentSelectedTable.cardStatus;
        const prevCardSessionStart = currentSelectedTable.cardSessionStart;
        const prevCardHasCombo = currentSelectedTable.cardHasCombo;
        const prevCardComboMinutes = currentSelectedTable.cardComboMinutes;

        container.innerHTML = html;
        recalculateDeltaOffset();
        updateTableFilterAndCounts(false);
        updateClocks();

        const panel = document.getElementById('table-detail-panel');
        const isPanelVisible = panel && !panel.classList.contains('d-none');
        if (!selectedId || !isPanelVisible) {
            updateButtons();
            return;
        }

        const card = container.querySelector(`.table-card[data-table-id="${selectedId}"]`);
        if (!card) {
            closeDetailPanel(false);
            return;
        }

        card.classList.add('selected');
        const newCardStatus = card.getAttribute('data-status') || '';
        const newCardSessionStart = card.getAttribute('data-session-start') || '';
        const newCardHasCombo = card.getAttribute('data-has-combo') || '';
        const newCardComboMinutes = card.getAttribute('data-combo-minutes') || '';

        if (isOpenFormVisible && newCardStatus !== 'Available') {
            hideOpenForm(false);
        }
        if (isCloseFormVisible && newCardStatus !== 'InUse') {
            hideCloseForm(false);
        }
        if (isTransferFormVisible && newCardStatus !== 'InUse') {
            hideTransferForm(false);
        }
        if (isComboFormVisible && newCardStatus !== 'InUse') {
            hideComboForm(false);
        }

        if (newCardStatus !== prevCardStatus ||
            newCardSessionStart !== prevCardSessionStart ||
            newCardHasCombo !== prevCardHasCombo ||
            newCardComboMinutes !== prevCardComboMinutes) {
            if (!isOpenFormVisible && !isCloseFormVisible && !isTransferFormVisible && !isComboFormVisible) {
                await handleTableSelect(selectedId);
            }
        }
        updateButtons();
    }

    async function pollTableGrid() {
        if (isPollingStopped || document.hidden || isPolling || isProcessing || isDetailLoading) return;
        if (pollingAbortController) pollingAbortController.abort();
        pollingAbortController = new AbortController();
        const signal = pollingAbortController.signal;
        const requestId = ++pollingRequestId;
        isPolling = true;

        try {
            const response = await fetch('/Table/GetTableCardsPartial', {
                method: 'GET',
                headers: { 'Accept': 'text/html' },
                credentials: 'same-origin',
                signal
            });
            if (requestId !== pollingRequestId || !response.ok) return;
            const html = await response.text();
            if (requestId !== pollingRequestId || isProcessing) return;
            await applyGridHtml(html);
        } catch {
        } finally {
            if (requestId === pollingRequestId) isPolling = false;
        }
    }

    function startPolling() {
        if (pollTimerId !== null || isPollingStopped) return;
        pollTimerId = setInterval(pollTableGrid, POLL_INTERVAL);
    }

    async function reloadTableGrid() {
        try {
            const response = await fetch('/Table/GetTableCardsPartial', {
                method: 'GET',
                headers: { 'Accept': 'text/html' },
                credentials: 'same-origin'
            });
            if (!response.ok) return;
            const html = await response.text();
            await applyGridHtml(html);
        } catch {
        }
    }

    // =========================================================================
    // PHẦN 7: LUỒNG MỞ BÀN (CHỌN 1 TRONG 2 CHẾ ĐỘ: TÍNH GIỜ CHƠI HOẶC GÓI COMBO)
    // =========================================================================

    function getSelectedOpenBillingMode() {
        const comboRadio = document.getElementById('mode-combo');
        return (comboRadio && comboRadio.checked) ? 'combo' : 'hourly';
    }

    function updateOpenFormPreview() {
        if (!isOpenFormVisible) return;
        const nowServerMs = Date.now() - deltaOffset;
        const elStart = document.getElementById('preview-open-start');
        if (elStart) elStart.textContent = formatVnTimeWithSeconds(nowServerMs);
    }

    function handleBillingModeChange() {
        const mode = getSelectedOpenBillingMode();
        const comboSection = document.getElementById('open-combo-section');
        const lblHourly = document.getElementById('lbl-mode-hourly');
        const lblCombo = document.getElementById('lbl-mode-combo');
        const btnConfirmOpen = document.getElementById('btn-confirm-open');

        if (mode === 'combo') {
            if (comboSection) comboSection.classList.remove('d-none');
            if (lblCombo) lblCombo.classList.add('bg-light', 'border-warning');
            if (lblHourly) lblHourly.classList.remove('bg-light', 'border-primary');
            loadActiveCombosCatalog();
        } else {
            if (comboSection) comboSection.classList.add('d-none');
            if (lblHourly) lblHourly.classList.add('bg-light', 'border-primary');
            if (lblCombo) lblCombo.classList.remove('bg-light', 'border-warning');
            if (btnConfirmOpen) btnConfirmOpen.disabled = isProcessing;
        }
    }

    function showOpenForm() {
        if (currentSelectedTable.status !== 'Available') return;
        if (isCloseFormVisible) hideCloseForm(false);
        if (isTransferFormVisible) hideTransferForm(false);
        if (isComboFormVisible) hideComboForm(false);

        isOpenFormVisible = true;
        const formEl = document.getElementById('open-session-form');
        const actionsBox = document.getElementById('action-buttons-box');
        if (formEl) formEl.classList.remove('d-none');
        if (actionsBox) actionsBox.classList.add('d-none');

        const hourlyRadio = document.getElementById('mode-hourly');
        if (hourlyRadio) hourlyRadio.checked = true;

        handleBillingModeChange();
        updateOpenFormPreview();
        updateButtons();
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
            if (btnOpen && !btnOpen.disabled) btnOpen.focus();
        }
    }

    async function handleConfirmOpenSession() {
        if (isProcessing || !currentSelectedTable.id || currentSelectedTable.status !== 'Available') return;
        const token = getVerificationToken();
        if (!token) {
            showToast('Thiếu mã xác thực bảo mật.', 'danger');
            return;
        }

        const billingMode = getSelectedOpenBillingMode();
        const selectCombo = document.getElementById('select-combo-package');
        const comboId = selectCombo ? parseInt(selectCombo.value, 10) : 0;

        if (billingMode === 'combo' && (!comboId || comboId <= 0)) {
            showToast('Vui lòng chọn một gói Combo để mở bàn theo chế độ Combo.', 'warning');
            return;
        }

        isProcessing = true;
        updateButtons();

        const targetTableId = currentSelectedTable.id;
        const targetTableCode = currentSelectedTable.code;

        try {
            const params = new URLSearchParams();
            params.append('TableId', targetTableId);
            params.append('BillingMode', billingMode);
            if (billingMode === 'combo' && comboId > 0) {
                params.append('ComboId', comboId);
            }
            params.append('__RequestVerificationToken', token);

            const response = await fetch('/Table/OpenSession', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: params,
                credentials: 'same-origin'
            });

            const result = await response.json();
            const isSuccess = Boolean(result.success ?? result.Success);
            const serverMessage = result.message ?? result.Message;

            if (isSuccess) {
                hideOpenForm(false);
                const data = result.data ?? result.Data;
                const startUtc = data?.startAtUtc ?? data?.StartAtUtc;
                const modeLabel = billingMode === 'combo' ? 'theo gói Combo' : 'tính tiền theo giờ';
                showToast(serverMessage || `Đã mở bàn ${targetTableCode} (${modeLabel}) lúc ${startUtc ? formatVnTimeOnly(startUtc) : '--:--'}.`, 'success');
                await reloadTableGrid();
                await handleTableSelect(targetTableId);
            } else {
                showToast(serverMessage || 'Không thể mở bàn.', 'danger');
                await reloadTableGrid();
            }
        } catch {
            showToast('Không kết nối được máy chủ.', 'danger');
        } finally {
            isProcessing = false;
            updateButtons();
        }
    }

    // =========================================================================
    // PHẦN 8: TẢI DANH MỤC GÓI COMBO CHO CHẾ ĐỘ COMBO KHI MỞ BÀN
    // =========================================================================

    async function loadActiveCombosCatalog() {
        isComboListLoading = true;
        const loadingEl = document.getElementById('combo-list-loading');
        const emptyEl = document.getElementById('combo-list-empty');
        const fieldsEl = document.getElementById('combo-form-fields');
        const selectEl = document.getElementById('select-combo-package');
        const btnConfirm = document.getElementById('btn-confirm-open');

        if (loadingEl) loadingEl.classList.remove('d-none');
        if (emptyEl) emptyEl.classList.add('d-none');
        if (fieldsEl) fieldsEl.classList.add('d-none');
        if (btnConfirm && getSelectedOpenBillingMode() === 'combo') btnConfirm.disabled = true;

        try {
            const response = await fetch('/Table/GetActiveCombos', {
                method: 'GET',
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin'
            });
            if (!response.ok) return;

            const result = await response.json();
            activeCombosCatalog = result.data ?? result.Data ?? [];

            if (loadingEl) loadingEl.classList.add('d-none');

            if (!activeCombosCatalog || activeCombosCatalog.length === 0) {
                if (emptyEl) emptyEl.classList.remove('d-none');
                return;
            }

            if (fieldsEl) fieldsEl.classList.remove('d-none');
            if (selectEl) {
                selectEl.replaceChildren();
                const defaultOpt = document.createElement('option');
                defaultOpt.value = '';
                defaultOpt.textContent = '-- Chọn gói Combo --';
                selectEl.appendChild(defaultOpt);

                activeCombosCatalog.forEach(c => {
                    const id = c.id ?? c.Id;
                    const name = c.name ?? c.Name ?? '';
                    const price = c.price ?? c.Price ?? 0;
                    const hours = c.playtimeHours ?? c.PlaytimeHours ?? 0;
                    const opt = document.createElement('option');
                    opt.value = String(id);
                    opt.textContent = `${name} (${hours} giờ — ${formatVnCurrency(price)})`;
                    selectEl.appendChild(opt);
                });
            }

            updateComboFormPreview();
        } catch {
            if (loadingEl) loadingEl.classList.add('d-none');
        } finally {
            isComboListLoading = false;
        }
    }

    function updateComboFormPreview() {
        if (!isOpenFormVisible && !isComboFormVisible) return;
        const selectEl = document.getElementById('select-combo-package');
        const previewBox = document.getElementById('combo-preview-box');
        const elHours = document.getElementById('preview-combo-hours');
        const elPrice = document.getElementById('preview-combo-price');
        const elItems = document.getElementById('preview-combo-items');
        const btnConfirmOpen = document.getElementById('btn-confirm-open');

        const selectedId = selectEl ? parseInt(selectEl.value, 10) : 0;

        const combo = activeCombosCatalog.find(c => (c.id ?? c.Id) === selectedId);
        if (!combo) {
            if (previewBox) previewBox.classList.add('d-none');
            if (btnConfirmOpen && getSelectedOpenBillingMode() === 'combo') btnConfirmOpen.disabled = true;
            return;
        }

        const totalHours = Number(combo.playtimeHours ?? combo.PlaytimeHours ?? 0);
        const totalMins = Math.round(totalHours * 60);
        const totalPrice = Number(combo.price ?? combo.Price ?? 0);
        const items = combo.items ?? combo.Items ?? [];

        if (elHours) elHours.textContent = `${totalHours} giờ (${totalMins} phút)`;
        if (elPrice) elPrice.textContent = formatVnCurrency(totalPrice);
        if (elItems) {
            if (items.length > 0) {
                elItems.textContent = items.map(i => `${i.productName ?? i.ProductName} ×${i.quantity ?? i.Quantity ?? 1}`).join(', ');
            } else {
                elItems.textContent = 'Chỉ bao gồm giờ chơi';
            }
        }

        if (previewBox) previewBox.classList.remove('d-none');
        if (btnConfirmOpen && getSelectedOpenBillingMode() === 'combo') {
            btnConfirmOpen.disabled = isProcessing;
        }
    }

    function showComboForm() {}
    function hideComboForm() { isComboFormVisible = false; }
    async function handleConfirmSelectCombo() {}

    // =========================================================================
    // PHẦN 9: LUỒNG ĐÓNG BÀN (CLOSE TABLE & TÓM TẮT TIỀN BÀN / COMBO)
    // =========================================================================

    function showCloseForm() {
        if (currentSelectedTable.status !== 'InUse' || !currentSelectedTable.sessionId) return;
        if (isOpenFormVisible) hideOpenForm(false);
        if (isTransferFormVisible) hideTransferForm(false);
        if (isComboFormVisible) hideComboForm(false);

        isCloseFormVisible = true;
        closeSummaryTimerSeconds = 0;

        const formEl = document.getElementById('close-session-form');
        const actionsBox = document.getElementById('action-buttons-box');
        if (formEl) formEl.classList.remove('d-none');
        if (actionsBox) actionsBox.classList.add('d-none');

        const btnConfirmClose = document.getElementById('btn-confirm-close');
        if (btnConfirmClose) btnConfirmClose.disabled = true;

        updateButtons();
        loadCloseSummary(currentSelectedTable.sessionId, true);
    }

    function hideCloseForm(returnFocus = true) {
        isCloseFormVisible = false;
        isCloseSummaryLoading = false;
        closeSummaryRequestId++;

        const formEl = document.getElementById('close-session-form');
        const actionsBox = document.getElementById('action-buttons-box');
        if (formEl) formEl.classList.add('d-none');
        if (actionsBox) actionsBox.classList.remove('d-none');

        updateButtons();
        if (returnFocus) {
            const btnClose = document.getElementById('btn-close-table');
            if (btnClose && !btnClose.disabled) btnClose.focus();
        }
    }

    async function loadCloseSummary(sessionId, isInitial = false) {
        if (!sessionId) return;
        const requestId = ++closeSummaryRequestId;
        isCloseSummaryLoading = true;

        const elLoading = document.getElementById('close-summary-loading');
        const elError = document.getElementById('close-summary-error');
        const elErrorMsg = document.getElementById('close-summary-error-msg');
        const elContent = document.getElementById('close-summary-content');
        const btnConfirm = document.getElementById('btn-confirm-close');

        if (isInitial) {
            if (elLoading) elLoading.classList.remove('d-none');
            if (elError) elError.classList.add('d-none');
            if (elContent) elContent.classList.add('d-none');
            if (btnConfirm) btnConfirm.disabled = true;
        }

        try {
            const response = await fetch(`/Table/GetCloseSummary?sessionId=${encodeURIComponent(sessionId)}`, {
                method: 'GET',
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin'
            });
            if (requestId !== closeSummaryRequestId || !response.ok) return;

            const result = await response.json();
            if (requestId !== closeSummaryRequestId) return;

            const isSuccess = Boolean(result.success ?? result.Success);
            if (!isSuccess) {
                if (elLoading) elLoading.classList.add('d-none');
                if (elError) {
                    if (elErrorMsg) elErrorMsg.textContent = result.message ?? result.Message ?? 'Lỗi tải tóm tắt.';
                    elError.classList.remove('d-none');
                }
                return;
            }

            const data = result.data ?? result.Data;
            if (!data) return;

            const elTable = document.getElementById('close-summary-table');
            const elStart = document.getElementById('close-summary-start');
            const elEnd = document.getElementById('close-summary-end');
            const elMinutes = document.getElementById('close-summary-minutes');
            const rowComboTime = document.getElementById('close-summary-combo-time-row');
            const elComboMinutes = document.getElementById('close-summary-combo-minutes');
            const rowComboAmt = document.getElementById('close-summary-combo-amount-row');
            const elComboAmt = document.getElementById('close-summary-combo-amount');
            const rowOvertimeAmt = document.getElementById('close-summary-overtime-amount-row');
            const elOvertimeAmt = document.getElementById('close-summary-overtime-amount');
            const elAmount = document.getElementById('close-summary-amount');
            const elTimestamp = document.getElementById('close-summary-timestamp');

            const startAtUtc = data.startAtUtc ?? data.StartAtUtc;
            const serverNowUtc = data.serverNowUtc ?? data.ServerNowUtc;
            const billedMinutes = data.billedMinutes ?? data.BilledMinutes ?? 1;
            const hasCombo = Boolean(data.hasCombo ?? data.HasCombo);
            const totalComboMinutes = Number(data.totalComboMinutes ?? data.TotalComboMinutes ?? 0);
            const overtimeMinutes = Number(data.overtimeMinutes ?? data.OvertimeMinutes ?? 0);
            const comboAmount = Number(data.comboAmount ?? data.ComboAmount ?? 0);
            const overtimeAmount = Number(data.overtimeAmount ?? data.OvertimeAmount ?? 0);
            const estimatedAmount = data.estimatedAmount ?? data.EstimatedAmount ?? 0;

            if (elTable) elTable.textContent = data.tableCode ?? data.TableCode ?? currentSelectedTable.code;
            if (elStart) elStart.textContent = startAtUtc ? formatVnDateTime(startAtUtc) : '--';
            if (elEnd) elEnd.textContent = serverNowUtc ? formatVnDateTime(serverNowUtc) : '--';
            if (elMinutes) elMinutes.textContent = `${billedMinutes} phút`;

            if (hasCombo) {
                if (elComboMinutes) {
                    elComboMinutes.textContent = overtimeMinutes > 0
                        ? `${totalComboMinutes} phút (Vượt ${overtimeMinutes} phút)`
                        : `${totalComboMinutes} phút (Không vượt giờ)`;
                }
                if (rowComboTime) rowComboTime.classList.remove('d-none');

                if (elComboAmt) elComboAmt.textContent = formatVnCurrency(comboAmount);
                if (rowComboAmt) rowComboAmt.classList.remove('d-none');

                if (elOvertimeAmt) elOvertimeAmt.textContent = formatVnCurrency(overtimeAmount);
                if (rowOvertimeAmt) rowOvertimeAmt.classList.remove('d-none');
            } else {
                if (rowComboTime) rowComboTime.classList.add('d-none');
                if (rowComboAmt) rowComboAmt.classList.add('d-none');
                if (rowOvertimeAmt) rowOvertimeAmt.classList.add('d-none');
            }

            if (elAmount) elAmount.textContent = formatVnCurrency(estimatedAmount);
            if (elTimestamp) elTimestamp.textContent = serverNowUtc ? `Tạm tính lúc ${formatVnTimeWithSeconds(serverNowUtc)}` : '';

            if (elLoading) elLoading.classList.add('d-none');
            if (elError) elError.classList.add('d-none');
            if (elContent) elContent.classList.remove('d-none');
            if (btnConfirm) btnConfirm.disabled = isProcessing;
        } catch {
        } finally {
            if (requestId === closeSummaryRequestId) isCloseSummaryLoading = false;
        }
    }

    async function handleConfirmCloseSession() {
        if (isProcessing || !currentSelectedTable.sessionId || currentSelectedTable.status !== 'InUse') return;
        const token = getVerificationToken();
        if (!token) return;

        const targetSessionId = currentSelectedTable.sessionId;
        const targetTableId = currentSelectedTable.id;
        const targetTableCode = currentSelectedTable.code;

        isProcessing = true;
        updateButtons();

        try {
            const params = new URLSearchParams();
            params.append('SessionId', targetSessionId);
            params.append('__RequestVerificationToken', token);

            const response = await fetch('/Table/CloseSession', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: params,
                credentials: 'same-origin'
            });

            const result = await response.json();
            const isSuccess = Boolean(result.success ?? result.Success);

            if (isSuccess) {
                hideCloseForm(false);
                const data = result.data ?? result.Data;
                const playtimeAmount = data?.playtimeAmount ?? data?.PlaytimeAmount ?? 0;
                const billedMinutes = data?.billedMinutes ?? data?.BilledMinutes ?? 1;
                showToast(`Đã đóng bàn ${targetTableCode} (${billedMinutes} phút) — Tiền bàn: ${formatVnCurrency(playtimeAmount)}. Bàn đã về Còn trống, vui lòng thanh toán tại mục Tính tiền & Tích điểm.`, 'success', 6000);
                await reloadTableGrid();
                await handleTableSelect(targetTableId);
            } else {
                showToast(result.message ?? result.Message ?? 'Đóng bàn thất bại.', 'danger');
                await reloadTableGrid();
            }
        } catch {
            showToast('Không kết nối được máy chủ.', 'danger');
        } finally {
            isProcessing = false;
            updateButtons();
        }
    }

    // =========================================================================
    // PHẦN 10: LUỒNG CHUYỂN BÀN (CHUYỂN BÀN)
    // =========================================================================

    function getAvailableTablesFromGrid() {
        const grid = document.getElementById('table-grid-container');
        if (!grid) return [];
        const currentId = currentSelectedTable.id ? String(currentSelectedTable.id) : null;
        const availableCards = grid.querySelectorAll('.table-card[data-status="Available"]');
        const tables = [];

        availableCards.forEach(card => {
            const tableIdStr = card.getAttribute('data-table-id');
            if (!tableIdStr || tableIdStr === currentId) return;
            tables.push({
                id: parseInt(tableIdStr, 10),
                code: card.getAttribute('data-table-code') || '',
                type: card.getAttribute('data-table-type') || '',
                floor: card.getAttribute('data-floor') || '',
                hourlyRate: parseFloat(card.getAttribute('data-hourly-rate')) || 0
            });
        });

        tables.sort((a, b) => a.code.localeCompare(b.code, 'vi', { numeric: true }));
        return tables;
    }

    function getCurrentOpenSegmentRate() {
        if (currentSelectedTable.segments && currentSelectedTable.segments.length > 0) {
            const openSeg = currentSelectedTable.segments.find(s => !(s.endAtUtc ?? s.EndAtUtc));
            if (openSeg) return openSeg.hourlyRate ?? openSeg.HourlyRate ?? 0;
        }
        return currentSelectedTable.hourlyRate || 0;
    }

    function buildAvailableTableOptions(isFromPolling = false) {
        const tables = getAvailableTablesFromGrid();
        const signature = tables.map(t => `${t.code}:${t.hourlyRate}`).join('|');
        if (isFromPolling && signature === lastAvailableSignature) return;
        lastAvailableSignature = signature;

        const selectEl = document.getElementById('select-target-table');
        const noTablesMsg = document.getElementById('transfer-no-tables-msg');
        const btnConfirm = document.getElementById('btn-confirm-transfer');
        if (!selectEl) return;

        const prevValue = selectEl.value;
        selectEl.replaceChildren();

        const defaultOption = document.createElement('option');
        defaultOption.value = '';
        defaultOption.textContent = 'Chọn bàn đích...';
        selectEl.appendChild(defaultOption);

        if (tables.length === 0) {
            if (noTablesMsg) noTablesMsg.classList.remove('d-none');
            selectEl.disabled = true;
            if (btnConfirm) btnConfirm.disabled = true;
            updateTransferFormPreview();
            return;
        }

        if (noTablesMsg) noTablesMsg.classList.add('d-none');
        selectEl.disabled = false;

        tables.forEach(t => {
            const option = document.createElement('option');
            option.value = String(t.id);
            option.textContent = `${t.code} · ${t.type} · Tầng ${t.floor} · ${formatVnCurrency(t.hourlyRate)}/giờ`;
            selectEl.appendChild(option);
        });

        if (tables.some(t => String(t.id) === prevValue)) {
            selectEl.value = prevValue;
        }
        updateTransferFormPreview();
    }

    function updateTransferFormPreview() {
        if (!isTransferFormVisible) return;
        const selectEl = document.getElementById('select-target-table');
        const previewCurrentRateEl = document.getElementById('preview-transfer-rate-current');
        const previewTargetRateEl = document.getElementById('preview-transfer-rate-target');
        const previewDiffNoteEl = document.getElementById('preview-transfer-rate-diff-note');
        const previewDiffTextEl = document.getElementById('preview-transfer-rate-diff-text');
        const btnConfirm = document.getElementById('btn-confirm-transfer');

        const currentRate = getCurrentOpenSegmentRate();
        if (previewCurrentRateEl) previewCurrentRateEl.textContent = `${formatVnCurrency(currentRate)}/giờ`;

        const selectedTargetId = selectEl ? selectEl.value : '';
        if (selectedTargetId) {
            const card = document.querySelector(`.table-card[data-table-id="${selectedTargetId}"]`);
            const targetRate = card ? (parseFloat(card.getAttribute('data-hourly-rate')) || 0) : 0;
            if (previewTargetRateEl) previewTargetRateEl.textContent = `${formatVnCurrency(targetRate)}/giờ`;

            if (Math.round(currentRate) !== Math.round(targetRate)) {
                if (previewDiffTextEl) {
                    previewDiffTextEl.textContent = currentSelectedTable.hasCombo
                        ? 'Khác đơn giá: Giờ Combo trừ theo thứ tự từng bàn (FIFO); phần vượt giờ ở bàn nào tính theo đơn giá bàn đó.'
                        : 'Khác đơn giá: Tiền giờ được tính theo số phút thực tế ở từng bàn.';
                }
                if (previewDiffNoteEl) previewDiffNoteEl.classList.remove('d-none');
            } else if (previewDiffNoteEl) {
                previewDiffNoteEl.classList.add('d-none');
            }
            if (btnConfirm) btnConfirm.disabled = isProcessing;
        } else {
            if (previewTargetRateEl) previewTargetRateEl.textContent = '--';
            if (previewDiffNoteEl) previewDiffNoteEl.classList.add('d-none');
            if (btnConfirm) btnConfirm.disabled = true;
        }
    }

    function showTransferForm() {
        if (currentSelectedTable.status !== 'InUse' || !currentSelectedTable.sessionId) return;
        if (isOpenFormVisible) hideOpenForm(false);
        if (isCloseFormVisible) hideCloseForm(false);
        if (isComboFormVisible) hideComboForm(false);

        isTransferFormVisible = true;
        lastAvailableSignature = '';

        const formEl = document.getElementById('transfer-session-form');
        const actionsBox = document.getElementById('action-buttons-box');
        if (formEl) formEl.classList.remove('d-none');
        if (actionsBox) actionsBox.classList.add('d-none');

        buildAvailableTableOptions(false);
        updateButtons();
    }

    function hideTransferForm(returnFocus = true) {
        isTransferFormVisible = false;
        const formEl = document.getElementById('transfer-session-form');
        const actionsBox = document.getElementById('action-buttons-box');
        if (formEl) formEl.classList.add('d-none');
        if (actionsBox) actionsBox.classList.remove('d-none');
        updateButtons();
        if (returnFocus) {
            const btnTransfer = document.getElementById('btn-transfer');
            if (btnTransfer && !btnTransfer.disabled) btnTransfer.focus();
        }
    }

    async function handleConfirmTransferSession() {
        if (isProcessing || currentSelectedTable.status !== 'InUse' || !currentSelectedTable.sessionId) return;
        const selectEl = document.getElementById('select-target-table');
        const targetTableId = selectEl ? parseInt(selectEl.value, 10) : 0;
        if (!targetTableId) return;

        const token = getVerificationToken();
        if (!token) return;

        isProcessing = true;
        updateButtons();

        const targetSessionId = currentSelectedTable.sessionId;
        const oldTableCode = currentSelectedTable.code;

        try {
            const params = new URLSearchParams();
            params.append('SessionId', targetSessionId);
            params.append('NewTableId', targetTableId);
            params.append('__RequestVerificationToken', token);

            const response = await fetch('/Table/TransferSession', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: params,
                credentials: 'same-origin'
            });

            const result = await response.json();
            const isSuccess = Boolean(result.success ?? result.Success);

            if (isSuccess) {
                hideTransferForm(false);
                const data = result.data ?? result.Data;
                const newTableCode = data?.newTableCode ?? data?.NewTableCode ?? '';
                const newTableId = data?.newTableId ?? data?.NewTableId ?? targetTableId;
                showToast(`Đã chuyển bàn ${oldTableCode} sang ${newTableCode}.`, 'success');
                currentSelectedTable.id = null;
                await reloadTableGrid();
                await handleTableSelect(newTableId);
            } else {
                showToast(result.message ?? result.Message ?? 'Chuyển bàn thất bại.', 'danger');
                await reloadTableGrid();
            }
        } catch {
            showToast('Không kết nối được máy chủ.', 'danger');
        } finally {
            isProcessing = false;
            updateButtons();
        }
    }

    // =========================================================================
    // PHẦN 11: GẮN SỰ KIỆN DOM (INITEVENTS) & KHỞI CHẠY KHI TẢI TRANG
    // =========================================================================

    function initEvents() {
        const gridContainer = document.getElementById('table-grid-container');
        if (gridContainer) {
            gridContainer.addEventListener('click', (e) => {
                const card = e.target.closest('.table-card');
                if (!card) return;
                const tableId = card.getAttribute('data-table-id');
                if (tableId) handleTableSelect(tableId);
            });
            gridContainer.addEventListener('keydown', (e) => {
                const card = e.target.closest('.table-card');
                if (!card) return;
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    const tableId = card.getAttribute('data-table-id');
                    if (tableId) handleTableSelect(tableId);
                }
            });
        }

        document.querySelectorAll('input[name="tableQuickFilter"]').forEach(radio => {
            radio.addEventListener('change', (e) => {
                if (e.target.checked) {
                    currentStatusFilter = e.target.value || 'all';
                    updateTableFilterAndCounts(true);
                }
            });
        });

        const btnCloseDetail = document.getElementById('btn-close-detail');
        if (btnCloseDetail) btnCloseDetail.addEventListener('click', () => closeDetailPanel(true));

        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                if (isOpenFormVisible) { hideOpenForm(true); e.preventDefault(); return; }
                if (isCloseFormVisible) { hideCloseForm(true); e.preventDefault(); return; }
                if (isTransferFormVisible) { hideTransferForm(true); e.preventDefault(); return; }
                if (isComboFormVisible) { hideComboForm(true); e.preventDefault(); return; }
                const panel = document.getElementById('table-detail-panel');
                if (panel && !panel.classList.contains('d-none')) closeDetailPanel(true);
            }
        });

        const btnOpen = document.getElementById('btn-open-table');
        if (btnOpen) btnOpen.addEventListener('click', showOpenForm);

        const modeHourlyRadio = document.getElementById('mode-hourly');
        const modeComboRadio = document.getElementById('mode-combo');
        if (modeHourlyRadio) modeHourlyRadio.addEventListener('change', handleBillingModeChange);
        if (modeComboRadio) modeComboRadio.addEventListener('change', handleBillingModeChange);

        const btnConfirmOpen = document.getElementById('btn-confirm-open');
        if (btnConfirmOpen) btnConfirmOpen.addEventListener('click', handleConfirmOpenSession);

        const btnCancelOpen = document.getElementById('btn-cancel-open');
        if (btnCancelOpen) btnCancelOpen.addEventListener('click', () => hideOpenForm(true));

        const selectComboPackage = document.getElementById('select-combo-package');
        if (selectComboPackage) selectComboPackage.addEventListener('change', updateComboFormPreview);

        const btnClose = document.getElementById('btn-close-table');
        if (btnClose) btnClose.addEventListener('click', showCloseForm);

        const btnConfirmClose = document.getElementById('btn-confirm-close');
        if (btnConfirmClose) btnConfirmClose.addEventListener('click', handleConfirmCloseSession);

        const btnCancelClose = document.getElementById('btn-cancel-close');
        if (btnCancelClose) btnCancelClose.addEventListener('click', () => hideCloseForm(true));

        const btnRetryClose = document.getElementById('btn-retry-close-summary');
        if (btnRetryClose) {
            btnRetryClose.addEventListener('click', () => {
                if (currentSelectedTable.sessionId) loadCloseSummary(currentSelectedTable.sessionId, true);
            });
        }

        const btnTransfer = document.getElementById('btn-transfer');
        if (btnTransfer) btnTransfer.addEventListener('click', showTransferForm);

        const selectTargetTable = document.getElementById('select-target-table');
        if (selectTargetTable) selectTargetTable.addEventListener('change', updateTransferFormPreview);

        const btnConfirmTransfer = document.getElementById('btn-confirm-transfer');
        if (btnConfirmTransfer) btnConfirmTransfer.addEventListener('click', handleConfirmTransferSession);

        const btnCancelTransfer = document.getElementById('btn-cancel-transfer');
        if (btnCancelTransfer) btnCancelTransfer.addEventListener('click', () => hideTransferForm(true));

        document.addEventListener('visibilitychange', () => {
            if (!document.hidden && !isPollingStopped) {
                updateClocks();
                pollTableGrid();
            }
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        initEvents();
        updateTableFilterAndCounts(false);
        updateButtons();
        recalculateDeltaOffset();
        startClockTimer();
        startPolling();
        window.currentSelectedTable = currentSelectedTable;
        window.handleTableSelect = handleTableSelect;
        window.showToast = showToast;
        window.reloadTableGrid = reloadTableGrid;
    });
})();
