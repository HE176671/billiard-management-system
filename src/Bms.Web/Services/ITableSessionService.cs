using Bms.Web.Models;

namespace Bms.Web.Services;

public interface ITableSessionService
{
    Task<List<TableCardViewModel>> GetTableCardsAsync();
    Task<TableDetailViewModel?> GetTableDetailAsync(int tableId);
    Task<TableOperationResult> OpenSessionAsync(int tableId, string staffId, int? bookingId = null);
    Task<TableOperationResult> CloseSessionAsync(int sessionId, string staffId);
    Task<CloseSummaryViewModel?> GetCloseSummaryAsync(int sessionId);
    Task<TableOperationResult> TransferSessionAsync(int sessionId, int newTableId, string staffId);
}
