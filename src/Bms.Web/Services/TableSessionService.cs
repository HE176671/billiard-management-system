using System.Data;
using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bms.Web.Services;

public class TableSessionService : ITableSessionService
{
    // =========================================================================
    // PHẦN 1: KHỞI TẠO SERVICE & DEPENDENCY INJECTION
    // =========================================================================

    private readonly ApplicationDbContext _context;
    private readonly ILogger<TableSessionService> _logger;

    public TableSessionService(ApplicationDbContext context, ILogger<TableSessionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // =========================================================================
    // PHẦN 2: TRUY VẤN ĐỌC DỮ LIỆU (LƯỚI BÀN, CHI TIẾT PHIÊN CHƠI, COMBO & KHÁCH HÀNG)
    // =========================================================================

    public async Task<List<TableCardViewModel>> GetTableCardsAsync()
    {
        var serverTimeUtc = DateTime.UtcNow;

        var query = from table in _context.BilliardTables.AsNoTracking()
                    join type in _context.TableTypes.AsNoTracking() on table.TableTypeId equals type.Id
                    join session in _context.PlaySessions.AsNoTracking().Where(s => s.Status == "Active")
                        on table.Id equals session.TableId into sessionGroup
                    from activeSession in sessionGroup.DefaultIfEmpty()
                    orderby table.TableCode
                    select new
                    {
                        table.Id,
                        table.TableCode,
                        TableTypeName = type.Name,
                        type.HourlyRate,
                        table.FloorNumber,
                        table.Status,
                        ActiveSessionId = (int?)activeSession.Id,
                        SessionStartUtc = (DateTime?)activeSession.StartAtUtc
                    };

        var items = await query.ToListAsync();

        var activeSessionIds = items
            .Where(x => x.ActiveSessionId.HasValue)
            .Select(x => x.ActiveSessionId!.Value)
            .Distinct()
            .ToList();

        var combosBySession = new Dictionary<int, (int TotalMinutes, string SummaryText)>();
        if (activeSessionIds.Count > 0)
        {
            var sessionComboRows = await _context.SessionCombos.AsNoTracking()
                .Where(sc => activeSessionIds.Contains(sc.SessionId))
                .OrderBy(sc => sc.PurchasedAtUtc)
                .ThenBy(sc => sc.Id)
                .Select(sc => new
                {
                    sc.SessionId,
                    sc.ComboNameSnapshot,
                    sc.Quantity,
                    sc.PlaytimeHoursSnapshot
                })
                .ToListAsync();

            foreach (var grp in sessionComboRows.GroupBy(x => x.SessionId))
            {
                int totalMins = (int)Math.Round(grp.Sum(x => x.Quantity * x.PlaytimeHoursSnapshot * 60m));
                var totalHours = grp.Sum(x => x.Quantity * x.PlaytimeHoursSnapshot);
                var names = string.Join(", ", grp.Select(x => x.Quantity > 1 ? $"{x.ComboNameSnapshot} x{x.Quantity}" : x.ComboNameSnapshot));
                combosBySession[grp.Key] = (totalMins, $"{names} ({totalHours:0.##}h)");
            }
        }

        return items.Select(x =>
        {
            bool hasCombo = false;
            int totalComboMinutes = 0;
            string? comboSummary = null;

            if (x.ActiveSessionId.HasValue && combosBySession.TryGetValue(x.ActiveSessionId.Value, out var cInfo))
            {
                hasCombo = true;
                totalComboMinutes = cInfo.TotalMinutes;
                comboSummary = cInfo.SummaryText;
            }

            DateTime? sessionStartUtc = x.SessionStartUtc.HasValue
                ? DateTime.SpecifyKind(x.SessionStartUtc.Value, DateTimeKind.Utc)
                : null;

            bool isComboOverdue = x.Status == "InUse"
                && hasCombo
                && totalComboMinutes > 0
                && sessionStartUtc.HasValue
                && (serverTimeUtc - sessionStartUtc.Value).TotalSeconds >= totalComboMinutes * 60.0;

            return new TableCardViewModel
            {
                Id = x.Id,
                TableCode = x.TableCode,
                TableTypeName = x.TableTypeName,
                HourlyRate = x.HourlyRate,
                FloorNumber = x.FloorNumber,
                Status = x.Status,
                DisplayStatus = MapDisplayStatus(x.Status, isComboOverdue),
                ActiveSessionId = x.ActiveSessionId,
                SessionStartUtc = sessionStartUtc,
                HasCombo = hasCombo,
                TotalComboMinutes = totalComboMinutes,
                ComboSummaryText = comboSummary,
                ServerTimeUtc = serverTimeUtc
            };
        }).ToList();
    }

    public async Task<TableDetailViewModel?> GetTableDetailAsync(int tableId)
    {
        var serverTimeUtc = DateTime.UtcNow;

        var query = from table in _context.BilliardTables.AsNoTracking()
                    where table.Id == tableId
                    join type in _context.TableTypes.AsNoTracking() on table.TableTypeId equals type.Id
                    join session in _context.PlaySessions.AsNoTracking().Where(s => s.Status == "Active")
                        on table.Id equals session.TableId into sessionGroup
                    from activeSession in sessionGroup.DefaultIfEmpty()
                    join user in _context.Users.AsNoTracking()
                        on activeSession.CustomerId equals user.Id into userGroup
                    from customerUser in userGroup.DefaultIfEmpty()
                    select new
                    {
                        TableId = table.Id,
                        table.TableCode,
                        TableTypeName = type.Name,
                        type.HourlyRate,
                        table.Status,
                        SessionId = (int?)activeSession.Id,
                        StartAtUtc = (DateTime?)activeSession.StartAtUtc,
                        EndAtUtc = (DateTime?)activeSession.EndAtUtc,
                        HourlyRateSnapshot = (decimal?)activeSession.HourlyRateSnapshot,
                        CustomerId = activeSession.CustomerId,
                        CustomerFullName = customerUser != null ? customerUser.FullName : null,
                        CustomerPhone = customerUser != null ? customerUser.PhoneNumber : null
                    };

        var raw = await query.FirstOrDefaultAsync();
        if (raw == null)
        {
            return null;
        }

        DateTime? startAtUtc = raw.StartAtUtc.HasValue
            ? DateTime.SpecifyKind(raw.StartAtUtc.Value, DateTimeKind.Utc)
            : null;
        DateTime? endAtUtc = raw.EndAtUtc.HasValue
            ? DateTime.SpecifyKind(raw.EndAtUtc.Value, DateTimeKind.Utc)
            : null;
        decimal? hourlyRateSnapshot = raw.HourlyRateSnapshot;
        decimal? playtimeAmount = null;
        decimal? estimatedAmount = null;
        string? customerId = raw.CustomerId;
        string? customerFullName = raw.CustomerFullName;
        string? customerPhone = raw.CustomerPhone;
        int? sessionId = raw.SessionId;
        int? targetSessionIdForHistory = raw.SessionId;
        List<SessionSegmentViewModel> segments = new();
        List<SessionComboPurchaseViewModel> combos = new();
        List<SessionCueRentalViewModel> cueRentals = new();

        if (raw.Status == "InUse" && raw.SessionId.HasValue)
        {
            var activeSessionId = raw.SessionId.Value;
            estimatedAmount = await _context.Database
                .SqlQuery<decimal?>($"SELECT dbo.fn_CalcPlaytimeAmount({activeSessionId}, CAST(SYSUTCDATETIME() AS datetime2(0))) AS [Value]")
                .FirstOrDefaultAsync();
        }

        if (targetSessionIdForHistory.HasValue)
        {
            int sid = targetSessionIdForHistory.Value;
            segments = await (from seg in _context.PlaySessionTableSegments.AsNoTracking()
                              where seg.SessionId == sid
                              join t in _context.BilliardTables.AsNoTracking() on seg.TableId equals t.Id
                              join ty in _context.TableTypes.AsNoTracking() on t.TableTypeId equals ty.Id
                              orderby seg.StartAtUtc, seg.Id
                              select new SessionSegmentViewModel
                              {
                                  TableCode = t.TableCode,
                                  TableTypeName = ty.Name,
                                  HourlyRate = seg.HourlyRateSnapshot,
                                  StartAtUtc = DateTime.SpecifyKind(seg.StartAtUtc, DateTimeKind.Utc),
                                  EndAtUtc = seg.EndAtUtc.HasValue
                                      ? DateTime.SpecifyKind(seg.EndAtUtc.Value, DateTimeKind.Utc)
                                      : null
                              }).ToListAsync();

            combos = await LoadSessionCombosAsync(sid);
            cueRentals = await LoadSessionCueRentalsAsync(sid);
        }

        decimal comboAmount = combos.Sum(c => c.Subtotal);
        int totalComboMinutes = combos.Sum(c => c.TotalMinutes);
        bool hasCombo = combos.Count > 0;
        decimal baseTotal = estimatedAmount ?? 0m;
        decimal overtimeAmount = hasCombo ? Math.Max(0m, baseTotal - comboAmount) : 0m;
        bool isComboOverdue = raw.Status == "InUse"
            && hasCombo
            && totalComboMinutes > 0
            && startAtUtc.HasValue
            && (serverTimeUtc - startAtUtc.Value).TotalSeconds >= totalComboMinutes * 60.0;

        return new TableDetailViewModel
        {
            TableId = raw.TableId,
            TableCode = raw.TableCode,
            TableTypeName = raw.TableTypeName,
            HourlyRate = raw.HourlyRate,
            Status = raw.Status,
            DisplayStatus = MapDisplayStatus(raw.Status, isComboOverdue),
            SessionId = sessionId,
            StartAtUtc = startAtUtc,
            EndAtUtc = endAtUtc,
            HourlyRateSnapshot = hourlyRateSnapshot,
            PlaytimeAmount = playtimeAmount,
            EstimatedAmount = estimatedAmount,
            ComboAmount = comboAmount,
            OvertimeAmount = overtimeAmount,
            TotalComboMinutes = totalComboMinutes,
            HasCombo = hasCombo,
            CustomerId = customerId,
            CustomerFullName = customerFullName,
            CustomerPhone = customerPhone,
            Segments = segments,
            Combos = combos,
            CueRentals = cueRentals,
            CueRentalAmount = cueRentals.Sum(c => c.Subtotal),
            ServerTimeUtc = serverTimeUtc
        };
    }

    private async Task<List<SessionComboPurchaseViewModel>> LoadSessionCombosAsync(int sessionId)
    {
        var purchases = await _context.SessionCombos.AsNoTracking()
            .Where(sc => sc.SessionId == sessionId)
            .OrderBy(sc => sc.PurchasedAtUtc)
            .ThenBy(sc => sc.Id)
            .Select(sc => new SessionComboPurchaseViewModel
            {
                SessionComboId = sc.Id,
                ComboId = sc.ComboId,
                ComboName = sc.ComboNameSnapshot,
                Quantity = sc.Quantity,
                UnitPrice = sc.UnitPriceSnapshot,
                PlaytimeHoursPerCombo = sc.PlaytimeHoursSnapshot,
                PurchasedAtUtc = DateTime.SpecifyKind(sc.PurchasedAtUtc, DateTimeKind.Utc)
            })
            .ToListAsync();

        if (purchases.Count == 0)
        {
            return purchases;
        }

        var purchaseIds = purchases.Select(p => p.SessionComboId).ToList();
        var items = await _context.SessionComboItems.AsNoTracking()
            .Where(sci => purchaseIds.Contains(sci.SessionComboId))
            .OrderBy(sci => sci.ProductNameSnapshot)
            .ToListAsync();

        var itemsByPurchase = items
            .GroupBy(i => i.SessionComboId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var p in purchases)
        {
            if (itemsByPurchase.TryGetValue(p.SessionComboId, out var pItems))
            {
                p.Items = pItems.Select(i => new SessionComboItemViewModel
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductNameSnapshot,
                    QuantityPerCombo = i.QuantityPerCombo,
                    TotalQuantity = i.QuantityPerCombo * p.Quantity
                }).ToList();
            }
        }

        return purchases;
    }

    public async Task<List<ActiveComboOptionViewModel>> GetActiveCombosAsync()
    {
        var combos = await _context.Combos.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.PlaytimeHours)
            .ThenBy(c => c.Price)
            .Select(c => new ActiveComboOptionViewModel
            {
                Id = c.Id,
                Name = c.Name,
                Price = c.Price,
                PlaytimeHours = c.PlaytimeHours
            })
            .ToListAsync();

        if (combos.Count == 0)
        {
            return combos;
        }

        var comboIds = combos.Select(c => c.Id).ToList();
        var comboItems = await (from ci in _context.ComboItems.AsNoTracking()
                                where comboIds.Contains(ci.ComboId)
                                join p in _context.Products.AsNoTracking() on ci.ProductId equals p.Id
                                orderby p.Name
                                select new
                                {
                                    ci.ComboId,
                                    ci.ProductId,
                                    ProductName = p.Name,
                                    ci.Quantity
                                }).ToListAsync();

        var itemsByCombo = comboItems
            .GroupBy(x => x.ComboId)
            .ToDictionary(g => g.Key, g => g.Select(x => new ActiveComboItemOptionViewModel
            {
                ProductId = x.ProductId,
                ProductName = x.ProductName,
                Quantity = x.Quantity
            }).ToList());

        foreach (var combo in combos)
        {
            if (itemsByCombo.TryGetValue(combo.Id, out var cItems))
            {
                combo.Items = cItems;
            }
        }

        return combos;
    }

    public async Task<List<CustomerLookupItemViewModel>> SearchCustomersAsync(string? keyword)
    {
        var query = from u in _context.Users.AsNoTracking()
                    join ur in _context.UserRoles.AsNoTracking() on u.Id equals ur.UserId
                    join r in _context.Roles.AsNoTracking() on ur.RoleId equals r.Id
                    where u.IsActive && r.Name == "Customer"
                    select u;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(u =>
                u.FullName.Contains(kw) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(kw)) ||
                (u.UserName != null && u.UserName.Contains(kw)));
        }

        return await query
            .OrderBy(u => u.FullName)
            .Take(20)
            .Select(u => new CustomerLookupItemViewModel
            {
                Id = u.Id,
                FullName = u.FullName,
                PhoneNumber = u.PhoneNumber,
                UserName = u.UserName
            })
            .ToListAsync();
    }

    public static string MapDisplayStatus(string status, bool isComboOverdue = false)
    {
        if (status == "InUse" && isComboOverdue)
        {
            return "Đã hết giờ Combo (Tính vượt giờ)";
        }

        return status switch
        {
            "Available" => "Còn trống",
            "InUse" => "Đang sử dụng",
            "Maintenance" => "Bảo trì",
            _ => status
        };
    }

    /// <summary>
    /// Tính số phút tính tiền theo đúng số phút thực tế đã chơi (chơi bao lâu tính bấy nhiêu, không áp mức tối thiểu 15 phút).
    /// </summary>
    public static int CalculateBilledMinutes(double elapsedSeconds, bool hasCombo = false)
    {
        return Math.Max(1, (int)Math.Ceiling(elapsedSeconds / 60.0));
    }

    // =========================================================================
    // PHẦN 3: THAO TÁC GHI DỮ LIỆU QUA STORED PROCEDURES (MỞ, ĐÓNG, CHUYỂN BÀN, CHỌN COMBO)
    // =========================================================================

    public async Task<TableOperationResult> OpenSessionAsync(int tableId, string staffId, string? customerId = null, int? comboId = null, List<CueRentalLineRequest>? cueRentals = null)
    {
        var cueLines = MergeCueLines(cueRentals, out var cueLineError);
        if (cueLineError != null)
        {
            return TableOperationResult.Fail(null, cueLineError, autoReload: false);
        }

        if (cueLines.Count > 0)
        {
            // Kiểm tra gậy/tồn kho trước khi mở bàn để không mở bàn rồi mới báo hết gậy.
            var cueStockError = await ValidateCueLinesAsync(cueLines);
            if (cueStockError != null)
            {
                return TableOperationResult.Fail(null, cueStockError, autoReload: false);
            }
        }

        if (tableId <= 0)
        {
            return TableOperationResult.Fail(null, "Mã bàn không hợp lệ.", autoReload: false);
        }

        if (string.IsNullOrWhiteSpace(staffId))
        {
            return TableOperationResult.Fail(null, "Thông tin nhân viên thực hiện không hợp lệ.", autoReload: false);
        }

        if (comboId.HasValue && comboId.Value > 0)
        {
            bool isValidCombo = await _context.Combos.AsNoTracking()
                .AnyAsync(c => c.Id == comboId.Value && c.IsActive);
            if (!isValidCombo)
            {
                return TableOperationResult.Fail(51714, "Gói Combo đã chọn không tồn tại hoặc đã ngừng bán.", autoReload: false);
            }
        }

        var normalizedCustomerId = string.IsNullOrWhiteSpace(customerId) ? null : customerId.Trim();

        var conn = (SqlConnection)_context.Database.GetDbConnection();
        var openedHere = conn.State == ConnectionState.Closed;

        try
        {
            if (openedHere)
            {
                await conn.OpenAsync();
            }

            int sessionId = 0;
            DateTime startAtUtc = DateTime.UtcNow;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "dbo.usp_OpenSession";
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.Add(new SqlParameter("@TableId", SqlDbType.Int) { Value = tableId });
                cmd.Parameters.Add(new SqlParameter("@StaffId", SqlDbType.NVarChar, 450) { Value = staffId });
                cmd.Parameters.Add(new SqlParameter("@BookingId", SqlDbType.Int) { Value = DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.NVarChar, 450) { Value = (object?)normalizedCustomerId ?? DBNull.Value });

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    int sessionIdOrdinal = reader.GetOrdinal("SessionId");
                    int startOrdinal = reader.GetOrdinal("StartAtUtc");

                    sessionId = reader.GetInt32(sessionIdOrdinal);
                    startAtUtc = DateTime.SpecifyKind(reader.GetDateTime(startOrdinal), DateTimeKind.Utc);
                }
            }

            if (sessionId <= 0)
            {
                return TableOperationResult.Fail(null, "Không nhận được phản hồi từ hệ thống cơ sở dữ liệu khi mở phiên.", autoReload: true);
            }

            if (comboId.HasValue && comboId.Value > 0)
            {
                var comboRes = await AddSessionComboAsync(sessionId, comboId.Value, 1, staffId);
                if (!comboRes.Success)
                {
                    return comboRes;
                }

                var (comboCueAmount, comboCueWarning) = await RentCuesAtOpenAsync(sessionId, cueLines, staffId);
                return TableOperationResult.Ok(new
                {
                    SessionId = sessionId,
                    StartAtUtc = startAtUtc,
                    HasCombo = true,
                    ComboData = comboRes.Data,
                    CueRentalAmount = comboCueAmount,
                    CueRentalWarning = comboCueWarning
                }, comboRes.Message);            }

            var (cueAmount, cueWarning) = await RentCuesAtOpenAsync(sessionId, cueLines, staffId);
            return TableOperationResult.Ok(new
            {
                SessionId = sessionId,
                StartAtUtc = startAtUtc,
                HasCombo = false,
                CueRentalAmount = cueAmount,
                CueRentalWarning = cueWarning
            });        }
        catch (SqlException ex)
        {
            return HandleSqlException(ex, "OpenSessionAsync", tableId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi thực hiện OpenSessionAsync cho bàn {TableId} bởi nhân viên {StaffId}.", tableId, staffId);
            return TableOperationResult.Fail(null, "Đã xảy ra lỗi không xác định trên hệ thống. Vui lòng thử lại sau.", autoReload: false);
        }
        finally
        {
            if (openedHere && conn.State == ConnectionState.Open)
            {
                await conn.CloseAsync();
            }
        }
    }

    public async Task<TableOperationResult> CloseSessionAsync(int sessionId, string staffId)
    {
        // Chụp lại tiền gậy trước khi đóng; sau khi đóng thành công thì cộng trả toàn bộ gậy của phiên về tồn kho.
        decimal cueRentalAmount = 0m;
        if (sessionId > 0)
        {
            var cueRentals = await LoadSessionCueRentalsAsync(sessionId);
            cueRentalAmount = cueRentals.Sum(c => c.Subtotal);
        }

        var result = await CloseSessionCoreAsync(sessionId, staffId, cueRentalAmount);
        if (result.Success)
        {
            await ReleaseCueRentalsAsync(sessionId);
        }

        return result;
    }

    private async Task<TableOperationResult> CloseSessionCoreAsync(int sessionId, string staffId, decimal cueRentalAmount)
    {
        if (sessionId <= 0)
        {
            return TableOperationResult.Fail(null, "Mã phiên chơi không hợp lệ.", autoReload: false);
        }

        if (string.IsNullOrWhiteSpace(staffId))
        {
            return TableOperationResult.Fail(null, "Thông tin nhân viên thực hiện không hợp lệ.", autoReload: false);
        }

        bool hasCombo = await _context.SessionCombos.AsNoTracking().AnyAsync(sc => sc.SessionId == sessionId);

        var conn = (SqlConnection)_context.Database.GetDbConnection();
        var openedHere = conn.State == ConnectionState.Closed;

        try
        {
            if (openedHere)
            {
                await conn.OpenAsync();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "dbo.usp_CloseSession";
            cmd.CommandType = CommandType.StoredProcedure;

            cmd.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = sessionId });
            cmd.Parameters.Add(new SqlParameter("@StaffId", SqlDbType.NVarChar, 450) { Value = staffId });

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int idOrdinal = reader.GetOrdinal("Id");
                int startOrdinal = reader.GetOrdinal("StartAtUtc");
                int endOrdinal = reader.GetOrdinal("EndAtUtc");
                int rateOrdinal = reader.GetOrdinal("HourlyRateSnapshot");
                int amountOrdinal = reader.GetOrdinal("PlaytimeAmount");

                int id = reader.GetInt32(idOrdinal);
                DateTime startAtUtc = DateTime.SpecifyKind(reader.GetDateTime(startOrdinal), DateTimeKind.Utc);
                DateTime endAtUtc = DateTime.SpecifyKind(reader.GetDateTime(endOrdinal), DateTimeKind.Utc);
                decimal hourlyRateSnapshot = reader.GetDecimal(rateOrdinal);
                decimal playtimeAmount = reader.GetDecimal(amountOrdinal);
                int billedMinutes = CalculateBilledMinutes(Math.Max(0, (endAtUtc - startAtUtc).TotalSeconds), hasCombo);

                return TableOperationResult.Ok(new
                {
                    SessionId = id,
                    PlaytimeAmount = playtimeAmount,
                    HourlyRateSnapshot = hourlyRateSnapshot,
                    StartAtUtc = startAtUtc,
                    EndAtUtc = endAtUtc,
                    BilledMinutes = billedMinutes,
                    CueRentalAmount = cueRentalAmount
                });
            }

            return TableOperationResult.Fail(null, "Không nhận được phản hồi từ hệ thống cơ sở dữ liệu khi đóng phiên.", autoReload: true);
        }
        catch (SqlException ex)
        {
            return HandleSqlException(ex, "CloseSessionAsync", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi thực hiện CloseSessionAsync cho phiên {SessionId} bởi nhân viên {StaffId}.", sessionId, staffId);
            return TableOperationResult.Fail(null, "Đã xảy ra lỗi không xác định trên hệ thống. Vui lòng thử lại sau.", autoReload: false);
        }
        finally
        {
            if (openedHere && conn.State == ConnectionState.Open)
            {
                await conn.CloseAsync();
            }
        }
    }

    public async Task<CloseSummaryViewModel?> GetCloseSummaryAsync(int sessionId)
    {
        var summary = await GetCloseSummaryCoreAsync(sessionId);
        if (summary == null)
        {
            return null;
        }

        summary.CueRentals = await LoadSessionCueRentalsAsync(sessionId);
        summary.CueRentalAmount = summary.CueRentals.Sum(c => c.Subtotal);
        return summary;
    }

    private async Task<CloseSummaryViewModel?> GetCloseSummaryCoreAsync(int sessionId)
    {
        if (sessionId <= 0)
        {
            return null;
        }

        var conn = (SqlConnection)_context.Database.GetDbConnection();
        var openedHere = conn.State == ConnectionState.Closed;

        try
        {
            if (openedHere)
            {
                await conn.OpenAsync();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT 
    s.Id AS SessionId,
    t.TableCode,
    s.StartAtUtc,
    CAST(SYSUTCDATETIME() AS datetime2(0)) AS ServerNowUtc,
    dbo.fn_CalcPlaytimeAmount(s.Id, CAST(SYSUTCDATETIME() AS datetime2(0))) AS EstimatedAmount,
    (SELECT COUNT(*) FROM dbo.SessionCombos sc WHERE sc.SessionId = s.Id) AS ComboCount,
    COALESCE((SELECT SUM(CONVERT(decimal(18,2), sc.Quantity) * sc.UnitPriceSnapshot) FROM dbo.SessionCombos sc WHERE sc.SessionId = s.Id), 0) AS ComboAmount,
    COALESCE((SELECT CONVERT(int, ROUND(SUM(CONVERT(decimal(18,2), sc.Quantity) * sc.PlaytimeHoursSnapshot * 60.0), 0)) FROM dbo.SessionCombos sc WHERE sc.SessionId = s.Id), 0) AS TotalComboMinutes
FROM dbo.PlaySessions s
JOIN dbo.BilliardTables t ON t.Id = s.TableId
WHERE s.Id = @SessionId AND s.Status = 'Active';";
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = sessionId });

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int sessionIdOrdinal = reader.GetOrdinal("SessionId");
                int tableCodeOrdinal = reader.GetOrdinal("TableCode");
                int startOrdinal = reader.GetOrdinal("StartAtUtc");
                int serverNowOrdinal = reader.GetOrdinal("ServerNowUtc");
                int estAmountOrdinal = reader.GetOrdinal("EstimatedAmount");
                int comboCountOrdinal = reader.GetOrdinal("ComboCount");
                int comboAmountOrdinal = reader.GetOrdinal("ComboAmount");
                int totalComboMinutesOrdinal = reader.GetOrdinal("TotalComboMinutes");

                int resultSessionId = reader.GetInt32(sessionIdOrdinal);
                string tableCode = reader.GetString(tableCodeOrdinal);
                DateTime startAtUtc = DateTime.SpecifyKind(reader.GetDateTime(startOrdinal), DateTimeKind.Utc);
                DateTime serverNowUtc = DateTime.SpecifyKind(reader.GetDateTime(serverNowOrdinal), DateTimeKind.Utc);
                decimal? estimatedAmount = reader.IsDBNull(estAmountOrdinal) ? null : reader.GetDecimal(estAmountOrdinal);
                int comboCount = reader.GetInt32(comboCountOrdinal);
                decimal comboAmount = reader.GetDecimal(comboAmountOrdinal);
                int totalComboMinutes = reader.GetInt32(totalComboMinutesOrdinal);

                bool hasCombo = comboCount > 0;
                var elapsedSeconds = Math.Max(0, (serverNowUtc - startAtUtc).TotalSeconds);
                int billedMinutes = CalculateBilledMinutes(elapsedSeconds, hasCombo);
                int overtimeMinutes = hasCombo ? Math.Max(0, billedMinutes - totalComboMinutes) : 0;
                decimal overtimeAmount = hasCombo && estimatedAmount.HasValue
                    ? Math.Max(0m, estimatedAmount.Value - comboAmount)
                    : 0m;

                return new CloseSummaryViewModel
                {
                    SessionId = resultSessionId,
                    TableCode = tableCode,
                    StartAtUtc = startAtUtc,
                    ServerNowUtc = serverNowUtc,
                    BilledMinutes = billedMinutes,
                    HasCombo = hasCombo,
                    TotalComboMinutes = totalComboMinutes,
                    OvertimeMinutes = overtimeMinutes,
                    ComboAmount = comboAmount,
                    OvertimeAmount = overtimeAmount,
                    EstimatedAmount = estimatedAmount
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy tóm tắt đóng phiên GetCloseSummaryAsync cho phiên {SessionId}.", sessionId);
            return null;
        }
        finally
        {
            if (openedHere && conn.State == ConnectionState.Open)
            {
                await conn.CloseAsync();
            }
        }
    }

    public async Task<TableOperationResult> TransferSessionAsync(int sessionId, int newTableId, string staffId)
    {
        if (sessionId <= 0)
        {
            return TableOperationResult.Fail(null, "Mã phiên chơi không hợp lệ.", autoReload: false);
        }

        if (newTableId <= 0)
        {
            return TableOperationResult.Fail(null, "Mã bàn đích không hợp lệ.", autoReload: false);
        }

        if (string.IsNullOrWhiteSpace(staffId))
        {
            return TableOperationResult.Fail(null, "Thông tin nhân viên thực hiện không hợp lệ.", autoReload: false);
        }

        var conn = (SqlConnection)_context.Database.GetDbConnection();
        var openedHere = conn.State == ConnectionState.Closed;

        try
        {
            if (openedHere)
            {
                await conn.OpenAsync();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "dbo.usp_TransferSession";
            cmd.CommandType = CommandType.StoredProcedure;

            cmd.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = sessionId });
            cmd.Parameters.Add(new SqlParameter("@NewTableId", SqlDbType.Int) { Value = newTableId });
            cmd.Parameters.Add(new SqlParameter("@StaffId", SqlDbType.NVarChar, 450) { Value = staffId });

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int sessionIdOrdinal = reader.GetOrdinal("SessionId");
                int oldTableIdOrdinal = reader.GetOrdinal("OldTableId");
                int newTableIdOrdinal = reader.GetOrdinal("NewTableId");
                int newTableCodeOrdinal = reader.GetOrdinal("NewTableCode");
                int newHourlyRateOrdinal = reader.GetOrdinal("NewHourlyRate");
                int transferAtUtcOrdinal = reader.GetOrdinal("TransferAtUtc");

                int resSessionId = reader.GetInt32(sessionIdOrdinal);
                int oldTableId = reader.GetInt32(oldTableIdOrdinal);
                int resNewTableId = reader.GetInt32(newTableIdOrdinal);
                string newTableCode = reader.GetString(newTableCodeOrdinal);
                decimal newHourlyRate = reader.GetDecimal(newHourlyRateOrdinal);
                DateTime transferAtUtc = DateTime.SpecifyKind(reader.GetDateTime(transferAtUtcOrdinal), DateTimeKind.Utc);

                return TableOperationResult.Ok(new
                {
                    SessionId = resSessionId,
                    OldTableId = oldTableId,
                    NewTableId = resNewTableId,
                    NewTableCode = newTableCode,
                    NewHourlyRate = newHourlyRate,
                    TransferAtUtc = transferAtUtc
                });
            }

            return TableOperationResult.Fail(null, "Không nhận được phản hồi từ hệ thống cơ sở dữ liệu khi chuyển bàn.", autoReload: true);
        }
        catch (SqlException ex)
        {
            return HandleSqlException(ex, "TransferSessionAsync", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi thực hiện TransferSessionAsync cho phiên {SessionId} sang bàn {NewTableId} bởi nhân viên {StaffId}.", sessionId, newTableId, staffId);
            return TableOperationResult.Fail(null, "Đã xảy ra lỗi không xác định trên hệ thống. Vui lòng thử lại sau.", autoReload: false);
        }
        finally
        {
            if (openedHere && conn.State == ConnectionState.Open)
            {
                await conn.CloseAsync();
            }
        }
    }

    // =========================================================================
    // PHẦN: THUÊ GẬY THEO LƯỢT (CHỌN LÚC MỞ BÀN HOẶC THUÊ THÊM KHI ĐANG CHƠI)
    // - Giá cố định lấy từ danh mục "Gậy cho thuê", chụp lại (snapshot) lúc thuê.
    // - Trừ tồn khi thuê (nguyên tử, không cho âm), cộng trả lại khi đóng bàn.
    // - Tiền gậy = đơn giá × số lượng, cộng vào hóa đơn lúc đóng bàn.
    // =========================================================================

    public async Task<List<RentalCueOptionViewModel>> GetRentalCuesAsync()
    {
        // Đồng bộ lại tồn kho cho các phiên đã đóng mà chưa kịp hoàn gậy (ví dụ lỗi giữa chừng khi đóng bàn).
        await ReleaseCueRentalsAsync(sessionId: null);

        return await _context.Products.AsNoTracking()
            .Where(p => p.IsActive
                        && p.Category != null
                        && p.Category.Name == ProductCategory.RentalCueCategoryName)
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Name)
            .Select(p => new RentalCueOptionViewModel
            {
                ProductId = p.Id,
                Name = p.Name,
                Price = p.Price,
                StockQuantity = p.StockQuantity
            })
            .ToListAsync();
    }

    public async Task<TableOperationResult> AddCueRentalAsync(int sessionId, List<CueRentalLineRequest>? lines, string staffId)
    {
        if (sessionId <= 0)
        {
            return TableOperationResult.Fail(null, "Mã phiên chơi không hợp lệ.", autoReload: false);
        }

        if (string.IsNullOrWhiteSpace(staffId))
        {
            return TableOperationResult.Fail(null, "Thông tin nhân viên thực hiện không hợp lệ.", autoReload: false);
        }

        var merged = MergeCueLines(lines, out var lineError);
        if (lineError != null)
        {
            return TableOperationResult.Fail(null, lineError, autoReload: false);
        }

        if (merged.Count == 0)
        {
            return TableOperationResult.Fail(null, "Vui lòng chọn ít nhất 1 cây gậy để thuê.", autoReload: false);
        }

        bool isActive = await _context.PlaySessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.Status == "Active");
        if (!isActive)
        {
            return TableOperationResult.Fail(51502, "Phiên chơi không còn ở trạng thái Hoạt động (có thể đã được nhân viên khác đóng).", autoReload: true);
        }

        var (error, amount) = await ApplyCueRentalsAsync(sessionId, merged, staffId);
        if (error != null)
        {
            return TableOperationResult.Fail(null, error, autoReload: false);
        }

        int totalCues = merged.Sum(m => m.Quantity);
        return TableOperationResult.Ok(
            new { SessionId = sessionId, CueRentalAmount = amount, TotalQuantity = totalCues },
            $"Đã thuê {totalCues} cây gậy ({amount:N0} VND).");
    }

    /// <summary>
    /// Gộp các dòng trùng ProductId, bỏ dòng số lượng 0 và báo lỗi nếu có số lượng âm.
    /// </summary>
    private static List<CueRentalLineRequest> MergeCueLines(List<CueRentalLineRequest>? lines, out string? error)
    {
        error = null;
        var result = new List<CueRentalLineRequest>();
        if (lines == null)
        {
            return result;
        }

        foreach (var line in lines)
        {
            if (line == null || (line.ProductId <= 0 && line.Quantity == 0))
            {
                continue;
            }

            if (line.ProductId <= 0 || line.Quantity < 0)
            {
                error = "Danh sách gậy thuê không hợp lệ.";
                return new List<CueRentalLineRequest>();
            }

            if (line.Quantity == 0)
            {
                continue;
            }

            if (line.Quantity > 100)
            {
                error = "Số lượng thuê mỗi loại gậy không hợp lệ.";
                return new List<CueRentalLineRequest>();
            }

            var existing = result.FirstOrDefault(r => r.ProductId == line.ProductId);
            if (existing != null)
            {
                existing.Quantity += line.Quantity;
            }
            else
            {
                result.Add(new CueRentalLineRequest { ProductId = line.ProductId, Quantity = line.Quantity });
            }
        }

        return result;
    }

    /// <summary>
    /// Kiểm tra trước (không ghi) các gậy chọn có hợp lệ và còn đủ tồn hay không. Trả về thông báo lỗi hoặc null.
    /// </summary>
    private async Task<string?> ValidateCueLinesAsync(List<CueRentalLineRequest> merged)
    {
        if (merged.Count == 0)
        {
            return null;
        }

        var ids = merged.Select(m => m.ProductId).ToList();
        var products = await _context.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();

        foreach (var line in merged)
        {
            var product = products.FirstOrDefault(p => p.Id == line.ProductId);
            if (product == null
                || !product.IsActive
                || product.Category == null
                || product.Category.Name != ProductCategory.RentalCueCategoryName)
            {
                return "Loại gậy đã chọn không tồn tại hoặc đã ngừng cho thuê.";
            }

            if (product.StockQuantity < line.Quantity)
            {
                return $"Gậy \"{product.Name}\" chỉ còn {product.StockQuantity} cây, không đủ để thuê {line.Quantity} cây.";
            }
        }

        return null;
    }

    /// <summary>
    /// Trừ tồn nguyên tử + ghi dòng thuê trong một transaction. Trả về (lỗi, tổng tiền gậy vừa thuê).
    /// </summary>
    private async Task<(string? Error, decimal Amount)> ApplyCueRentalsAsync(int sessionId, List<CueRentalLineRequest> merged, string staffId)
    {
        try
        {
            var ids = merged.Select(m => m.ProductId).ToList();
            var products = await _context.Products.AsNoTracking()
                .Include(p => p.Category)
                .Where(p => ids.Contains(p.Id))
                .ToListAsync();

            foreach (var line in merged)
            {
                var product = products.FirstOrDefault(p => p.Id == line.ProductId);
                if (product == null
                    || !product.IsActive
                    || product.Category == null
                    || product.Category.Name != ProductCategory.RentalCueCategoryName)
                {
                    return ("Loại gậy đã chọn không tồn tại hoặc đã ngừng cho thuê.", 0m);
                }
            }

            await using var tx = await _context.Database.BeginTransactionAsync();
            var nowUtc = DateTime.UtcNow;
            decimal amount = 0m;

            foreach (var line in merged)
            {
                var product = products.First(p => p.Id == line.ProductId);
                int qty = line.Quantity;
                int pid = product.Id;

                // Trừ tồn nguyên tử: chỉ thành công khi còn đủ gậy, tránh hai nhân viên cùng thuê cây cuối.
                int affected = await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE dbo.Products SET StockQuantity = StockQuantity - {qty} WHERE Id = {pid} AND StockQuantity >= {qty}");
                if (affected == 0)
                {
                    await tx.RollbackAsync();
                    int currentStock = await _context.Products.AsNoTracking()
                        .Where(p => p.Id == pid)
                        .Select(p => p.StockQuantity)
                        .FirstOrDefaultAsync();
                    return ($"Gậy \"{product.Name}\" chỉ còn {currentStock} cây, không đủ để thuê {qty} cây.", 0m);
                }

                _context.SessionCueRentals.Add(new SessionCueRental
                {
                    SessionId = sessionId,
                    ProductId = pid,
                    ProductNameSnapshot = product.Name,
                    UnitPriceSnapshot = product.Price,
                    Quantity = qty,
                    RentedAtUtc = nowUtc,
                    CreatedById = staffId,
                    IsReturned = false
                });
                amount += product.Price * qty;
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return (null, amount);
        }
        catch (Exception ex)
        {
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "Lỗi khi thuê gậy cho phiên {SessionId} bởi nhân viên {StaffId}.", sessionId, staffId);
            return ("Không thể ghi nhận thuê gậy do lỗi hệ thống. Vui lòng thử lại.", 0m);
        }
    }

    /// <summary>
    /// Cộng trả tồn kho cho các dòng thuê chưa trả. sessionId = null: xử lý mọi phiên đã đóng nhưng chưa hoàn gậy.
    /// Chạy lại nhiều lần vẫn an toàn (chỉ xử lý dòng IsReturned = 0). Không ném lỗi ra ngoài.
    /// </summary>
    private async Task ReleaseCueRentalsAsync(int? sessionId)
    {
        const string sqlBySession = @"
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @Returned TABLE (ProductId int NOT NULL, Quantity int NOT NULL);
UPDATE r SET IsReturned = 1, ReturnedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.ProductId, inserted.Quantity INTO @Returned
FROM dbo.SessionCueRentals r
WHERE r.IsReturned = 0 AND r.SessionId = {0};
UPDATE p SET StockQuantity = p.StockQuantity + x.Qty
FROM dbo.Products p
JOIN (SELECT ProductId, SUM(Quantity) AS Qty FROM @Returned GROUP BY ProductId) x ON x.ProductId = p.Id;
COMMIT;";

        const string sqlOrphans = @"
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @Returned TABLE (ProductId int NOT NULL, Quantity int NOT NULL);
UPDATE r SET IsReturned = 1, ReturnedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.ProductId, inserted.Quantity INTO @Returned
FROM dbo.SessionCueRentals r
JOIN dbo.PlaySessions s ON s.Id = r.SessionId
WHERE r.IsReturned = 0 AND s.Status <> 'Active';
UPDATE p SET StockQuantity = p.StockQuantity + x.Qty
FROM dbo.Products p
JOIN (SELECT ProductId, SUM(Quantity) AS Qty FROM @Returned GROUP BY ProductId) x ON x.ProductId = p.Id;
COMMIT;";

        try
        {
            if (sessionId.HasValue)
            {
                await _context.Database.ExecuteSqlRawAsync(sqlBySession, sessionId.Value);
            }
            else
            {
                await _context.Database.ExecuteSqlRawAsync(sqlOrphans);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể hoàn tồn kho gậy thuê (SessionId={SessionId}). Sẽ tự đồng bộ lại ở lần mở danh sách gậy kế tiếp.", sessionId);
        }
    }

    private async Task<List<SessionCueRentalViewModel>> LoadSessionCueRentalsAsync(int sessionId)
    {
        try
        {
            var rentals = await _context.SessionCueRentals.AsNoTracking()
                .Where(r => r.SessionId == sessionId)
                .OrderBy(r => r.RentedAtUtc)
                .ThenBy(r => r.Id)
                .Select(r => new SessionCueRentalViewModel
                {
                    Id = r.Id,
                    ProductId = r.ProductId,
                    ProductName = r.ProductNameSnapshot,
                    Quantity = r.Quantity,
                    UnitPrice = r.UnitPriceSnapshot,
                    RentedAtUtc = r.RentedAtUtc
                })
                .ToListAsync();

            foreach (var r in rentals)
            {
                r.RentedAtUtc = DateTime.SpecifyKind(r.RentedAtUtc, DateTimeKind.Utc);
            }

            return rentals;
        }
        catch (Exception ex)
        {
            // Không làm hỏng luồng mở/đóng bàn nếu bảng gậy thuê chưa sẵn sàng.
            _logger.LogError(ex, "Không thể tải danh sách gậy thuê của phiên {SessionId}.", sessionId);
            return new List<SessionCueRentalViewModel>();
        }
    }

    private async Task<(decimal Amount, string? Warning)> RentCuesAtOpenAsync(int sessionId, List<CueRentalLineRequest> cueLines, string staffId)
    {
        if (cueLines.Count == 0)
        {
            return (0m, null);
        }

        var (error, amount) = await ApplyCueRentalsAsync(sessionId, cueLines, staffId);
        return error == null
            ? (amount, null)
            : (0m, $"Đã mở bàn nhưng chưa thuê được gậy: {error} Vui lòng thuê gậy lại sau khi bắt đầu chơi.");
    }

    public async Task<TableOperationResult> AddSessionComboAsync(int sessionId, int comboId, int quantity, string staffId)
    {
        if (sessionId <= 0)
        {
            return TableOperationResult.Fail(null, "Mã phiên chơi không hợp lệ.", autoReload: false);
        }

        if (comboId <= 0)
        {
            return TableOperationResult.Fail(null, "Vui lòng chọn gói Combo hợp lệ.", autoReload: false);
        }

        if (quantity != 1)
        {
            return TableOperationResult.Fail(51712, "Mỗi phiên chơi chỉ áp dụng 1 gói Combo.", autoReload: false);
        }

        if (string.IsNullOrWhiteSpace(staffId))
        {
            return TableOperationResult.Fail(null, "Thông tin nhân viên thực hiện không hợp lệ.", autoReload: false);
        }

        bool alreadyHasCombo = await _context.SessionCombos.AsNoTracking()
            .AnyAsync(sc => sc.SessionId == sessionId);
        if (alreadyHasCombo)
        {
            return TableOperationResult.Fail(51715, "Phiên chơi chỉ được áp dụng 1 gói Combo duy nhất.", autoReload: true);
        }

        var conn = (SqlConnection)_context.Database.GetDbConnection();
        var openedHere = conn.State == ConnectionState.Closed;

        try
        {
            if (openedHere)
            {
                await conn.OpenAsync();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "dbo.usp_AddSessionCombo";
            cmd.CommandType = CommandType.StoredProcedure;

            cmd.Parameters.Add(new SqlParameter("@SessionId", SqlDbType.Int) { Value = sessionId });
            cmd.Parameters.Add(new SqlParameter("@ComboId", SqlDbType.Int) { Value = comboId });
            cmd.Parameters.Add(new SqlParameter("@Quantity", SqlDbType.Int) { Value = 1 });
            cmd.Parameters.Add(new SqlParameter("@StaffId", SqlDbType.NVarChar, 450) { Value = staffId });

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int sessionComboId = reader.GetInt32(reader.GetOrdinal("SessionComboId"));
                int resSessionId = reader.GetInt32(reader.GetOrdinal("SessionId"));
                int resComboId = reader.GetInt32(reader.GetOrdinal("ComboId"));
                int resQuantity = reader.GetInt32(reader.GetOrdinal("Quantity"));
                string comboName = reader.GetString(reader.GetOrdinal("ComboNameSnapshot"));
                decimal unitPrice = reader.GetDecimal(reader.GetOrdinal("UnitPriceSnapshot"));
                decimal playtimeHours = reader.GetDecimal(reader.GetOrdinal("PlaytimeHoursSnapshot"));
                DateTime purchasedAtUtc = DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("PurchasedAtUtc")), DateTimeKind.Utc);
                decimal? estimatedAmount = reader.IsDBNull(reader.GetOrdinal("EstimatedAmount"))
                    ? null
                    : reader.GetDecimal(reader.GetOrdinal("EstimatedAmount"));

                return TableOperationResult.Ok(new
                {
                    SessionComboId = sessionComboId,
                    SessionId = resSessionId,
                    ComboId = resComboId,
                    Quantity = resQuantity,
                    ComboName = comboName,
                    UnitPrice = unitPrice,
                    PlaytimeHours = playtimeHours,
                    PurchasedAtUtc = purchasedAtUtc,
                    EstimatedAmount = estimatedAmount
                }, $"Đã mở bàn áp dụng gói {comboName}.");
            }

            return TableOperationResult.Fail(null, "Không nhận được phản hồi từ hệ thống cơ sở dữ liệu khi chọn Combo.", autoReload: true);
        }
        catch (SqlException ex)
        {
            return HandleSqlException(ex, "AddSessionComboAsync", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi thực hiện AddSessionComboAsync cho phiên {SessionId}, Combo {ComboId} bởi nhân viên {StaffId}.", sessionId, comboId, staffId);
            return TableOperationResult.Fail(null, "Đã xảy ra lỗi không xác định trên hệ thống. Vui lòng thử lại sau.", autoReload: false);
        }
        finally
        {
            if (openedHere && conn.State == ConnectionState.Open)
            {
                await conn.CloseAsync();
            }
        }
    }

    // =========================================================================
    // PHẦN 4: XỬ LÝ & ÁNH XẠ MÃ LỖI SQL (ERROR HANDLING & MAPPING)
    // =========================================================================

    private TableOperationResult HandleSqlException(SqlException ex, string operationName, int targetId)
    {
        if (ex.Number == 51699)
        {
            _logger.LogError(ex, "Lỗi toàn vẹn dữ liệu phiên chơi ({SqlErrorNumber}) khi thực thi {Operation} (TargetId: {TargetId}).",
                ex.Number, operationName, targetId);
            return TableOperationResult.Fail(ex.Number, MapErrorCode(ex.Number), autoReload: true);
        }

        if (IsKnownDomainError(ex.Number))
        {
            var message = MapErrorCode(ex.Number);
            var autoReload = IsAutoReloadError(ex.Number);
            _logger.LogWarning("Nghiệp vụ từ chối {Operation} (TargetId: {TargetId}) với mã {ErrorCode}: {ErrorMessage}",
                operationName, targetId, ex.Number, message);
            return TableOperationResult.Fail(ex.Number, message, autoReload: autoReload);
        }

        _logger.LogError(ex, "Lỗi cơ sở dữ liệu ({SqlErrorNumber}) khi thực thi {Operation} (TargetId: {TargetId}).",
            ex.Number, operationName, targetId);

        return TableOperationResult.Fail(
            ex.Number,
            "Đã xảy ra lỗi khi kết nối hoặc xử lý cơ sở dữ liệu. Vui lòng thử lại sau.",
            autoReload: false);
    }

    private static bool IsKnownDomainError(int errorNumber) => errorNumber switch
    {
        51401 or 51402 or 51403 or 51408 or 51501 or 51502 or 51611 or 51612 or 51613 or 51614 or 51616 or 51699 or 51711 or 51712 or 51713 or 51714 or 51715 => true,
        _ => false
    };

    private static bool IsAutoReloadError(int errorNumber) => errorNumber switch
    {
        51402 or 51403 or 51502 or 51612 or 51613 or 51699 or 51713 or 51714 or 51715 => true,
        _ => false
    };

    private static string MapErrorCode(int errorNumber) => errorNumber switch
    {
        51401 => "Thao tác yêu cầu tài khoản Nhân viên hoặc Quản trị viên đang hoạt động.",
        51402 => "Bàn hiện không ở trạng thái Trống để có thể mở phiên.",
        51403 => "Bàn này đã có một phiên chơi đang hoạt động.",
        51408 => "Tài khoản hội viên của khách hàng không tồn tại hoặc đang bị khóa.",
        51501 => "Thao tác đóng phiên yêu cầu quyền Nhân viên hoặc Quản trị viên đang hoạt động.",
        51502 => "Phiên chơi không còn ở trạng thái Hoạt động (có thể đã được nhân viên khác đóng).",
        51611 => "Thao tác chuyển bàn yêu cầu tài khoản Nhân viên hoặc Quản trị viên đang hoạt động.",
        51612 => "Chỉ chuyển bàn được khi phiên đang chơi (phiên có thể đã được đóng).",
        51613 => "Bàn đích không tồn tại hoặc không còn ở trạng thái Trống.",
        51614 => "Bàn đích trùng với bàn hiện tại của phiên.",
        51616 => "Phiên này không thể chuyển bàn.",
        51699 => "Dữ liệu phiên chơi không nhất quán (không có đoạn bàn đang mở). Vui lòng báo quản trị viên.",
        51711 => "Thao tác chọn Combo yêu cầu tài khoản Nhân viên hoặc Quản trị viên đang hoạt động.",
        51712 => "Mỗi phiên chơi chỉ áp dụng 1 gói Combo.",
        51713 => "Chỉ được chọn Combo cho phiên đang hoạt động (phiên có thể đã kết thúc).",
        51714 => "Gói Combo đã chọn không tồn tại hoặc đã ngừng bán.",
        51715 => "Phiên chơi chỉ được áp dụng 1 gói Combo duy nhất.",
        _ => "Đã xảy ra lỗi khi kết nối hoặc xử lý cơ sở dữ liệu. Vui lòng thử lại sau."
    };
}
