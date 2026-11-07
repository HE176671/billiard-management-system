using Bms.Web.Models;

namespace Bms.Web.Services;

public interface ITableSessionService
{
    Task<List<TableCardViewModel>> GetTableCardsAsync();
    Task<TableDetailViewModel?> GetTableDetailAsync(int tableId);
    Task<List<ActiveComboOptionViewModel>> GetActiveCombosAsync();
    Task<List<CustomerLookupItemViewModel>> SearchCustomersAsync(string? keyword);
    Task<TableOperationResult> OpenSessionAsync(int tableId, string staffId, string? customerId = null);
    Task<TableOperationResult> CloseSessionAsync(int sessionId, string staffId);
    Task<CloseSummaryViewModel?> GetCloseSummaryAsync(int sessionId);
    Task<TableOperationResult> TransferSessionAsync(int sessionId, int newTableId, string staffId);
    Task<TableOperationResult> AddSessionComboAsync(int sessionId, int comboId, int quantity, string staffId);
}
