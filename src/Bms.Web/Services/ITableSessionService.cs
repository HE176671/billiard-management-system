using Bms.Web.Models;

namespace Bms.Web.Services;

public interface ITableSessionService
{
    Task<List<TableCardViewModel>> GetTableCardsAsync();
    Task<TableDetailViewModel?> GetTableDetailAsync(int tableId);
}
