const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');
const source = readFileSync(join(__dirname, '../src/Bms.Web/wwwroot/js/checkout-state.js'), 'utf8');

function checkout(status = 'Pending', cash = false) {
    const elements = new Map();
    function node(id) {
        const listeners = new Map();
        const button = { disabled: false };
        const entry = { dataset: {}, textContent: '', removed: false, button, value: '400000', hidden: false, validityMessage: '',
            setCustomValidity(message) { this.validityMessage = message; }, checkValidity() { return !this.validityMessage; },
            addEventListener: (name, callback) => listeners.set(name, callback),
            querySelectorAll: () => [button],
            fire(name, data = {}) {
                const event = { defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, ...data };
                listeners.get(name)?.(event);
                return event;
            }, remove() { this.removed = true; } };
        elements.set(id, entry);
        return entry;
    }
    ['checkoutState', 'checkoutConfirmTransfer', 'checkoutReopenTransfer', 'checkoutTransferMessage', 'checkoutQrImage', 'window',
        'checkoutSwitchCash', 'checkoutCashPanel', 'checkoutTransferActions', 'checkoutSwitchReceived', 'checkoutCashChange', 'checkoutShowCash', 'checkoutBackToQr', 'vietQrBox'].forEach(node);
    const get = id => elements.get(id);
    get('checkoutState').dataset = { status, reference: 'HD123', amount: '380000', cashMode: String(cash) };
    const timers = [];
    let reloads = 0;
    let response = { ok: true, headers: { get: () => 'application/json' }, json: async () => ({ status }) };
    runInNewContext(source, {
        document: { getElementById: get }, encodeURIComponent,
        window: { addEventListener: get('window').addEventListener, setTimeout: callback => timers.push(callback), location: { reload() { reloads++; } } },
        fetch: async () => response
    });
    return { get, timers, reloads: () => reloads, respond: value => { response = value; } };
}

test('confirm and edit cannot both submit, and acknowledgement controls are untouched', () => {
    const c = checkout();
    assert.equal(c.get('checkoutConfirmTransfer').fire('submit').defaultPrevented, false);
    assert.equal(c.get('checkoutReopenTransfer').button.disabled, true);
    assert.equal(c.get('checkoutReopenTransfer').fire('submit').defaultPrevented, true);
    assert.equal(c.get('checkoutConfirmTransfer').fire('submit').defaultPrevented, true);
});

test('legacy expiry never removes the QR or blocks staff confirmation', async () => {
    const c = checkout();
    c.respond({ ok: true, headers: { get: () => 'application/json' }, json: async () => ({ status: 'Expired' }) });
    await c.timers.shift()();
    assert.equal(c.get('checkoutQrImage').removed, false);
    assert.equal(c.get('checkoutConfirmTransfer').button.disabled, false);
    assert.equal(c.reloads(), 0);
    assert.equal(c.timers.length, 1);
});

test('another staff confirmation refreshes an expired page to the saved receipt', async () => {
    const c = checkout('Expired');
    c.respond({ ok: true, headers: { get: () => 'application/json' }, json: async () => ({ status: 'Confirmed' }) });
    await c.timers.shift()();
    assert.equal(c.reloads(), 1);
    assert.equal(c.get('checkoutConfirmTransfer').fire('submit').defaultPrevented, true);
});

test('network failure does not mark a payment confirmed or hide its manual confirmation', async () => {
    const c = checkout();
    c.respond({ ok: false });
    await c.timers.shift()();
    assert.equal(c.reloads(), 0);
    assert.equal(c.get('checkoutConfirmTransfer').button.disabled, false);
    assert.ok(c.get('checkoutTransferMessage').textContent);
    assert.equal(c.timers.length, 1);
});

test('browser back restores server state, and paid pages do not poll', () => {
    const c = checkout();
    c.get('window').fire('pageshow', { persisted: true });
    assert.equal(c.reloads(), 1);
    assert.equal(checkout('Confirmed').timers.length, 0);
});

test('opening cash hides QR, retains the invoice and never submits; back restores QR', () => {
    const c = checkout();
    c.get('checkoutShowCash').fire('click');
    assert.equal(c.get('vietQrBox').hidden, true);
    assert.equal(c.get('checkoutCashPanel').hidden, false);
    assert.equal(c.get('checkoutTransferActions').hidden, true);
    assert.equal(c.get('checkoutConfirmTransfer').fire('submit').defaultPrevented, true);
    assert.equal(c.get('checkoutSwitchCash').button.disabled, false);
    assert.equal(c.reloads(), 0);
    c.get('checkoutBackToQr').fire('click');
    assert.equal(c.get('vietQrBox').hidden, false);
    assert.equal(c.get('checkoutSwitchCash').fire('submit').defaultPrevented, true);
});

test('cash requires enough integer money and only one of cash or bank forms can submit', () => {
    const c = checkout('Pending', true);
    const input = c.get('checkoutSwitchReceived');
    for (const value of ['1', '', '380000.5', '100000001']) {
        input.value = value; input.fire('input');
        assert.equal(c.get('checkoutSwitchCash').button.disabled, true);
        assert.equal(c.get('checkoutSwitchCash').fire('submit').defaultPrevented, true);
    }
    input.value = '500000'; input.fire('input');
    assert.equal(c.get('checkoutCashChange').textContent, '120.000 VND');
    assert.equal(c.get('checkoutSwitchCash').fire('submit').defaultPrevented, false);
    assert.equal(c.get('checkoutSwitchCash').fire('submit').defaultPrevented, true);
    assert.equal(c.get('checkoutConfirmTransfer').fire('submit').defaultPrevented, true);
});

test('QR expiry during cash entry retains input and permits cash confirmation', async () => {
    const c = checkout('Pending', true);
    c.get('checkoutSwitchReceived').value = '500000';
    c.respond({ ok: true, headers: { get: () => 'application/json' }, json: async () => ({ status: 'Expired' }) });
    await c.timers.shift()();
    assert.equal(c.reloads(), 0);
    assert.equal(c.get('checkoutSwitchReceived').value, '500000');
    assert.equal(c.get('checkoutQrImage').removed, false);
    assert.equal(c.get('checkoutSwitchCash').fire('submit').defaultPrevented, false);
});

test('bank confirmation or another cashier completion disables cash immediately and loads receipt', async () => {
    for (const status of ['Confirmed', 'Cancelled', 'Failed']) {
        const c = checkout('Pending', true);
        c.respond({ ok: true, headers: { get: () => 'application/json' }, json: async () => ({ status }) });
        await c.timers.shift()();
        assert.equal(c.get('checkoutSwitchCash').button.disabled, true);
        assert.equal(c.get('checkoutSwitchCash').fire('submit').defaultPrevented, true);
        assert.equal(c.reloads(), 1);
    }
});
