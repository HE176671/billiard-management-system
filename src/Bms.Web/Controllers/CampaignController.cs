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

    // GET /Campaign?segment=...
    [HttpGet]
    public async Task<IActionResult> Index(string? segment = null)
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

        return View(await BuildViewModelAsync(form, preview: true));
    }

    // POST /Campaign/Preview
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(CampaignCreateInputModel form)
    {
        return View("Index", await BuildViewModelAsync(form, preview: true));
    }

    // POST /Campaign/CreateAndSend
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAndSend(CampaignCreateInputModel form, bool sendImmediately = true)
    {
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

        var rewardItems = await _db.RewardItems.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.PointCost)
            .ToListAsync();

        CampaignPreviewResult? previewRes = null;
        if (preview)
        {
            previewRes = await _campaignService.PreviewRecipientsAsync(form.TargetSegment, form.TargetTierId);
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

