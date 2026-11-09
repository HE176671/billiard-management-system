(() => {
    const enabled = document.currentScript.dataset.transferEnabled === 'true';
    const form = document.getElementById('walletTopUpForm');
    const method = document.getElementById('walletPaymentMethod');
    const amount = document.getElementById('topUpAmount');
    const cashCheck = document.getElementById('receivedCash');
    const confirmForm = document.getElementById('walletManualConfirmForm');
    const transferCheck = document.getElementById('receivedTransfer');
    const confirmButton = document.getElementById('walletManualConfirmSubmit');
    const prepareStatus = document.getElementById('walletPrepareStatus');
    const panel = document.getElementById('walletTransferStatus');
    let submitting = false;
    let transferConfirmed = false;

    function matchesCurrentTransfer() {
        return !!form?.dataset.transferReference && amount.checkValidity()
            && Number(amount.value) === Number(form.dataset.transferAmount);
    }

    function updateMethod() {
        if (!form) return;
        const cash = method.value === 'Cash';
        const matches = matchesCurrentTransfer();
        document.getElementById('walletCashConfirmation').classList.toggle('d-none', !cash);
        document.getElementById('walletCashActions').classList.toggle('d-none', !cash);
        document.getElementById('walletTransferHelp').classList.toggle('d-none', cash);
        document.getElementById('walletTransferActions').classList.toggle('d-none', cash);
        cashCheck.required = cash;
        cashCheck.disabled = !cash;
        if (transferCheck) {
            transferCheck.disabled = cash || !matches || submitting || transferConfirmed;
            transferCheck.required = !cash && matches;
            if (transferCheck.disabled) transferCheck.checked = false;
            confirmButton.disabled = transferCheck.disabled;
        }
        // Never show a QR for a different amount while the staff edits the deposit.
        panel?.classList.toggle('d-none', cash || !matches);
        document.querySelector('.wallet-workspace')?.classList.toggle('has-transfer', !!panel && !cash && matches);
        if (prepareStatus) prepareStatus.textContent = !cash && !matches && enabled
            ? 'Nhập số tiền hợp lệ; QR sẽ cập nhật khi bạn hoàn tất nhập.' : '';
    }

    function prepareQr() {
        if (!form || submitting || method.value !== 'Transfer' || !enabled || matchesCurrentTransfer()) return;
        if (form.reportValidity()) form.requestSubmit();
    }

    form?.addEventListener('submit', event => {
        if (submitting) { event.preventDefault(); return; }
        if (method.value === 'Transfer' && (!enabled || matchesCurrentTransfer())) {
            event.preventDefault();
            return;
        }
        submitting = true;
        document.getElementById('walletTopUpSubmit').disabled = true;
        if (transferCheck) transferCheck.disabled = true;
        if (confirmButton) confirmButton.disabled = true;
        if (method.value === 'Transfer' && prepareStatus) prepareStatus.textContent = 'Đang cập nhật QR…';
    });
    method?.addEventListener('change', () => {
        if (transferCheck) transferCheck.checked = false;
        updateMethod();
        prepareQr();
    });
    amount?.addEventListener('input', () => {
        if (transferCheck) transferCheck.checked = false;
        updateMethod();
    });
    amount?.addEventListener('change', prepareQr);
    updateMethod();
    if (form?.dataset.autoPrepare === 'true') prepareQr();

    confirmForm?.addEventListener('submit', event => {
        if (submitting || transferConfirmed || method.value !== 'Transfer' || !matchesCurrentTransfer()) {
            event.preventDefault();
            updateMethod();
            return;
        }
        submitting = true;
        confirmButton.disabled = true;
        confirmButton.textContent = 'Đang ghi nhận…';
    });

    // Restore controls if the browser returns to this page from its back/forward cache.
    window.addEventListener('pageshow', event => {
        if (!event.persisted || !form) return;
        submitting = false;
        document.getElementById('walletTopUpSubmit').disabled = false;
        if (transferCheck) transferCheck.checked = false;
        if (confirmButton) confirmButton.textContent = 'Xác nhận đã nhận tiền';
        updateMethod();
    });

    if (!panel || panel.dataset.pending !== 'true') return;
    let stopped = false;
    async function poll() {
        if (stopped || submitting) return;
        try {
            const response = await fetch('/Wallet/TransferStatus?referenceCode=' + encodeURIComponent(panel.dataset.reference), { cache: 'no-store' });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) {
                document.getElementById('walletTransferStatusText').textContent = 'Chưa kiểm tra được trạng thái. Hãy tải lại trang hoặc đăng nhập lại.';
                stopped = true;
                return;
            }
            const result = await response.json();
            if (result.status === 'Confirmed') {
                stopped = true;
                transferConfirmed = true;
                if (transferCheck) transferCheck.disabled = true;
                if (confirmButton) confirmButton.disabled = true;
                // A pending confirmation has completed; show its persisted balance and audit.
                if (!form || (method.value === 'Transfer' && matchesCurrentTransfer())) window.location.reload();
            }
        } catch {
            document.getElementById('walletTransferStatusText').textContent = 'Đang kết nối lại để kiểm tra trạng thái…';
        } finally {
            if (!stopped) window.setTimeout(poll, 5000);
        }
    }
    window.setTimeout(poll, 3000);
})();
