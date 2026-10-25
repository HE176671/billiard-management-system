using Bms.Web.Models;

namespace Bms.Web.Services;

public interface IBookingService
{
    Task<int> AutoExpireOverdueBookingsAsync(CancellationToken cancellationToken = default);

    // Chức năng cho Khách hàng (Customer)
    Task<List<BookableTableOptionViewModel>> GetBookableTablesAsync(DateTime startAtUtc);
    Task<List<CustomerBookingItemViewModel>> GetCustomerBookingsAsync(string customerId);
    Task<TableOperationResult> CreateBookingAsync(string customerId, int tableId, DateTime startAtUtc, string? notes);
    Task<TableOperationResult> CancelBookingAsync(int bookingId, string actorId, string? reason);

    // Chức năng cho Nhân viên (Staff / Admin)
    Task<StaffBookingManagementViewModel> SearchBookingsForStaffAsync(string? keyword, string? statusFilter, DateTime? dateLocal);
    Task<TableOperationResult> CheckInBookingAsync(int bookingId, string staffId, bool allowEarly = false);
    Task<TableOperationResult> AssignTableAndOpenSessionAsync(int bookingId, int selectedTableId, string staffId);
}
