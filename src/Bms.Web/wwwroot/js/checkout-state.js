(() => {
    const state = document.getElementById('checkoutState');
    if (!state || !['Pending', 'Expired'].includes(state.dataset.status)) return;
    let submitting = false;
    let stopped = false;
    let cashMode = state.dataset.cashMode === 'true';
    const cashInput = document.getElementById('checkoutSwitchReceived');
    const cashForm = document.getElementById('checkoutSwitchCash');
    const amount = Number(state.dataset.amount);
    const forms = ['checkoutConfirmTransfer', 'checkoutReopenTransfer', 'checkoutSwitchCash']
        .map(id => document.getElementById(id)).filter(Boolean);
    const buttons = forms.flatMap(form => Array.from(form.querySelectorAll('button[type="submit"]')));
    function updateCash() {
        if (!cashInput || !cashForm) return;
        const received = Number(cashInput.value);
        const valid = cashInput.value !== '' && Number.isInteger(received) && received >= amount && received <= 100000000;
        cashInput.setCustomValidity(valid ? '' : 'Nhập số tiền nguyên, từ số tiền cần thanh toán đến 100.000.000 VND.');
        document.getElementById('checkoutCashChange').textContent = Math.max(0, received - amount).toLocaleString('vi-VN') + ' VND';
        cashForm.querySelectorAll('button[type="submit"]').forEach(button => { button.disabled = submitting || stopped || !valid; });
    }
    function showCash(value) {
        if (submitting || stopped) return;
        cashMode = value;
        document.getElementById('checkoutCashPanel').hidden = value !== true;
        document.getElementById('checkoutTransferActions').hidden = value;
        const qr = document.getElementById('vietQrBox');
        if (qr) qr.hidden = value || state.dataset.status !== 'Pending';
        updateCash();
    }
    document.getElementById('checkoutShowCash')?.addEventListener('click', () => showCash(true));
    document.getElementById('checkoutBackToQr')?.addEventListener('click', () => showCash(false));
    cashInput?.addEventListener('input', updateCash);
    if (cashForm) showCash(cashMode);
    forms.forEach(form => form.addEventListener('submit', event => {
        if (submitting || stopped || (cashForm && (form === cashForm) !== cashMode)) { event.preventDefault(); return; }
        if (form === cashForm && !cashInput.checkValidity()) { event.preventDefault(); return; }
        submitting = true;
        buttons.forEach(button => { button.disabled = true; });
        // Keep checked inputs enabled so the acknowledgement is included in POST.
    }));
    window.addEventListener('pageshow', event => {
        if (!event.persisted) return;
        window.location.reload();
    });
    async function poll() {
        if (stopped || submitting) return;
        try {
            const response = await fetch('/CashierSimulator/CheckoutStatus?referenceCode=' + encodeURIComponent(state.dataset.reference), { cache: 'no-store' });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) {
                document.getElementById('checkoutTransferMessage').textContent = 'Chưa kiểm tra được trạng thái. Tải lại trang; đăng nhập lại nếu cần.';
                return;
            }
            const result = await response.json();
            if (result.status === 'Expired') result.status = 'Pending'; // Older servers/records: no time-based QR expiry.
            if (result.status !== state.dataset.status) {
                stopped = true;
                buttons.forEach(button => { button.disabled = true; });
                document.getElementById('checkoutQrImage')?.remove();
                window.location.reload();
            }
        } catch {
            // The persisted status and manual refresh remain available while offline.
        } finally {
            if (!stopped && !submitting) window.setTimeout(poll, 5000);
        }
    }
    window.setTimeout(poll, 3000);
})();
