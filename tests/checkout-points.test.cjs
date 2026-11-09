const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');
function choice(checked = false) {
    const window = {};
    runInNewContext(readFileSync(join(__dirname, '../src/Bms.Web/wwwroot/js/checkout-points.js'), 'utf8'), { window });
    function element(properties) {
        const handlers = {};
        return { ...properties, addEventListener(type, fn) { handlers[type] = fn; }, fire(type) { handlers[type]?.(); } };
    }
    const toggle = element({ checked }), input = element({ value: '5' }), maxButton = element({});
    let changes = 0;
    const controller = window.initCheckoutPointsChoice({ toggle, input, maxButton, getMax: () => 50, onChange: () => changes++ });
    return { toggle, input, maxButton, controller, changes: () => changes };
}
test('default choice clears stale points, disables entry and maximum use', () => {
    const c = choice();
    assert.equal(c.input.value, '0'); assert.equal(c.input.disabled, true); assert.equal(c.maxButton.disabled, true);
    c.maxButton.fire('click'); assert.equal(c.input.value, '0'); assert.equal(c.changes(), 0);
});
test('opting in does not automatically choose points; explicit maximum is a separate action', () => {
    const c = choice();
    c.toggle.checked = true; c.toggle.fire('change');
    assert.equal(c.input.disabled, false); assert.equal(c.input.value, '0');
    c.maxButton.fire('click'); assert.equal(c.input.value, '50'); assert.equal(c.changes(), 2);
});
test('opting out removes a selected redemption and changing customers resets the choice', () => {
    const c = choice(true);
    assert.equal(c.input.value, '5');
    c.toggle.checked = false; c.toggle.fire('change'); assert.equal(c.input.value, '0');
    c.toggle.checked = true; c.toggle.fire('change'); c.maxButton.fire('click');
    c.controller.reset(); assert.equal(c.toggle.checked, false); assert.equal(c.input.value, '0'); assert.equal(c.input.disabled, true);
});
