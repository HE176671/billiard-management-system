using System.Data;
using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bms.Web.Services;

public class TableSessionService : ITableSessionService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TableSessionService> _logger;

    public TableSessionService(ApplicationDbContext context, ILogger<TableSessionService> logger)
    {
        _context = context;
        _logger = logger;
    }

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
                        SessionStartUtc = (DateTime?)activeSession.StartAtUtc,
                        SessionMode = activeSession != null ? activeSession.SessionMode : null,
                        PlannedEndAtUtc = (DateTime?)activeSession.PlannedEndAtUtc
                    };

        var items = await query.ToListAsync();

        return items.Select(x => new TableCardViewModel
        {
            Id = x.Id,
            TableCode = x.TableCode,
            TableTypeName = x.TableTypeName,
            HourlyRate = x.HourlyRate,
            FloorNumber = x.FloorNumber,
            Status = x.Status,
            DisplayStatus = MapDisplayStatus(x.Status),
            ActiveSessionId = x.ActiveSessionId,
            SessionStartUtc = x.SessionStartUtc.HasValue
                ? DateTime.SpecifyKind(x.SessionStartUtc.Value, DateTimeKind.Utc)
                : null,
            SessionMode = x.SessionMode,
            PlannedEndAtUtc = x.PlannedEndAtUtc.HasValue
                ? DateTime.SpecifyKind(x.PlannedEndAtUtc.Value, DateTimeKind.Utc)
                : null,
            ServerTimeUtc = serverTimeUtc
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
                        CustomerFullName = customerUser != null ? customerUser.FullName : null,
                        SessionMode = activeSession != null ? activeSession.SessionMode : null,
                        PlannedEndAtUtc = (DateTime?)activeSession.PlannedEndAtUtc,
                        BillingStartAtUtc = (DateTime?)activeSession.BillingStartAtUtc,
                        BillingEndAtUtc = (DateTime?)activeSession.BillingEndAtUtc
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
        string? customerFullName = raw.CustomerFullName;
        int? sessionId = raw.SessionId;
        string? sessionMode = raw.SessionMode;
        DateTime? plannedEndAtUtc = raw.PlannedEndAtUtc.HasValue
            ? DateTime.SpecifyKind(raw.PlannedEndAtUtc.Value, DateTimeKind.Utc)
            : null;
        DateTime? billingStartAtUtc = raw.BillingStartAtUtc.HasValue
            ? DateTime.SpecifyKind(raw.BillingStartAtUtc.Value, DateTimeKind.Utc)
            : null;
        DateTime? billingEndAtUtc = raw.BillingEndAtUtc.HasValue
            ? DateTime.SpecifyKind(raw.BillingEndAtUtc.Value, DateTimeKind.Utc)
            : null;
        List<SessionSegmentViewModel> segments = new();

        if (raw.Status == "InUse" && raw.SessionId.HasValue)
        {
            var activeSessionId = raw.SessionId.Value;
            estimatedAmount = await _context.Database
                .SqlQuery<decimal?>($"SELECT dbo.fn_CalcPlaytimeAmount({activeSessionId}, CAST(SYSUTCDATETIME() AS datetime2(0))) AS [Value]")
                .FirstOrDefaultAsync();

            segments = await (from seg in _context.PlaySessionTableSegments.AsNoTracking()
                              where seg.SessionId == activeSessionId
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
        }
        else if (raw.Status == "AwaitingPayment")
        {
            var latestClosed = await (from s in _context.PlaySessions.AsNoTracking()
                                      where s.TableId == tableId && s.Status == "Closed"
                                      orderby s.EndAtUtc descending, s.Id descending
                                      join user in _context.Users.AsNoTracking()
                                          on s.CustomerId equals user.Id into userGroup
                                      from customerUser in userGroup.DefaultIfEmpty()
                                      select new
                                      {
                                          s.Id,
                                          s.StartAtUtc,
                                          s.EndAtUtc,
                                          s.HourlyRateSnapshot,
                                          s.PlaytimeAmount,
                                          s.SessionMode,
                                          s.PlannedEndAtUtc,
                                          s.BillingStartAtUtc,
                                          s.BillingEndAtUtc,
                                          CustomerFullName = customerUser != null ? customerUser.FullName : null
                                      }).FirstOrDefaultAsync();

            if (latestClosed != null)
            {
                startAtUtc = DateTime.SpecifyKind(latestClosed.StartAtUtc, DateTimeKind.Utc);
                endAtUtc = latestClosed.EndAtUtc.HasValue
                    ? DateTime.SpecifyKind(latestClosed.EndAtUtc.Value, DateTimeKind.Utc)
                    : null;
                hourlyRateSnapshot = latestClosed.HourlyRateSnapshot;
                playtimeAmount = latestClosed.PlaytimeAmount;
                estimatedAmount = latestClosed.PlaytimeAmount;
                customerFullName = latestClosed.CustomerFullName;
                sessionMode = latestClosed.SessionMode;
                plannedEndAtUtc = latestClosed.PlannedEndAtUtc.HasValue
                    ? DateTime.SpecifyKind(latestClosed.PlannedEndAtUtc.Value, DateTimeKind.Utc)
                    : null;
                billingStartAtUtc = DateTime.SpecifyKind(latestClosed.BillingStartAtUtc, DateTimeKind.Utc);
                billingEndAtUtc = latestClosed.BillingEndAtUtc.HasValue
                    ? DateTime.SpecifyKind(latestClosed.BillingEndAtUtc.Value, DateTimeKind.Utc)
                    : null;

                segments = await (from seg in _context.PlaySessionTableSegments.AsNoTracking()
                                  where seg.SessionId == latestClosed.Id
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
            }

            sessionId = null;
        }

        return new TableDetailViewModel
        {
            TableId = raw.TableId,
            TableCode = raw.TableCode,
            TableTypeName = raw.TableTypeName,
            HourlyRate = raw.HourlyRate,
            Status = raw.Status,
            DisplayStatus = MapDisplayStatus(raw.Status),
            SessionId = sessionId,
            SessionMode = sessionMode,
            StartAtUtc = startAtUtc,
            EndAtUtc = endAtUtc,
            PlannedEndAtUtc = plannedEndAtUtc,
            BillingStartAtUtc = billingStartAtUtc,
            BillingEndAtUtc = billingEndAtUtc,
            HourlyRateSnapshot = hourlyRateSnapshot,
            PlaytimeAmount = playtimeAmount,
            EstimatedAmount = estimatedAmount,
            CustomerFullName = customerFullName,
            Segments = segments,
            ServerTimeUtc = serverTimeUtc
        };
    }

    public static string MapDisplayStatus(string status) => status switch
    {
        "Available" => "Trống",
        "InUse" => "Đang chơi",
        "AwaitingPayment" => "Chờ thanh toán",
        "Maintenance" => "Bảo trì",
        "Inactive" => "Ngừng hoạt động",
        _ => status
    };

    public async Task<TableOperationResult> OpenSessionAsync(int tableId, string staffId)
    {
        if (tableId <= 0)
        {
            return TableOperationResult.Fail(null, "Mã bàn không hợp lệ.", autoReload: false);
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
            cmd.CommandText = "dbo.usp_OpenSession";
            cmd.CommandType = CommandType.StoredProcedure;

            cmd.Parameters.Add(new SqlParameter("@TableId", SqlDbType.Int) { Value = tableId });
            cmd.Parameters.Add(new SqlParameter("@StaffId", SqlDbType.NVarChar, 450) { Value = staffId });
            cmd.Parameters.Add(new SqlParameter("@BookingId", SqlDbType.Int) { Value = DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.NVarChar, 450) { Value = DBNull.Value });

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int sessionIdOrdinal = reader.GetOrdinal("SessionId");
                int sessionId = reader.GetInt32(sessionIdOrdinal);

                return TableOperationResult.Ok(new { SessionId = sessionId });
            }

            return TableOperationResult.Fail(null, "Không nhận được phản hồi từ hệ thống cơ sở dữ liệu khi mở phiên.", autoReload: true);
        }
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
        if (sessionId <= 0)
        {
            return TableOperationResult.Fail(null, "Mã phiên chơi không hợp lệ.", autoReload: false);
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

                return TableOperationResult.Ok(new
                {
                    SessionId = id,
                    PlaytimeAmount = playtimeAmount,
                    HourlyRateSnapshot = hourlyRateSnapshot,
                    StartAtUtc = startAtUtc,
                    EndAtUtc = endAtUtc
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

    private TableOperationResult HandleSqlException(SqlException ex, string operationName, int targetId)
    {
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
        51401 or 51402 or 51403 or 51404 or 51405 or 51406 or 51407 or 51408 or 51501 or 51502 => true,
        _ => false
    };

    private static bool IsAutoReloadError(int errorNumber) => errorNumber switch
    {
        51402 or 51403 or 51404 or 51405 or 51406 or 51407 or 51502 => true,
        _ => false
    };

    private static string MapErrorCode(int errorNumber) => errorNumber switch
    {
        51401 => "Thao tác yêu cầu tài khoản Nhân viên hoặc Quản trị viên đang hoạt động.",
        51402 => "Bàn hiện không ở trạng thái Trống để có thể mở phiên.",
        51403 => "Bàn này đã có một phiên chơi đang hoạt động.",
        51404 => "Lượt đặt bàn không hợp lệ (chưa check-in, sai bàn hoặc đã hết hạn giữ chỗ).",
        51405 => "Khách hàng không khớp với thông tin người đặt trước.",
        51406 => "Lượt đặt bàn này đã được mở phiên chơi trước đó.",
        51407 => "Bàn đang được giữ chỗ cho khách đặt trước trong khung giờ này.",
        51408 => "Tài khoản hội viên của khách hàng không tồn tại hoặc đang bị khóa.",
        51501 => "Thao tác đóng phiên yêu cầu quyền Nhân viên hoặc Quản trị viên đang hoạt động.",
        51502 => "Phiên chơi không còn ở trạng thái Hoạt động (có thể đã được nhân viên khác đóng).",
        _ => "Đã xảy ra lỗi khi kết nối hoặc xử lý cơ sở dữ liệu. Vui lòng thử lại sau."
    };
}
