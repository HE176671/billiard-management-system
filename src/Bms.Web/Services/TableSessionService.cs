using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Bms.Web.Services;

public class TableSessionService : ITableSessionService
{
    private readonly ApplicationDbContext _context;

    public TableSessionService(ApplicationDbContext context)
    {
        _context = context;
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
                        SessionStartUtc = (DateTime?)activeSession.StartAtUtc
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
                        CustomerFullName = customerUser != null ? customerUser.FullName : null
                    };

        var raw = await query.FirstOrDefaultAsync();
        if (raw == null)
        {
            return null;
        }

        return new TableDetailViewModel
        {
            TableId = raw.TableId,
            TableCode = raw.TableCode,
            TableTypeName = raw.TableTypeName,
            HourlyRate = raw.HourlyRate,
            Status = raw.Status,
            DisplayStatus = MapDisplayStatus(raw.Status),
            SessionId = raw.SessionId,
            StartAtUtc = raw.StartAtUtc.HasValue
                ? DateTime.SpecifyKind(raw.StartAtUtc.Value, DateTimeKind.Utc)
                : null,
            EndAtUtc = raw.EndAtUtc.HasValue
                ? DateTime.SpecifyKind(raw.EndAtUtc.Value, DateTimeKind.Utc)
                : null,
            HourlyRateSnapshot = raw.HourlyRateSnapshot,
            CustomerFullName = raw.CustomerFullName,
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
}
