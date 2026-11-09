/**
 * TV5 - Sơ đồ bàn realtime qua SignalR (/hubs/table).
 * Khi máy khác mở/đóng/chuyển bàn, server phát "TablesChanged" và trang này tải lại lưới bàn ngay.
 * Dùng window.reloadTableGrid() do table-management.js cung cấp; polling 10 giây vẫn giữ làm dự phòng.
 */
(() => {
    'use strict';
    let timer = null;

    function scheduleReload() {
        if (timer) clearTimeout(timer);
        timer = setTimeout(() => {
            timer = null;
            if (typeof window.reloadTableGrid === 'function') window.reloadTableGrid();
        }, 300);
    }

    document.addEventListener('DOMContentLoaded', () => {
        if (typeof signalR === 'undefined') return;
        const conn = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/table')
            .withAutomaticReconnect()
            .build();
        conn.on('TablesChanged', scheduleReload);
        conn.onreconnected(scheduleReload);
        conn.start().catch(() => { /* polling 10 giây vẫn hoạt động */ });
    });
})();
