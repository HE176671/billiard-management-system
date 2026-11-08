using Bms.Web.Models;

namespace Bms.Web.Services;

public interface IComboService
{
    Task<List<ComboViewModel>> GetAllCombosAsync(bool activeOnly = false);
    Task<ComboViewModel?> GetComboByIdAsync(int id);
    Task<FandBServiceResult> SaveComboAsync(ComboInputModel model, string actorId);
    Task<FandBServiceResult> ToggleComboActiveAsync(int id);
}
