/**
 * BMS - Quản lý bàn & Phiên chơi (Table Management)
 * Phiên chơi không giới hạn thời gian (tính tiền theo phút) & Ràng buộc trống bàn trước giờ Booking 5 phút
 */
(() => {
    'use strict';

    let currentSelectedTable = {
        id: null,
        code: '',
        status: '',
        displayStatus: '',
        sessionId: null,
        startAtUtc: null,
        endAtUtc: null,
        nextBookingId: null,
        nextBookingStartAtUtc: null,
        mustEndBeforeUtc: null,
        nextBookingCustomerName: null,
        nextBookingStatus: null,
        cardStatus: '',
        cardSessionStart: '',
        cardNextBookingStart: '',
        isBookingSession: false,
        segments: [],
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
    let isTransferring = false;
    let lastAvailableSignature = '';

    let detailEstimatedAmountSeconds = 0;
    let isEstimatedAmountRefreshing = false;
    let estimatedAmountRequestId = 0;

    let clockTimerId = null;
    let deltaOffset = 0;
    let isClockSkewCalculated = false;

    const notifiedBookingDeadlines = new Map();
    let isInitialScan = true;

    const POLL_INTERVAL = 10000;
    let pollTimerId = null;
    let isPolling = false;
    let isPollingStopped = false;
    let pollingAbortController = null;
    let pollingRequestId = 0;
    let hasNetworkErrorNotified = false;

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
        if (amount === null || amount === undefined || isNaN(amount)) return '0 ₫';
        return vnCurrencyFormatter.format(amount);
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

    /**
     * Tính trạng thái thời gian của bàn đối với lịch Booking sắp tới (bắt buộc nghỉ/chuyển trước 5 phút)
     */
    function getBookingDeadlineState(mustEndBeforeMs, nextBookingStartMs, nowMs, tableStatus) {
        if (!mustEndBeforeMs || isNaN(mustEndBeforeMs) || !nowMs || isNaN(nowMs)) {
            return {
                state: 'none',
                text: '--',
                panelText: '--'
            };
        }

        const bookingTimeStr = nextBookingStartMs ? formatVnTimeOnly(nextBookingStartMs) : '--:--';
        const deadlineStr = formatVnTimeOnly(mustEndBeforeMs);

        if (tableStatus !== 'InUse') {
            return {
                state: 'normal',
                text: `Đặt lúc ${bookingTimeStr}`,
                panelText: `Khách đặt ${bookingTimeStr} (trống trước ${deadlineStr})`
            };
        }

        const remainingToDeadlineMs = mustEndBeforeMs - nowMs;
        if (remainingToDeadlineMs > 900000) { // > 15 phút trước mốc 5p
            return {
                state: 'normal',
                text: `Nghỉ trước ${deadlineStr} (${formatRemainingTime(remainingToDeadlineMs)})`,
                panelText: `${formatRemainingTime(remainingToDeadlineMs)} (trước ${deadlineStr})`
            };
        } else if (remainingToDeadlineMs > 0) { // <= 15 phút trước mốc 5p
            return {
                state: 'warning',
                text: `Sắp tới Booking - còn ${formatRemainingTime(remainingToDeadlineMs)}`,
                panelText: `Còn ${formatRemainingTime(remainingToDeadlineMs)} (phải trống lúc ${deadlineStr})`
            };
        } else {
            const overdueMs = Math.abs(remainingToDeadlineMs);
            return {
                state: 'overdue',
                text: `Phải trả/chuyển bàn ngay (+${formatRemainingTime(overdueMs)})`,
                panelText: `Quá hạn ${formatRemainingTime(overdueMs)}! Phải đóng/chuyển bàn ngay`
            };
        }
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

    function updateButtons() {
        const btnOpen = document.getElementById('btn-open-table');
        const btnClose = document.getElementById('btn-close-table');
        const btnTransfer = document.getElementById('btn-transfer');

        if (isOpenFormVisible || isCloseFormVisible || isTransferFormVisible || isProcessing || isDetailLoading) {
            if (btnOpen) btnOpen.disabled = true;
            if (btnClose) btnClose.disabled = true;
            if (btnTransfer) btnTransfer.disabled = true;
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

    function checkBookingAlerts(cards) {
        if (!isClockSkewCalculated) return;
        const nowServerMs = Date.now() - deltaOffset;

        cards.forEach(card => {
            const status = card.getAttribute('data-status') || '';
            const mustEndStr = card.getAttribute('data-must-end-before');
            const nextBookingStr = card.getAttribute('data-next-booking-start');
            if (status !== 'InUse' || !mustEndStr) return;

            const mustEndMs = Date.parse(mustEndStr);
            const nextBookingMs = nextBookingStr ? Date.parse(nextBookingStr) : null;
            if (isNaN(mustEndMs)) return;

            const tableId = card.getAttribute('data-table-id');
            const tableCode = card.getAttribute('data-table-code') || '';
            const key = `${tableId}_${mustEndStr}`;

            const info = getBookingDeadlineState(mustEndMs, nextBookingMs, nowServerMs, status);
            const prev = notifiedBookingDeadlines.get(key);

            if (!prev || prev !== info.state) {
                notifiedBookingDeadlines.set(key, info.state);
                if (!isInitialScan) {
                    if (info.state === 'warning') {
                        showToast(`Bàn ${tableCode} sắp tới giờ khách đặt trước! Phải kết thúc hoặc chuyển bàn trước ${formatVnTimeOnly(mustEndMs)}.`, 'warning', 6000);
                    } else if (info.state === 'overdue') {
                        showToast(`Bàn ${tableCode} đã chạm mốc 5 phút trước giờ Booking! Bắt buộc đóng hoặc chuyển bàn ngay.`, 'danger', 7000);
                    }
                }
            }
        });

        isInitialScan = false;
    }

    function updateClocks() {
        const nowServerMs = Date.now() - deltaOffset;
        const cards = document.querySelectorAll('#table-grid-container .table-card');

        cards.forEach(card => {
            const status = card.getAttribute('data-status') || '';
            const displayStatus = card.getAttribute('data-display-status') || '';
            const clockEl = card.querySelector('.table-clock');
            const statusTextEl = card.querySelector('.status-text');
            const sessionStartStr = card.getAttribute('data-session-start');

            if (status === 'InUse' && sessionStartStr) {
                const startParsed = Date.parse(sessionStartStr);
                if (!isNaN(startParsed)) {
                    const diffMs = Math.max(0, nowServerMs - startParsed);
                    const totalSeconds = Math.floor(diffMs / 1000);
                    if (clockEl) clockEl.textContent = formatElapsedSeconds(totalSeconds);
                } else if (clockEl) {
                    clockEl.textContent = '--';
                }
            } else if (clockEl) {
                clockEl.textContent = '--';
            }

            if (statusTextEl) {
                statusTextEl.textContent = displayStatus;
            }

            const mustEndStr = card.getAttribute('data-must-end-before');
            const nextBookingStr = card.getAttribute('data-next-booking-start');
            const remainingEl = card.querySelector('.table-remaining');

            if (mustEndStr && remainingEl) {
                const mustEndMs = Date.parse(mustEndStr);
                const nextBookingMs = nextBookingStr ? Date.parse(nextBookingStr) : null;
                const deadlineInfo = getBookingDeadlineState(mustEndMs, nextBookingMs, nowServerMs, status);
                remainingEl.textContent = deadlineInfo.text;
                if (status === 'InUse' && (deadlineInfo.state === 'warning' || deadlineInfo.state === 'overdue')) {
                    card.setAttribute('data-time-state', deadlineInfo.state);
                } else {
                    card.removeAttribute('data-time-state');
                }
            } else {
                if (remainingEl) remainingEl.textContent = '--';
                card.removeAttribute('data-time-state');
            }
        });

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

            const elRemaining = document.getElementById('detail-remaining');
            const rowRemaining = document.getElementById('detail-remaining-row');
            if (elRemaining && rowRemaining) {
                if (currentSelectedTable.mustEndBeforeUtc && currentSelectedTable.status === 'InUse') {
                    const mustEndMs = Date.parse(currentSelectedTable.mustEndBeforeUtc);
                    const nextStartMs = currentSelectedTable.nextBookingStartAtUtc ? Date.parse(currentSelectedTable.nextBookingStartAtUtc) : null;
                    const info = getBookingDeadlineState(mustEndMs, nextStartMs, nowServerMs, currentSelectedTable.status);
                    elRemaining.textContent = info.panelText;
                    rowRemaining.classList.remove('d-none');
                } else {
                    rowRemaining.classList.add('d-none');
                }
            }
        }

        if (isOpenFormVisible) {
            updateOpenFormPreview();
        }

        checkBookingAlerts(cards);

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
            !isOpenFormVisible && !isCloseFormVisible && !isTransferFormVisible) {
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

            const boxPricing = document.getElementById('detail-pricing-box');
            const elPlaytimeLabel = document.getElementById('detail-playtime-label');
            const elPlaytimeAmount = document.getElementById('detail-playtime-amount');
            const estAmount = detail.estimatedAmount ?? detail.EstimatedAmount;
            const srvTime = detail.serverTimeUtc ?? detail.ServerTimeUtc;

            if (elPlaytimeLabel) elPlaytimeLabel.textContent = 'Tạm tính:';
            if (elPlaytimeAmount) {
                if (estAmount !== null && estAmount !== undefined) {
                    const timeStr = srvTime ? ` (cập nhật ${formatVnTimeWithSeconds(srvTime)})` : '';
                    elPlaytimeAmount.textContent = `${formatVnCurrency(estAmount)}${timeStr}`;
                } else {
                    elPlaytimeAmount.textContent = '--';
                }
            }
            if (boxPricing) boxPricing.classList.remove('d-none');
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
        currentSelectedTable.nextBookingId = detail.nextBookingId ?? detail.NextBookingId ?? null;
        currentSelectedTable.nextBookingStartAtUtc = detail.nextBookingStartAtUtc ?? detail.NextBookingStartAtUtc ?? null;
        currentSelectedTable.mustEndBeforeUtc = detail.mustEndBeforeUtc ?? detail.MustEndBeforeUtc ?? null;
        currentSelectedTable.nextBookingCustomerName = detail.nextBookingCustomerName ?? detail.NextBookingCustomerName ?? null;
        currentSelectedTable.nextBookingStatus = detail.nextBookingStatus ?? detail.NextBookingStatus ?? null;
        currentSelectedTable.isBookingSession = Boolean(detail.isBookingSession ?? detail.IsBookingSession);
        currentSelectedTable.hourlyRate = detail.hourlyRate ?? detail.HourlyRate ?? 0;
        currentSelectedTable.segments = detail.segments ?? detail.Segments ?? [];

        const card = document.querySelector(`.table-card[data-table-id="${currentSelectedTable.id}"]`);
        if (card) {
            currentSelectedTable.cardStatus = card.getAttribute('data-status') || '';
            currentSelectedTable.cardSessionStart = card.getAttribute('data-session-start') || '';
            currentSelectedTable.cardNextBookingStart = card.getAttribute('data-next-booking-start') || '';
        }

        const elTableName = document.getElementById('detail-table-name');
        const elStatus = document.getElementById('detail-status');
        const elCustomer = document.getElementById('detail-customer');
        const elBookingTime = document.getElementById('detail-booking-time');
        const elMustEndRow = document.getElementById('detail-must-end-row');
        const elMustEndTime = document.getElementById('detail-must-end-time');
        const elStartTime = document.getElementById('detail-start-time');
        const elEndTime = document.getElementById('detail-end-time');
        const rowEndTime = document.getElementById('detail-end-time-row');

        if (elTableName) elTableName.textContent = currentSelectedTable.code || '--';
        if (elStatus) elStatus.textContent = currentSelectedTable.displayStatus || '--';

        const customerName = detail.customerFullName ?? detail.CustomerFullName;
        if (elCustomer) elCustomer.textContent = customerName || '--';

        if (elBookingTime) {
            if (currentSelectedTable.nextBookingStartAtUtc) {
                const statusBadge = currentSelectedTable.nextBookingStatus === 'CheckedIn' ? ' (Đã Check-in)' : '';
                elBookingTime.textContent = `${formatVnDateTime(currentSelectedTable.nextBookingStartAtUtc)}${statusBadge}`;
            } else {
                elBookingTime.textContent = '--';
            }
        }

        if (elMustEndRow && elMustEndTime) {
            if (currentSelectedTable.mustEndBeforeUtc) {
                elMustEndTime.textContent = formatVnTimeOnly(currentSelectedTable.mustEndBeforeUtc);
                elMustEndRow.classList.remove('d-none');
            } else {
                elMustEndRow.classList.add('d-none');
            }
        }

        if (elStartTime) {
            elStartTime.textContent = currentSelectedTable.startAtUtc ? formatVnDateTime(currentSelectedTable.startAtUtc) : '--';
        }
        if (elEndTime) {
            elEndTime.textContent = currentSelectedTable.endAtUtc ? formatVnDateTime(currentSelectedTable.endAtUtc) : '--';
        }
        if (rowEndTime) {
            if (currentSelectedTable.endAtUtc || currentSelectedTable.status === 'AwaitingPayment') {
                rowEndTime.classList.remove('d-none');
            } else {
                rowEndTime.classList.add('d-none');
            }
        }

        const boxPricing = document.getElementById('detail-pricing-box');
        const elPlaytimeLabel = document.getElementById('detail-playtime-label');
        const elPlaytimeAmount = document.getElementById('detail-playtime-amount');
        const playtimeAmount = detail.playtimeAmount ?? detail.PlaytimeAmount;
        const estimatedAmount = detail.estimatedAmount ?? detail.EstimatedAmount;
        const serverTime = detail.serverTimeUtc ?? detail.ServerTimeUtc;

        if (currentSelectedTable.status === 'InUse') {
            if (elPlaytimeLabel) elPlaytimeLabel.textContent = 'Tạm tính:';
            if (elPlaytimeAmount) {
                if (estimatedAmount !== null && estimatedAmount !== undefined) {
                    const timeStr = serverTime ? ` (cập nhật ${formatVnTimeWithSeconds(serverTime)})` : '';
                    elPlaytimeAmount.textContent = `${formatVnCurrency(estimatedAmount)}${timeStr}`;
                } else {
                    elPlaytimeAmount.textContent = '--';
                }
            }
            if (boxPricing) boxPricing.classList.remove('d-none');
        } else if (currentSelectedTable.status === 'AwaitingPayment') {
            if (elPlaytimeLabel) elPlaytimeLabel.textContent = 'Tiền giờ (đã chốt):';
            if (elPlaytimeAmount) {
                elPlaytimeAmount.textContent = (playtimeAmount !== null && playtimeAmount !== undefined)
                    ? formatVnCurrency(playtimeAmount)
                    : '--';
            }
            if (boxPricing) boxPricing.classList.remove('d-none');
        } else {
            if (boxPricing) boxPricing.classList.add('d-none');
        }

        renderTableSegments(currentSelectedTable.segments);
        updateClocks();

        isDetailLoading = false;
        updateButtons();
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
            const endTimeStr = endUtc ? formatVnTimeOnly(endUtc) : 'đang chơi';

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
        const ids = ['detail-table-name', 'detail-status', 'detail-customer', 'detail-booking-time', 'detail-start-time', 'detail-end-time', 'detail-duration', 'detail-remaining'];
        ids.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.textContent = '--';
        });
        const hideIds = ['detail-end-time-row', 'detail-remaining-row', 'detail-pricing-box', 'detail-must-end-row'];
        hideIds.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.classList.add('d-none');
        });
        renderTableSegments([]);
    }

    function closeDetailPanel(returnFocus = true) {
        if (isOpenFormVisible) hideOpenForm(false);
        if (isCloseFormVisible) hideCloseForm(false);
        if (isTransferFormVisible) hideTransferForm(false);

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
            startAtUtc: null, endAtUtc: null, nextBookingId: null, nextBookingStartAtUtc: null,
            mustEndBeforeUtc: null, nextBookingCustomerName: null, nextBookingStatus: null,
            cardStatus: '', cardSessionStart: '', cardNextBookingStart: '',
            isBookingSession: false, segments: [], hourlyRate: 0
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
        const prevCardNextBooking = currentSelectedTable.cardNextBookingStart;

        container.innerHTML = html;
        recalculateDeltaOffset();
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
        const newCardNextBooking = card.getAttribute('data-next-booking-start') || '';

        if (isOpenFormVisible && newCardStatus !== 'Available') {
            hideOpenForm(false);
        }
        if (isCloseFormVisible && newCardStatus !== 'InUse') {
            hideCloseForm(false);
        }
        if (isTransferFormVisible && newCardStatus !== 'InUse') {
            hideTransferForm(false);
        }

        if (newCardStatus !== prevCardStatus || newCardSessionStart !== prevCardSessionStart || newCardNextBooking !== prevCardNextBooking) {
            if (!isOpenFormVisible && !isCloseFormVisible && !isTransferFormVisible) {
                await handleTableSelect(selectedId);
            }
        }
        updateButtons();
    }

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
            hasNetworkErrorNotified = false;
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

    function updateOpenFormPreview() {
        if (!isOpenFormVisible) return;
        const nowServerMs = Date.now() - deltaOffset;
        const elStart = document.getElementById('preview-open-start');
        if (elStart) elStart.textContent = formatVnTimeWithSeconds(nowServerMs);

        const rowLimit = document.getElementById('preview-booking-limit-row');
        const elMustEnd = document.getElementById('preview-must-end');
        if (rowLimit && elMustEnd) {
            if (currentSelectedTable.mustEndBeforeUtc && currentSelectedTable.nextBookingStatus !== 'CheckedIn') {
                elMustEnd.textContent = formatVnTimeOnly(currentSelectedTable.mustEndBeforeUtc);
                rowLimit.classList.remove('d-none');
            } else {
                rowLimit.classList.add('d-none');
            }
        }
    }

    function showOpenForm() {
        if (currentSelectedTable.status !== 'Available') return;
        if (isCloseFormVisible) hideCloseForm(false);
        if (isTransferFormVisible) hideTransferForm(false);

        isOpenFormVisible = true;
        const formEl = document.getElementById('open-session-form');
        const actionsBox = document.getElementById('action-buttons-box');
        if (formEl) formEl.classList.remove('d-none');
        if (actionsBox) actionsBox.classList.add('d-none');

        const bookingOptBox = document.getElementById('open-booking-option-box');
        const chkBooking = document.getElementById('chk-open-for-checkedin-booking');
        if (bookingOptBox && chkBooking) {
            if (currentSelectedTable.nextBookingId && currentSelectedTable.nextBookingStatus === 'CheckedIn') {
                bookingOptBox.classList.remove('d-none');
                chkBooking.checked = true;
            } else {
                bookingOptBox.classList.add('d-none');
                chkBooking.checked = false;
            }
        }

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

        isProcessing = true;
        updateButtons();

        const targetTableId = currentSelectedTable.id;
        const targetTableCode = currentSelectedTable.code;
        const chkBooking = document.getElementById('chk-open-for-checkedin-booking');
        const useBookingId = (chkBooking && chkBooking.checked && currentSelectedTable.nextBookingId && currentSelectedTable.nextBookingStatus === 'CheckedIn')
            ? currentSelectedTable.nextBookingId
            : null;

        try {
            const params = new URLSearchParams();
            params.append('TableId', targetTableId);
            if (useBookingId) {
                params.append('BookingId', useBookingId);
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
                showToast(`Đã mở bàn ${targetTableCode} lúc ${startUtc ? formatVnTimeOnly(startUtc) : '--:--'}.`, 'success');
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

    function showCloseForm() {
        if (currentSelectedTable.status !== 'InUse' || !currentSelectedTable.sessionId) return;
        if (isOpenFormVisible) hideOpenForm(false);
        if (isTransferFormVisible) hideTransferForm(false);

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
            const elAmount = document.getElementById('close-summary-amount');
            const elTimestamp = document.getElementById('close-summary-timestamp');

            const startAtUtc = data.startAtUtc ?? data.StartAtUtc;
            const serverNowUtc = data.serverNowUtc ?? data.ServerNowUtc;
            const billedMinutes = data.billedMinutes ?? data.BilledMinutes ?? 1;
            const estimatedAmount = data.estimatedAmount ?? data.EstimatedAmount ?? 0;

            if (elTable) elTable.textContent = data.tableCode ?? data.TableCode ?? currentSelectedTable.code;
            if (elStart) elStart.textContent = startAtUtc ? formatVnDateTime(startAtUtc) : '--';
            if (elEnd) elEnd.textContent = serverNowUtc ? formatVnDateTime(serverNowUtc) : '--';
            if (elMinutes) elMinutes.textContent = `${billedMinutes} phút`;
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
                const billedMinutes = data?.billedMinutes ?? data?.BilledMinutes ?? 0;
                showToast(`Đã đóng bàn ${targetTableCode} (${billedMinutes} phút). Tiền giờ: ${formatVnCurrency(playtimeAmount)}`, 'success');
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
                if (previewDiffTextEl) previewDiffTextEl.textContent = `Khác đơn giá: Tiền giờ được tính theo số phút thực tế ở từng bàn.`;
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
                isTransferring = true;
                hideTransferForm(false);
                const data = result.data ?? result.Data;
                const newTableCode = data?.newTableCode ?? data?.NewTableCode ?? '';
                const newTableId = data?.newTableId ?? data?.NewTableId ?? targetTableId;
                showToast(`Đã chuyển bàn ${oldTableCode} sang ${newTableCode}.`, 'success');
                currentSelectedTable.id = null;
                await reloadTableGrid();
                await handleTableSelect(newTableId);
                isTransferring = false;
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

        const btnCloseDetail = document.getElementById('btn-close-detail');
        if (btnCloseDetail) btnCloseDetail.addEventListener('click', () => closeDetailPanel(true));

        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                if (isOpenFormVisible) { hideOpenForm(true); e.preventDefault(); return; }
                if (isCloseFormVisible) { hideCloseForm(true); e.preventDefault(); return; }
                if (isTransferFormVisible) { hideTransferForm(true); e.preventDefault(); return; }
                const panel = document.getElementById('table-detail-panel');
                if (panel && !panel.classList.contains('d-none')) closeDetailPanel(true);
            }
        });

        const btnOpen = document.getElementById('btn-open-table');
        if (btnOpen) btnOpen.addEventListener('click', showOpenForm);

        const btnConfirmOpen = document.getElementById('btn-confirm-open');
        if (btnConfirmOpen) btnConfirmOpen.addEventListener('click', handleConfirmOpenSession);

        const btnCancelOpen = document.getElementById('btn-cancel-open');
        if (btnCancelOpen) btnCancelOpen.addEventListener('click', () => hideOpenForm(true));

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
        updateButtons();
        recalculateDeltaOffset();
        startClockTimer();
        startPolling();
    });
})();
