const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');

const source = readFileSync(join(__dirname, '../src/Bms.Web/wwwroot/js/wallet.js'), 'utf8');

// Exercise form events without a browser or real wallet writes.
function wallet({ existing = true, method = 'Transfer', enabled = true, auto = true } = {}) {
    const elements = new Map();
    function element(id) {
        const handlers = new Map();
        const classes = new Set();
        const node = {
            dataset: {}, value: '', checked: false, disabled: false, required: false,
            textContent: '', submissions: 0,
            classList: {
                toggle(name, on) { if (on) classes.add(name); else classes.delete(name); },
                add(name) { classes.add(name); },
                remove(name) { classes.delete(name); },
                contains(name) { return classes.has(name); }
            },
            addEventListener(name, handler) { handlers.set(name, handler); },
            fire(name, extra = {}) {
                const event = { defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, ...extra };
                handlers.get(name)?.(event);
                return event;
            },
            checkValidity() { return true; },
            reportValidity() { return true; },
            requestSubmit() {
                if (this.reportValidity() && !this.fire('submit').defaultPrevented) this.submissions++;
            },
            remove() {}, closest() { return elements.get('body'); }
        };
        elements.set(id, node);
        return node;
    }
    for (const id of ['walletTopUpForm', 'walletPaymentMethod', 'topUpAmount', 'receivedCash',
        'walletPrepareStatus', 'walletCashConfirmation', 'walletCashActions', 'walletTransferHelp',
        'walletTransferActions', 'walletTopUpSubmit', 'workspace', 'body', 'window']) element(id);
    if (existing) for (const id of ['walletManualConfirmForm', 'receivedTransfer',
        'walletManualConfirmSubmit', 'walletTransferStatus', 'walletTransferStatusText', 'walletQrDetails']) element(id);
    const get = id => elements.get(id);
    const form = get('walletTopUpForm');
    form.dataset = { transferReference: existing ? 'NAPCK-TEST' : '', transferAmount: existing ? '2000000' : '', autoPrepare: String(auto) };
    get('walletPaymentMethod').value = method;
    const amount = get('topUpAmount');
    amount.value = '2000000';
    amount.checkValidity = () => Number.isInteger(Number(amount.value)) && Number(amount.value) >= 1000 && Number(amount.value) <= 100000000;
    form.reportValidity = () => amount.checkValidity() && (!get('receivedCash').required || get('receivedCash').checked);
    if (existing) get('walletTransferStatus').dataset = { reference: 'NAPCK-TEST', pending: 'true' };
    const timers = [];
    let reloads = 0;
    let response = { ok: true, headers: { get: () => 'application/json' }, json: async () => ({ status: 'Pending' }) };
    runInNewContext(source, {
        document: {
            currentScript: { dataset: { transferEnabled: String(enabled) } },
            getElementById: get,
            querySelector: () => get('workspace')
        },
        window: {
            addEventListener: get('window').addEventListener,
            setTimeout: callback => timers.push(callback),
            location: { reload() { reloads++; } }
        },
        fetch: async () => response,
        encodeURIComponent
    });
    return { get, form, amount, timers, reloads: () => reloads, setResponse: value => { response = value; } };
}

test('existing QR is reused and confirmation leaves the acknowledgement enabled for POST', () => {
    const w = wallet();
    assert.equal(w.form.submissions, 0);
    assert.equal(w.get('walletManualConfirmSubmit').disabled, false);
    w.get('receivedTransfer').checked = true;
    assert.equal(w.get('walletManualConfirmForm').fire('submit').defaultPrevented, false);
    assert.equal(w.get('receivedTransfer').disabled, false);
    assert.equal(w.get('receivedTransfer').checked, true);
    assert.equal(w.get('walletManualConfirmSubmit').disabled, true);
    assert.equal(w.get('walletManualConfirmForm').fire('submit').defaultPrevented, true);
    assert.equal(w.form.submissions, 0);
});

test('selecting transfer prepares a QR only once without requiring the cash acknowledgement', () => {
    const w = wallet({ existing: false, method: 'Cash' });
    assert.equal(w.form.submissions, 0);
    w.get('walletPaymentMethod').value = 'Transfer';
    w.get('walletPaymentMethod').fire('change');
    assert.equal(w.form.submissions, 1);
    assert.equal(w.get('receivedCash').disabled, true);
    w.amount.fire('change');
    assert.equal(w.form.submissions, 1);
});

test('editing the amount hides the old QR and blocks its confirmation before preparing a new one', () => {
    const w = wallet();
    w.get('receivedTransfer').checked = true;
    w.amount.value = '5000000';
    w.amount.fire('input');
    assert.equal(w.get('receivedTransfer').checked, false);
    assert.equal(w.get('walletManualConfirmSubmit').disabled, true);
    assert.equal(w.get('walletTransferStatus').classList.contains('d-none'), true);
    assert.equal(w.get('walletManualConfirmForm').fire('submit').defaultPrevented, true);
    w.amount.fire('change');
    assert.equal(w.form.submissions, 1);
});

test('invalid amounts, missing receiving account and server errors do not auto-submit', () => {
    const invalid = wallet();
    invalid.amount.value = '0';
    invalid.amount.fire('input');
    invalid.amount.fire('change');
    assert.equal(invalid.form.submissions, 0);
    assert.equal(invalid.get('walletManualConfirmSubmit').disabled, true);
    assert.equal(wallet({ existing: false, enabled: false }).form.submissions, 0);
    assert.equal(wallet({ existing: false, auto: false }).form.submissions, 0);
});

test('switching to cash hides QR and transfer confirmation and requires cash acknowledgement', () => {
    const w = wallet();
    w.get('walletPaymentMethod').value = 'Cash';
    w.get('walletPaymentMethod').fire('change');
    assert.equal(w.get('walletTransferStatus').classList.contains('d-none'), true);
    assert.equal(w.get('walletTransferActions').classList.contains('d-none'), true);
    assert.equal(w.get('walletCashActions').classList.contains('d-none'), false);
    assert.equal(w.get('receivedCash').required, true);
    assert.equal(w.get('receivedTransfer').disabled, true);
    w.form.requestSubmit();
    assert.equal(w.form.submissions, 0);
    w.get('receivedCash').checked = true;
    w.form.requestSubmit();
    assert.equal(w.form.submissions, 1);
});

test('poll confirmation cannot be re-enabled after editing and does not discard an edited amount', async () => {
    const w = wallet();
    w.amount.value = '5000000';
    w.amount.fire('input');
    w.setResponse({ ok: true, headers: { get: () => 'application/json' }, json: async () => ({ status: 'Confirmed' }) });
    await w.timers.shift()();
    assert.equal(w.reloads(), 0);
    w.amount.value = '2000000';
    w.amount.fire('input');
    assert.equal(w.get('walletManualConfirmSubmit').disabled, true);
    assert.equal(w.get('walletManualConfirmForm').fire('submit').defaultPrevented, true);
});

test('failed polling is not treated as a confirmed transfer', async () => {
    const w = wallet();
    w.setResponse({ ok: false });
    await w.timers.shift()();
    w.amount.fire('input');
    assert.equal(w.reloads(), 0);
    assert.equal(w.get('walletManualConfirmSubmit').disabled, false);
});
