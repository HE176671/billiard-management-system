using Bms.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Bms.Web.Filters;

/// <summary>
/// TV5 — BR-23: chặn thao tác vận hành (mở bàn...) khi quán chưa mở ca.
/// Gắn [RequireOpenShift] lên action. Request AJAX/fetch nhận JSON { success = false, message },
/// request thường được chuyển về trang Ca làm kèm thông báo.
/// </summary>
public sealed class RequireOpenShiftAttribute : TypeFilterAttribute
{
    public RequireOpenShiftAttribute() : base(typeof(RequireOpenShiftFilter)) { }
}

internal sealed class RequireOpenShiftFilter : IAsyncActionFilter
{
    public const string Message = "Quán chưa mở ca. Vào mục \"Ca làm của quán\" để mở ca trước khi mở bàn hoặc thu tiền.";

    private readonly IShiftService _shifts;
    private readonly ITempDataDictionaryFactory _tempData;

    public RequireOpenShiftFilter(IShiftService shifts, ITempDataDictionaryFactory tempData)
    {
        _shifts = shifts;
        _tempData = tempData;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var active = await _shifts.GetStoreActiveShiftAsync(context.HttpContext.RequestAborted);
        if (active != null)
        {
            await next();
            return;
        }

        var request = context.HttpContext.Request;
        // fetch/AJAX nhận JSON; form submit thường (trình duyệt điều hướng) thì chuyển về trang Ca làm.
        var fetchMode = request.Headers["Sec-Fetch-Mode"].ToString();
        var wantsJson = request.Headers.XRequestedWith == "XMLHttpRequest"
            || request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
            || (fetchMode.Length > 0 && !fetchMode.Equals("navigate", StringComparison.OrdinalIgnoreCase));

        if (wantsJson)
        {
            context.Result = new JsonResult(new { success = false, message = Message, requireShift = true });
            return;
        }

        _tempData.GetTempData(context.HttpContext)["Error"] = Message;
        context.Result = new RedirectToActionResult("Index", "Shift", null);
    }
}
