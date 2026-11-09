const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { runInNewContext } = require('node:vm');

function fixture({ readOnly = false, disabled = false, value = '2000' } = {}) {
    function element() {
        const attributes = {}, events = {};
        const el = { dataset: {}, style: {}, children: [], className: '',
            getAttribute(name) { return attributes[name] ?? null; },
            hasAttribute(name) { return Object.hasOwn(attributes, name); },
            setAttribute(name, value) { attributes[name] = String(value); },
            addEventListener(type, fn) { (events[type] ??= []).push(fn); },
            fire(type, args = {}) {
                const event = { defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, stopPropagation() {}, ...args };
                for (const fn of events[type] ?? []) fn(event);
                return event;
            },
            closest() { return null; },
            querySelector(selector) { return this.children.find(child => child.classList.contains(selector.slice(1))) ?? null; },
            appendChild(child) { this.children.push(child); child.parentElement = this; },
            insertBefore(child) { this.appendChild(child); },
            focus() { this.fire('focus'); }
        };
        el.classList = {
            contains(name) { return el.className.split(' ').includes(name); },
            add(name) { if (!this.contains(name)) el.className += ' ' + name; },
            remove(name) { el.className = el.className.split(' ').filter(c => c !== name).join(' '); }
        };
        return el;
    }
    const input = Object.assign(element(), { id: 'inpPlaytime', type: 'number', value, readOnly, disabled, validity: {} });
    input.setAttribute('required', '');
    input.dataset.readonlyMessage = 'Không được phép sửa tiền giờ khi đã chọn bàn. Bấm Bỏ chọn bàn để chuyển sang nhập thủ công.';
    const container = element(); container.appendChild(input);
    const form = element(); form.querySelectorAll = () => [input];
    const document = { readyState: 'complete', createElement: element, querySelectorAll: selector => selector === 'form' ? [form] : [input] };
    runInNewContext(readFileSync(join(__dirname, '../src/Bms.Web/wwwroot/js/bms-number-validator.js'), 'utf8'), { document, window: {} });
    return { input, form, container, notice: () => container.querySelector('.bms-readonly-notice'), warning: () => container.querySelector('.bms-inline-num-warning') };
}

test('clicking or focusing a table amount explains the lock without marking valid data invalid', () => {
    const f = fixture({ readOnly: true });
    f.input.fire('click'); f.input.fire('focus');
    assert.match(f.notice().textContent, /Không được phép sửa/);
    assert.equal(f.notice().getAttribute('role'), 'status');
    assert.equal(f.input.getAttribute('aria-describedby'), f.notice().id);
    assert.equal(f.container.children.filter(el => el.classList.contains('bms-readonly-notice')).length, 1);
    assert.equal(f.input.classList.contains('is-invalid'), false);
    assert.equal(f.input.dataset.bmsInvalid, undefined);
});

test('typing, deleting and pasting into a locked amount show a permission notice and cannot change the value', () => {
    const f = fixture({ readOnly: true });
    for (const key of ['a', '1', '-', 'e', 'Backspace', 'Delete']) {
        assert.equal(f.input.fire('keydown', { key }).defaultPrevented, true);
    }
    assert.equal(f.input.fire('paste', { clipboardData: { getData: () => '123' } }).defaultPrevented, true);
    assert.equal(f.input.fire('beforeinput').defaultPrevented, true);
    assert.match(f.notice().textContent, /Không được phép sửa/);
    assert.equal(f.input.value, '2000');
    assert.equal(f.input.dataset.bmsInvalid, undefined);
    assert.equal(f.form.fire('submit').defaultPrevented, false);
});

test('locked inputs allow tab, navigation and copy/select shortcuts', () => {
    const f = fixture({ readOnly: true });
    for (const key of ['Tab', 'ArrowLeft', 'ArrowRight', 'Home', 'End']) {
        assert.equal(f.input.fire('keydown', { key }).defaultPrevented, false);
    }
    for (const key of ['c', 'a']) assert.equal(f.input.fire('keydown', { key, ctrlKey: true }).defaultPrevented, false);
});

test('locked or disabled controls never block submit with numeric or required warnings', () => {
    for (const state of [{ readOnly: true }, { disabled: true }]) {
        const f = fixture({ ...state, value: '' });
        f.input.classList.add('is-invalid'); f.input.dataset.bmsInvalid = 'true';
        f.input.validity.badInput = true;
        f.input.fire('change');
        assert.equal(f.form.fire('submit').defaultPrevented, false);
        assert.equal(f.input.classList.contains('is-invalid'), false);
        assert.equal(f.input.dataset.bmsInvalid, undefined);
    }
});

test('manual amounts remain editable and still reject letters and invalid pasted numbers', () => {
    const f = fixture();
    f.input.fire('click');
    assert.equal(f.notice(), null);
    assert.equal(f.input.fire('keydown', { key: '5' }).defaultPrevented, false);
    assert.equal(f.input.fire('keydown', { key: 'a' }).defaultPrevented, true);
    assert.equal(f.input.dataset.bmsInvalid, 'true');
    assert.equal(f.input.fire('paste', { clipboardData: { getData: () => '-10' } }).defaultPrevented, true);
    f.input.value = '-10';
    assert.equal(f.form.fire('submit').defaultPrevented, true);
    f.input.value = '50000'; f.input.fire('input');
    assert.equal(f.input.dataset.bmsInvalid, undefined);
    assert.equal(f.form.fire('submit').defaultPrevented, false);
});
