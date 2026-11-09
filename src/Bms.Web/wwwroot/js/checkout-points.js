window.initCheckoutPointsChoice = ({ toggle, input, maxButton, getMax, onChange }) => {
    if (!toggle || !input) return { reset() {} };
    function sync() {
        input.disabled = !toggle.checked;
        if (maxButton) maxButton.disabled = !toggle.checked;
        if (!toggle.checked) input.value = '0';
    }
    toggle.addEventListener('change', () => { sync(); onChange(); });
    maxButton?.addEventListener('click', () => {
        if (!toggle.checked) return;
        input.value = String(getMax());
        onChange();
    });
    sync();
    return { reset() { toggle.checked = false; sync(); } };
};
