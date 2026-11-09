(function () {
    'use strict';

    const ALLOWED_CONTROL_KEYS = new Set([
        'Backspace', 'Delete', 'Tab', 'Escape', 'Enter',
        'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown',
        'Home', 'End'
    ]);

    function getOrCreateWarningEl(input) {
        const container = input.closest('.input-group') ? input.closest('.input-group').parentElement : input.parentElement;
        if (!container) return null;
        let warn = container.querySelector('.bms-inline-num-warning');
        if (!warn) {
            warn = document.createElement('div');
            warn.className = 'bms-inline-num-warning text-danger fw-semibold mt-1 d-none';
            warn.style.fontSize = '0.76rem';
            const anchor = input.closest('.input-group') || input;
            if (anchor.nextSibling) {
                container.insertBefore(warn, anchor.nextSibling);
            } else {
                container.appendChild(warn);
            }
        }
        return warn;
    }

    function showWarning(input, message) {
        const warn = getOrCreateWarningEl(input);
        input.classList.add('is-invalid');
        input.dataset.bmsInvalid = 'true';
        if (warn) {
            warn.innerHTML = '<i class="bi bi-exclamation-triangle-fill me-1"></i>' + message;
            warn.classList.remove('d-none');
        }
    }

    function clearWarning(input) {
        const warn = getOrCreateWarningEl(input);
        input.classList.remove('is-invalid');
        delete input.dataset.bmsInvalid;
        if (warn) {
            warn.textContent = '';
            warn.classList.add('d-none');
        }
    }

    function showReadonlyNotice(input) {
        if (!input.readOnly || !input.dataset.readonlyMessage) return;
        clearWarning(input);
        const container = input.parentElement;
        let notice = container.querySelector('.bms-readonly-notice');
        if (!notice) {
            notice = document.createElement('div');
            notice.className = 'bms-readonly-notice alert alert-warning py-2 px-2 mt-2 mb-0 small';
            notice.setAttribute('role', 'status');
            notice.setAttribute('aria-live', 'polite');
            notice.id = input.id + '-readonly-notice';
            container.insertBefore(notice, input.nextSibling);
            input.setAttribute('aria-describedby', [input.getAttribute('aria-describedby'), notice.id].filter(Boolean).join(' '));
        }
        notice.textContent = input.dataset.readonlyMessage;
    }

    function validateNumberInput(input, triggeredByBadChar, badCharReason) {
        if (input.readOnly || input.disabled) {
            clearWarning(input);
            return true;
        }
        const allowSigned = input.getAttribute('data-allow-signed') === 'true';
        const raw = (input.value || '').trim();
        const minAttr = input.getAttribute('min');
        const minVal = (minAttr !== null && minAttr !== '' && !isNaN(Number(minAttr))) ? Number(minAttr) : (allowSigned ? null : 0);

        if (triggeredByBadChar && badCharReason) {
            showWarning(input, badCharReason);
            return false;
        }

        if (input.validity && input.validity.badInput) {
            showWarning(input, 'Cảnh báo: Không được nhập chữ cái, số âm hoặc ký tự đặc biệt! Chỉ được nhập số hợp lệ.');
            return false;
        }

        if (raw === '') {
            if (input.hasAttribute('required')) {
                // Clear transient char warning when empty unless submitting
                clearWarning(input);
            } else {
                clearWarning(input);
            }
            return true;
        }

        const pattern = allowSigned ? /^-?\d+(\.\d+)?$/ : /^\d+(\.\d+)?$/;
        if (!pattern.test(raw)) {
            if (raw.startsWith('-')) {
                showWarning(input, 'Cảnh báo: Không được nhập số âm! Vui lòng nhập giá trị từ 0 trở lên.');
            } else {
                showWarning(input, 'Cảnh báo: Không được nhập ký tự đặc biệt hoặc chữ cái! Chỉ được nhập số từ 0 trở lên.');
            }
            return false;
        }

        const num = Number(raw);
        if (!Number.isFinite(num)) {
            showWarning(input, 'Cảnh báo: Giá trị số không hợp lệ!');
            return false;
        }

        if (!allowSigned && num < 0) {
            showWarning(input, 'Cảnh báo: Không được nhập số âm! Vui lòng nhập từ 0 trở lên.');
            return false;
        }

        if (minVal !== null && num < minVal) {
            showWarning(input, `Cảnh báo: Giá trị phải từ ${minVal.toLocaleString('vi-VN')} trở lên.`);
            return false;
        }

        if (allowSigned && num === 0) {
            showWarning(input, 'Cảnh báo: Số điểm điều chỉnh phải khác 0 (nhập số dương để cộng, số âm để trừ).');
            return false;
        }

        clearWarning(input);
        return true;
    }

    function attachInputValidator(input) {
        if (input.dataset.bmsBound === 'true') return;
        input.dataset.bmsBound = 'true';

        const allowSigned = input.getAttribute('data-allow-signed') === 'true';
        if (!allowSigned) {
            const currentMin = input.getAttribute('min');
            if (currentMin === null || currentMin === '') {
                input.setAttribute('min', '0');
            }
        }

        input.addEventListener('click', () => showReadonlyNotice(input));
        input.addEventListener('focus', () => showReadonlyNotice(input));
        input.addEventListener('beforeinput', function (e) {
            if (input.readOnly) {
                e.preventDefault();
                showReadonlyNotice(input);
            }
        });
        input.addEventListener('keydown', function (e) {
            if (input.disabled) return;
            if (input.readOnly) {
                const copyOrSelect = (e.ctrlKey || e.metaKey) && ['a', 'c'].includes(e.key.toLowerCase());
                const navigation = ALLOWED_CONTROL_KEYS.has(e.key) && !['Backspace', 'Delete'].includes(e.key);
                if (!copyOrSelect && !navigation && (e.key.length === 1 || ['Backspace', 'Delete'].includes(e.key))) {
                    e.preventDefault();
                    showReadonlyNotice(input);
                }
                return;
            }
            if (e.ctrlKey || e.metaKey || e.altKey) return;
            if (ALLOWED_CONTROL_KEYS.has(e.key)) return;

            if (e.key === '-') {
                if (!allowSigned) {
                    showWarning(input, 'Cảnh báo: Không được nhập số âm! Chỉ được nhập số từ 0 trở lên.');
                }
                return;
            }

            if (e.key === '+' || e.key === 'e' || e.key === 'E') {
                e.preventDefault();
                showWarning(input, 'Cảnh báo: Không được nhập ký tự đặc biệt (' + e.key + ')! Chỉ được nhập chữ số từ 0 trở lên.');
                return;
            }

            if (e.key.length === 1 && !/[0-9.]/.test(e.key)) {
                e.preventDefault();
                showWarning(input, 'Cảnh báo: Không được nhập chữ cái hoặc ký tự đặc biệt! Chỉ được nhập chữ số từ 0 trở lên.');
            }
        });

        input.addEventListener('paste', function (e) {
            if (input.disabled) return;
            if (input.readOnly) {
                e.preventDefault();
                showReadonlyNotice(input);
                return;
            }
            const pasted = (e.clipboardData || window.clipboardData)?.getData('text') || '';
            const trimmed = pasted.trim();
            const pattern = allowSigned ? /^-?\d+(\.\d+)?$/ : /^\d+(\.\d+)?$/;
            if (!pattern.test(trimmed)) {
                e.preventDefault();
                if (trimmed.startsWith('-')) {
                    showWarning(input, 'Cảnh báo: Không được dán số âm! Chỉ được nhập số từ 0 trở lên.');
                } else {
                    showWarning(input, 'Cảnh báo: Nội dung dán chứa chữ cái hoặc ký tự đặc biệt không hợp lệ!');
                }
            }
        });

        input.addEventListener('input', function () {
            validateNumberInput(input, false, null);
        });

        input.addEventListener('change', function () {
            validateNumberInput(input, false, null);
        });
    }

    function initValidators() {
        const inputs = document.querySelectorAll('input[type="number"], input[data-non-negative="true"]');
        inputs.forEach(attachInputValidator);

        document.querySelectorAll('form').forEach(form => {
            if (form.dataset.bmsFormBound === 'true') return;
            form.dataset.bmsFormBound = 'true';

            form.addEventListener('submit', function (e) {
                const numInputs = form.querySelectorAll('input[type="number"], input[data-non-negative="true"]');
                let firstInvalid = null;
                numInputs.forEach(inp => {
                    if (inp.disabled || inp.readOnly || inp.type === 'hidden') {
                        clearWarning(inp);
                        return;
                    }
                    const raw = (inp.value || '').trim();
                    if (inp.hasAttribute('required') && raw === '' && !(inp.validity && inp.validity.badInput)) {
                        showWarning(inp, 'Cảnh báo: Vui lòng nhập số hợp lệ (từ 0 trở lên).');
                        if (!firstInvalid) firstInvalid = inp;
                        return;
                    }
                    if (!validateNumberInput(inp, false, null)) {
                        if (!firstInvalid) firstInvalid = inp;
                    }
                });
                if (firstInvalid) {
                    e.preventDefault();
                    e.stopPropagation();
                    firstInvalid.focus();
                }
            });
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initValidators);
    } else {
        initValidators();
    }
})();
