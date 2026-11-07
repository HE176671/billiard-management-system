using System.Security.Claims;
using Bms.Web.Data;
using Bms.Web.Models;
using Bms.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Controllers;

[Authorize(Roles = "Manager,Admin")]
public class CampaignController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICampaignService _campaignService;

    public CampaignController(ApplicationDbContext db, ICampaignService campaignService)
    {
        _db = db;
        _campaignService = campaignService;
    }

    private string CurrentActorId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // GET /Campaign?segment=...&rewardItemId=...
    [HttpGet]
    public async Task<IActionResult> Index(string? segment = null, int? rewardItemId = null)
    {
        var form = new CampaignCreateInputModel();
        if (!string.IsNullOrWhiteSpace(segment))
        {
            form.TargetSegment = segment;
            if (segment == CustomerSegmentService.SegAtRisk)
            {
                form.Name = $"Chiến dịch Win-back khách AtRisk ({DateTime.UtcNow.ToVn():MM/yyyy})";
                form.CampaignType = Campaign.TypeWinBack;
                form.MessageTitle = "Nhớ bạn quá {FullName} ơi — Quán tặng bạn ưu đãi quay lại!";
                form.MessageContent = "Xin chào {FullName} (Hạng {TierName}), đã hơn 30 ngày chúng tôi chưa được phục vụ bạn. Ghé quán tuần này để nhận ngay ưu đãi đặc biệt nhé!";
            }
        }

        // Mở từ màn Quà đổi điểm ("Gửi ưu đãi với voucher này") -> chọn sẵn voucher đang mở
        if (rewardItemId.HasValue)
        {
            var item = await _db.RewardItems.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == rewardItemId.Value && r.IsActive);
            if (item != null)
            {
                form.RewardItemId = item.Id;
                if (string.IsNullOrWhiteSpace(form.Name))
                    form.Name = $"Tặng voucher {item.Name} ({DateTime.UtcNow.ToVn():MM/yyyy})";
            }
            else
            {
                TempData["Error"] = "Voucher đã chọn không tồn tại hoặc đang ở trạng thái Đã đóng trong danh mục Quà đổi điểm.";
            }
        }

        return View(await BuildViewModelAsync(form, preview: true));
    }

    // POST /Campaign/Preview
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(CampaignCreateInputModel form)
    {
        await ValidateVoucherSelectionAsync(form);
        return View("Index", await BuildViewModelAsync(form, preview: true));
    }

    // POST /Campaign/CreateAndSend
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAndSend(CampaignCreateInputModel form, bool sendImmediately = true)
    {
        await ValidateVoucherSelectionAsync(form);
        if (!ModelState.IsValid)
            return View("Index", await BuildViewModelAsync(form, preview: true));

        var actorId = CurrentActorId;
        if (string.IsNullOrEmpty(actorId))
            actorId = await _db.Users.Select(u => u.Id).FirstAsync();

        var campaign = new Campaign
        {
            Name = form.Name.Trim(),
            CampaignType = string.IsNullOrWhiteSpace(form.CampaignType) ? Campaign.TypeManual : form.CampaignType,
            TargetSegment = string.IsNullOrWhiteSpace(form.TargetSegment) ? "All" : form.TargetSegment,
            TargetTierId = form.TargetTierId,
            RewardItemId = form.RewardItemId,
            BonusPoints = Math.Max(0, form.BonusPoints),
            MessageTitle = form.MessageTitle.Trim(),
            MessageContent = form.MessageContent.Trim(),
            Status = Campaign.StatusDraft,
            CreatedById = actorId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        if (sendImmediately)
        {
            var res = await _campaignService.SendCampaignAsync(campaign.Id, actorId);
            if (res.Success) TempData["Success"] = res.Message;
            else TempData["Error"] = res.Message;
        }
        else
        {
            TempData["Success"] = $"Đã lưu bản nháp chiến dịch \"{campaign.Name}\".";
        }

        return RedirectToAction(nameof(Index));
    }

    // POST /Campaign/Send/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(int id)
    {
        var actorId = CurrentActorId;
        if (string.IsNullOrEmpty(actorId))
            actorId = await _db.Users.Select(u => u.Id).FirstAsync();

        var res = await _campaignService.SendCampaignAsync(id, actorId);
        if (res.Success) TempData["Success"] = res.Message;
        else TempData["Error"] = res.Message;

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Đồng bộ với danh mục Quà đổi điểm: voucher phải tồn tại, đang mở,
    /// và hạng đích (nếu chọn) không được thấp hơn Hạng tối thiểu của voucher.
    /// </summary>
    private async Task ValidateVoucherSelectionAsync(CampaignCreateInputModel form)
    {
        if (form.BonusPoints < 0)
            ModelState.AddModelError(nameof(form.BonusPoints), "Điểm thưởng tặng thêm không được là số âm.");

        if (!form.RewardItemId.HasValue) return;

        var item = await _db.RewardItems.AsNoTracking()
            .Include(r => r.MinTier)
            .FirstOrDefaultAsync(r => r.Id == form.RewardItemId.Value);

        if (item == null || !item.IsActive)
        {
            ModelState.AddModelError(nameof(form.RewardItemId),
                "Voucher đã chọn không tồn tại hoặc đang ở trạng thái Đã đóng trong danh mục Quà đổi điểm.");
            return;
        }

        if (item.MinTier != null && form.TargetTierId.HasValue)
        {
            var targetTier = await _db.MembershipTiers.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == form.TargetTierId.Value);
            if (targetTier != null && targetTier.SpendThreshold < item.MinTier.SpendThreshold)
            {
                ModelState.AddModelError(nameof(form.TargetTierId),
                    $"Voucher \"{item.Name}\" chỉ áp dụng từ Hạng {item.MinTier.TierName} trở lên, không thể gửi cho Hạng {targetTier.TierName}.");
            }
        }
    }

    private async Task<CampaignIndexViewModel> BuildViewModelAsync(CampaignCreateInputModel form, bool preview)
    {
        var campaigns = await _db.Campaigns.AsNoTracking()
            .Include(c => c.TargetTier)
            .Include(c => c.RewardItem)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(25)
            .ToListAsync();

        var tiers = await _db.MembershipTiers.AsNoTracking()
            .OrderBy(t => t.SpendThreshold)
            .Select(t => new TierOptionViewModel { Id = t.Id, Name = t.TierName })
            .ToListAsync();

        // Lấy trực tiếp danh mục voucher đang mở từ Quà đổi điểm (kèm Hạng tối thiểu) để đồng bộ
        var rewardItems = await _db.RewardItems.AsNoTracking()
            .Include(r => r.MinTier)
            .Where(r => r.IsActive)
            .OrderBy(r => r.PointCost)
            .ThenBy(r => r.Id)
            .ToListAsync();

        CampaignPreviewResult? previewRes = null;
        if (preview)
        {
            previewRes = await _campaignService.PreviewRecipientsAsync(form.TargetSegment, form.TargetTierId, form.RewardItemId);
        }

        return new CampaignIndexViewModel
        {
            Campaigns = campaigns,
            Tiers = tiers,
            RewardItems = rewardItems,
            Form = form,
            Preview = previewRes
        };
    }
}

