using System.Data;
using Bms.Web.Data;
using Bms.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bms.Web.Services;

public class BookingService : IBookingService
{
    private readonly ApplicationDbContext _context;
    private readonly ITableSessionService _tableSessionService;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        ApplicationDbContext context,
        ITableSessionService tableSessionService,
        ILogger<BookingService> logger)
    {
        _context = context;
        _tableSessionService = tableSessionService;
        _logger = logger;
    }

    public async Task<int> AutoExpireOverdueBookingsAsync(CancellationToken cancellationToken = default)
    {
        var conn = (SqlConnection)_context.Database.GetDbConnection();
        var openedHere = conn.State == ConnectionState.Closed;

        try
        {
            if (openedHere)
            {
                await conn.OpenAsync(cancellationToken);
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "dbo.usp_AutoExpireBookings";
            cmd.CommandType = CommandType.StoredProcedure;

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi chạy tự động cập nhật trạng thái NoShow (usp_AutoExpireBookings).");
            return 0;
        }
        finally
        {
            if (openedHere && conn.State == ConnectionState.Open)
            {
                await conn.CloseAsync();
            }
        }
    }

    public async Task<List<BookableTableOptionViewModel>> GetBookableTablesAsync(DateTime startAtUtc)
    {
        await AutoExpireOverdueBookingsAsync();

        var nowUtc = DateTime.UtcNow;
        var endAtUtc = startAtUtc.AddHours(2);

        var tables = await (from t in _context.BilliardTables.AsNoTracking()
                            join ty in _context.TableTypes.AsNoTracking() on t.TableTypeId equals ty.Id
                            where t.Status != "Inactive"
                            orderby t.FloorNumber, t.TableCode
                            select new
                            {
                                t.Id,
                                t.TableCode,
                                TableTypeName = ty.Name,
                                t.FloorNumber,
                                ty.HourlyRate,
                                t.Status
                            }).ToListAsync();

        var overlappingTableIds = await _context.Bookings.AsNoTracking()
            .Where(b => (b.Status == "Confirmed" || b.Status == "CheckedIn")
                        && b.StartAtUtc < endAtUtc
                        && b.EndAtUtc > startAtUtc)
            .Select(b => b.TableId)
            .Distinct()
            .ToListAsync();

        var overlappingSet = overlappingTableIds.ToHashSet();

        return tables.Select(t =>
        {
            bool bookable = true;
            string? reason = null;

            if (t.Status == "Maintenance")
            {
                bookable = false;
                reason = "Bàn đang bảo trì";
            }
            else if (t.Status == "InUse" && startAtUtc < nowUtc.AddMinutes(5))
            {
                bookable = false;
                reason = "Bàn đang có khách chơi (cần đặt cách hiện tại ít nhất 5 phút)";
            }
            else if (overlappingSet.Contains(t.Id))
            {
                bookable = false;
                reason = "Đã có khách đặt trong khung giờ này";
            }

            return new BookableTableOptionViewModel
            {
                TableId = t.Id,
                TableCode = t.TableCode,
                TableTypeName = t.TableTypeName,
                FloorNumber = t.FloorNumber,
                HourlyRate = t.HourlyRate,
                CurrentStatus = t.Status,
                IsBookableAtSelectedTime = bookable,
                UnavailableReason = reason
            };
        }).ToList();
    }

    public async Task<List<CustomerBookingItemViewModel>> GetCustomerBookingsAsync(string customerId)
    {
        await AutoExpireOverdueBookingsAsync();

        var nowUtc = DateTime.UtcNow;

        var items = await (from b in _context.Bookings.AsNoTracking()
                           where b.CustomerId == customerId
                           join t in _context.BilliardTables.AsNoTracking() on b.TableId equals t.Id
                           join ty in _context.TableTypes.AsNoTracking() on t.TableTypeId equals ty.Id
                           orderby b.StartAtUtc descending
                           select new
                           {
                               b.Id,
                               b.TableId,
                               t.TableCode,
                               TableTypeName = ty.Name,
                               t.FloorNumber,
                               ty.HourlyRate,
                               b.StartAtUtc,
                               b.CreatedAtUtc,
                               b.Status,
                               b.Notes,
                               b.CancellationReason
                           }).ToListAsync();

        return items.Select(x =>
        {
            var startUtc = DateTime.SpecifyKind(x.StartAtUtc, DateTimeKind.Utc);
            var createdUtc = DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc);

            return new CustomerBookingItemViewModel
            {
                BookingId = x.Id,
                TableId = x.TableId,
                TableCode = x.TableCode,
                TableTypeName = x.TableTypeName,
                FloorNumber = x.FloorNumber,
                HourlyRate = x.HourlyRate,
                StartAtUtc = startUtc,
                HoldUntilUtc = startUtc.AddMinutes(15),
                CreatedAtUtc = createdUtc,
                Status = x.Status,
                DisplayStatus = MapBookingStatus(x.Status),
                Notes = x.Notes,
                CancellationReason = x.CancellationReason,
                CanCancel = x.Status == "Confirmed" && startUtc > nowUtc
            };
        }).ToList();
    }

    public async Task<TableOperationResult> CreateBookingAsync(string customerId, int tableId, DateTime startAtUtc, string? notes)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return TableOperationResult.Fail(51102, "Bạn phải đăng nhập bằng tài khoản Khách hàng để đặt bàn.");
        }

        if (tableId <= 0)
        {
            return TableOperationResult.Fail(51103, "Vui lòng chọn một bàn hợp lệ.");
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
            cmd.CommandText = "dbo.usp_CreateBooking";
            cmd.CommandType = CommandType.StoredProcedure;

            cmd.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.NVarChar, 450) { Value = customerId });
            cmd.Parameters.Add(new SqlParameter("@TableId", SqlDbType.Int) { Value = tableId });
            cmd.Parameters.Add(new SqlParameter("@StartAtUtc", SqlDbType.DateTime2) { Value = startAtUtc });
            cmd.Parameters.Add(new SqlParameter("@EndAtUtc", SqlDbType.DateTime2) { Value = startAtUtc.AddHours(2) });
            cmd.Parameters.Add(new SqlParameter("@Notes", SqlDbType.NVarChar, 300)
            {
                Value = string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes.Trim()
            });

            var scalar = await cmd.ExecuteScalarAsync();
            int bookingId = scalar != null && scalar != DBNull.Value ? Convert.ToInt32(scalar) : 0;

            return TableOperationResult.Ok(
                new { BookingId = bookingId },
                "Đặt bàn thành công! Quán sẽ giữ bàn cho bạn trong vòng 15 phút kể từ giờ hẹn.");
        }
        catch (SqlException ex)
        {
            return MapBookingSqlException(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi tạo đặt bàn cho khách {CustomerId} tại bàn {TableId}.", customerId, tableId);
            return TableOperationResult.Fail(null, "Đã xảy ra lỗi hệ thống khi đặt bàn. Vui lòng thử lại.");
        }
        finally
        {
            if (openedHere && conn.State == ConnectionState.Open)
            {
                await conn.CloseAsync();
            }
        }
    }

    public async Task<TableOperationResult> CancelBookingAsync(int bookingId, string actorId, string? reason)
    {
        if (bookingId <= 0 || string.IsNullOrWhiteSpace(actorId))
        {
            return TableOperationResult.Fail(null, "Thông tin hủy đặt bàn không hợp lệ.");
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
            cmd.CommandText = "dbo.usp_CancelBooking";
            cmd.CommandType = CommandType.StoredProcedure;

            cmd.Parameters.Add(new SqlParameter("@BookingId", SqlDbType.Int) { Value = bookingId });
            cmd.Parameters.Add(new SqlParameter("@ActorId", SqlDbType.NVarChar, 450) { Value = actorId });
            cmd.Parameters.Add(new SqlParameter("@Reason", SqlDbType.NVarChar, 300)
            {
                Value = string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason.Trim()
            });

            await cmd.ExecuteNonQueryAsync();
            return TableOperationResult.Ok(null, "Đã hủy lịch đặt bàn thành công.");
        }
        catch (SqlException ex)
        {
            return MapBookingSqlException(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi hủy đặt bàn {BookingId} bởi {ActorId}.", bookingId, actorId);
            return TableOperationResult.Fail(null, "Không thể hủy lịch đặt bàn. Vui lòng thử lại.");
        }
        finally
        {
            if (openedHere && conn.State == ConnectionState.Open)
            {
                await conn.CloseAsync();
            }
        }
    }

    public async Task<StaffBookingManagementViewModel> SearchBookingsForStaffAsync(string? keyword, string? statusFilter, DateTime? dateLocal)
    {
        await AutoExpireOverdueBookingsAsync();

        var nowUtc = DateTime.UtcNow;

        var baseQuery = from b in _context.Bookings.AsNoTracking()
                        join u in _context.Users.AsNoTracking() on b.CustomerId equals u.Id
                        join t in _context.BilliardTables.AsNoTracking() on b.TableId equals t.Id
                        join ty in _context.TableTypes.AsNoTracking() on t.TableTypeId equals ty.Id
                        join s in _context.PlaySessions.AsNoTracking() on b.Id equals s.BookingId into sessionGroup
                        from linkedSession in sessionGroup.DefaultIfEmpty()
                        select new
                        {
                            BookingId = b.Id,
                            b.CustomerId,
                            CustomerName = u.FullName,
                            CustomerPhone = u.PhoneNumber,
                            b.TableId,
                            t.TableCode,
                            TableTypeName = ty.Name,
                            t.FloorNumber,
                            ty.HourlyRate,
                            TableStatus = t.Status,
                            b.StartAtUtc,
                            b.Status,
                            b.CheckedInAtUtc,
                            b.Notes,
                            b.CancellationReason,
                            LinkedSessionId = (int?)linkedSession.Id,
                            LinkedSessionStatus = linkedSession != null ? linkedSession.Status : null
                        };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            if (int.TryParse(kw, out int idVal))
            {
                baseQuery = baseQuery.Where(x =>
                    x.BookingId == idVal ||
                    (x.CustomerPhone != null && x.CustomerPhone.Contains(kw)) ||
                    x.CustomerName.Contains(kw) ||
                    x.TableCode.Contains(kw));
            }
            else
            {
                baseQuery = baseQuery.Where(x =>
                    (x.CustomerPhone != null && x.CustomerPhone.Contains(kw)) ||
                    x.CustomerName.Contains(kw) ||
                    x.TableCode.Contains(kw));
            }
        }

        if (!string.IsNullOrWhiteSpace(statusFilter) && !string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            baseQuery = baseQuery.Where(x => x.Status == statusFilter);
        }

        if (dateLocal.HasValue)
        {
            // Chuyển ngày giờ Việt Nam (UTC+7) sang khoảng UTC
            var startOfDayVn = dateLocal.Value.Date;
            var startUtc = startOfDayVn.AddHours(-7);
            var endUtc = startUtc.AddDays(1);
            baseQuery = baseQuery.Where(x => x.StartAtUtc >= startUtc && x.StartAtUtc < endUtc);
        }

        var rawList = await baseQuery
            .OrderBy(x => x.Status == "CheckedIn" ? 0 : x.Status == "Confirmed" ? 1 : 2)
            .ThenBy(x => x.StartAtUtc)
            .Take(200)
            .ToListAsync();

        var availableTables = await (from t in _context.BilliardTables.AsNoTracking()
                                     join ty in _context.TableTypes.AsNoTracking() on t.TableTypeId equals ty.Id
                                     where t.Status == "Available"
                                     orderby t.FloorNumber, t.TableCode
                                     select new AssignableTableOptionViewModel
                                     {
                                         TableId = t.Id,
                                         TableCode = t.TableCode,
                                         TableTypeName = ty.Name,
                                         FloorNumber = t.FloorNumber,
                                         HourlyRate = ty.HourlyRate
                                     }).ToListAsync();

        var bookings = rawList.Select(x =>
        {
            var startUtc = DateTime.SpecifyKind(x.StartAtUtc, DateTimeKind.Utc);
            var holdUntilUtc = startUtc.AddMinutes(15);
            var mustVacateByUtc = startUtc.AddMinutes(-5);
            var checkedInUtc = x.CheckedInAtUtc.HasValue
                ? DateTime.SpecifyKind(x.CheckedInAtUtc.Value, DateTimeKind.Utc)
                : (DateTime?)null;

            var item = new StaffBookingItemViewModel
            {
                BookingId = x.BookingId,
                CustomerId = x.CustomerId,
                CustomerName = x.CustomerName,
                CustomerPhone = x.CustomerPhone,
                TableId = x.TableId,
                TableCode = x.TableCode,
                TableTypeName = x.TableTypeName,
                FloorNumber = x.FloorNumber,
                HourlyRate = x.HourlyRate,
                TableCurrentStatus = x.TableStatus,
                TableDisplayStatus = TableSessionService.MapDisplayStatus(x.TableStatus),
                StartAtUtc = startUtc,
                HoldUntilUtc = holdUntilUtc,
                MustVacateByUtc = mustVacateByUtc,
                Status = x.Status,
                DisplayStatus = MapBookingStatus(x.Status),
                CheckedInAtUtc = checkedInUtc,
                LinkedSessionId = x.LinkedSessionId,
                LinkedSessionStatus = x.LinkedSessionStatus,
                Notes = x.Notes,
                CancellationReason = x.CancellationReason
            };

            PopulateBusinessScenario(item, nowUtc, availableTables.Count > 0);
            return item;
        }).ToList();

        return new StaffBookingManagementViewModel
        {
            Keyword = keyword,
            StatusFilter = statusFilter,
            DateFilterLocal = dateLocal,
            Bookings = bookings,
            AvailableTables = availableTables,
            ServerTimeUtc = nowUtc
        };
    }

    private static void PopulateBusinessScenario(StaffBookingItemViewModel item, DateTime nowUtc, bool anyTableAvailable)
    {
        if (item.Status == "Cancelled")
        {
            item.ScenarioCode = "Cancelled";
            item.ScenarioTitle = "Booking đã bị hủy";
            item.ScenarioBadgeClass = "bg-secondary";
            item.BusinessGuidance = "Không Check-in bằng booking này. Hướng dẫn khách đặt lại hoặc kiểm tra khả năng phục vụ trực tiếp.";
            return;
        }

        if (item.Status == "NoShow")
        {
            item.ScenarioCode = "LateOver15m";
            item.ScenarioTitle = "Khách đến muộn quá 15 phút (No-Show)";
            item.ScenarioBadgeClass = "bg-danger";
            item.BusinessGuidance = "Booking đã vượt thời gian giữ chỗ 15 phút và tự động chuyển No-Show. Nhân viên kiểm tra trạng thái bàn thực tế và xử lý theo chính sách No-show của quán.";
            return;
        }

        if (item.Status == "Completed")
        {
            item.ScenarioCode = "Completed";
            item.ScenarioTitle = "Đã hoàn thành phiên chơi";
            item.ScenarioBadgeClass = "bg-dark";
            item.BusinessGuidance = "Booking này đã được Check-in, mở bàn và kết thúc phiên chơi.";
            return;
        }

        if (item.Status == "CheckedIn")
        {
            if (item.LinkedSessionId.HasValue)
            {
                item.ScenarioCode = "AlreadyCheckedInPlaying";
                item.ScenarioTitle = "Đã Check-in trước đó (Đang chơi)";
                item.ScenarioBadgeClass = "bg-primary";
                item.BusinessGuidance = "Không tạo Check-in lần nữa. Khách đã được bố trí bàn và đang trong phiên chơi.";
            }
            else if (item.TableCurrentStatus == "Available" || anyTableAvailable)
            {
                item.ScenarioCode = "CheckedInReadyToAssign";
                item.ScenarioTitle = "Đã Check-in — Chờ nhân viên chọn bàn";
                item.ScenarioBadgeClass = "bg-success";
                item.BusinessGuidance = "Khách đã được ghi nhận đến. Nhân viên nhấn 'Chọn bàn & Mở phiên' để bắt đầu tính giờ.";
                item.CanAssignTableAndOpen = true;
            }
            else
            {
                item.ScenarioCode = "CheckedInWaitingTable";
                item.ScenarioTitle = "Đã đến giờ nhưng chưa có bàn trống";
                item.ScenarioBadgeClass = "bg-warning text-dark";
                item.BusinessGuidance = "Đã ghi nhận khách đến nhưng hiện chưa có bàn trống. Nhân viên yêu cầu bàn đang chơi kết thúc/chuyển bàn rồi bố trí bàn cho khách.";
                item.CanAssignTableAndOpen = false;
            }
            return;
        }

        // Status == "Confirmed"
        item.CanCancel = true;

        var minutesDiff = (nowUtc - item.StartAtUtc).TotalMinutes; // âm: đến sớm, dương: đến muộn

        if (minutesDiff < -15)
        {
            item.ScenarioCode = "EarlyArrival";
            item.ScenarioTitle = "Khách đến sớm";
            item.ScenarioBadgeClass = "bg-info text-dark";
            if (item.TableCurrentStatus == "Available")
            {
                item.BusinessGuidance = $"Bàn {item.TableCode} hiện đang Trống. Quán có thể phục vụ sớm ngay hoặc để khách chờ đến giờ hẹn.";
                item.CanCheckInEarly = true;
            }
            else
            {
                item.BusinessGuidance = $"Bàn {item.TableCode} hiện đang {item.TableDisplayStatus}. Khách chờ đến giờ phù hợp (bàn hiện tại bắt buộc trống trước giờ đặt 5 phút) hoặc kiểm tra bàn trống khác.";
                item.CanCheckInEarly = anyTableAvailable;
            }
        }
        else if (minutesDiff <= 0)
        {
            if (item.TableCurrentStatus != "Available")
            {
                item.ScenarioCode = "OnTimeNoTableYet";
                item.ScenarioTitle = "Khách đến đúng giờ (Bàn đặt đang thu dọn/chưa trống)";
                item.ScenarioBadgeClass = "bg-warning text-dark";
                item.BusinessGuidance = "Ghi nhận khách đã đến (Check-in) để giữ quyền ưu tiên, sau đó nhân viên bố trí bàn ngay khi bàn trống hoặc chọn bàn trống khác.";
                item.CanCheckInNormal = true;
            }
            else
            {
                item.ScenarioCode = "OnTime";
                item.ScenarioTitle = "Khách đến đúng giờ";
                item.ScenarioBadgeClass = "bg-success";
                item.BusinessGuidance = "Xác nhận khách đã đến (Check-in) và chuyển sang chọn bàn mở phiên chơi.";
                item.CanCheckInNormal = true;
            }
        }
        else if (minutesDiff <= 15)
        {
            item.ScenarioCode = "LateUnder15m";
            item.ScenarioTitle = $"Khách đến muộn {(int)Math.Ceiling(minutesDiff)} phút (Chưa quá 15 phút)";
            item.ScenarioBadgeClass = "bg-warning text-dark";
            item.BusinessGuidance = "Vẫn trong thời gian giữ bàn 15 phút. Giữ quyền ưu tiên theo booking, xác nhận Check-in và chọn bàn phù hợp cho khách.";
            item.CanCheckInNormal = true;
        }
        else
        {
            item.ScenarioCode = "LateOver15m";
            item.ScenarioTitle = "Khách đến muộn quá 15 phút";
            item.ScenarioBadgeClass = "bg-danger";
            item.BusinessGuidance = "Booking đã vượt thời gian giữ chỗ 15 phút; xử lý theo chính sách No-show của quán.";
            item.CanCancel = false;
        }
    }

    public async Task<TableOperationResult> CheckInBookingAsync(int bookingId, string staffId, bool allowEarly = false)
    {
        if (bookingId <= 0 || string.IsNullOrWhiteSpace(staffId))
        {
            return TableOperationResult.Fail(null, "Thông tin Check-in không hợp lệ.");
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
            cmd.CommandText = "dbo.usp_CheckInBooking";
            cmd.CommandType = CommandType.StoredProcedure;

            cmd.Parameters.Add(new SqlParameter("@BookingId", SqlDbType.Int) { Value = bookingId });
            cmd.Parameters.Add(new SqlParameter("@StaffId", SqlDbType.NVarChar, 450) { Value = staffId });
            cmd.Parameters.Add(new SqlParameter("@AllowEarly", SqlDbType.Bit) { Value = allowEarly });

            await cmd.ExecuteNonQueryAsync();
            return TableOperationResult.Ok(
                new { BookingId = bookingId },
                "Đã xác nhận khách đến (Check-in) thành công! Vui lòng chọn bàn để mở phiên chơi cho khách.");
        }
        catch (SqlException ex)
        {
            return MapBookingSqlException(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi Check-in booking {BookingId} bởi nhân viên {StaffId}.", bookingId, staffId);
            return TableOperationResult.Fail(null, "Đã xảy ra lỗi khi Check-in. Vui lòng thử lại.");
        }
        finally
        {
            if (openedHere && conn.State == ConnectionState.Open)
            {
                await conn.CloseAsync();
            }
        }
    }

    public async Task<TableOperationResult> AssignTableAndOpenSessionAsync(int bookingId, int selectedTableId, string staffId)
    {
        if (bookingId <= 0 || selectedTableId <= 0)
        {
            return TableOperationResult.Fail(null, "Vui lòng chọn booking và bàn hợp lệ.");
        }

        return await _tableSessionService.OpenSessionAsync(selectedTableId, staffId, bookingId);
    }

    public static string MapBookingStatus(string status) => status switch
    {
        "Confirmed" => "Đã đặt (Giữ chỗ 15p)",
        "CheckedIn" => "Đã Check-in (Chờ/Đang chơi)",
        "Completed" => "Hoàn thành",
        "Cancelled" => "Đã hủy",
        "NoShow" => "Quá hạn 15p (No-Show)",
        _ => status
    };

    private static TableOperationResult MapBookingSqlException(SqlException ex) => ex.Number switch
    {
        51101 => TableOperationResult.Fail(51101, "Thời gian đặt bàn phải lớn hơn thời điểm hiện tại."),
        51102 => TableOperationResult.Fail(51102, "Bạn cần đăng nhập tài khoản Khách hàng đang hoạt động để đặt bàn."),
        51103 => TableOperationResult.Fail(51103, "Bàn đã chọn hiện đang bảo trì hoặc ngừng hoạt động."),
        51104 => TableOperationResult.Fail(51104, "Bàn này đã có lịch đặt trùng giờ hoặc đang có khách chơi (cần cách hiện tại ít nhất 5 phút)."),
        51105 => TableOperationResult.Fail(51105, "Chỉ được đặt bàn trước tối đa 15 ngày."),
        51201 => TableOperationResult.Fail(51201, "Chỉ có thể hủy lịch đặt bàn đang ở trạng thái Đã đặt (Confirmed)."),
        51202 => TableOperationResult.Fail(51202, "Tài khoản thực hiện không hợp lệ hoặc đã bị khóa."),
        51203 => TableOperationResult.Fail(51203, "Khách hàng chỉ có thể hủy lịch đặt sắp tới của chính mình."),
        51300 => TableOperationResult.Fail(51300, "Không tìm thấy booking. Vui lòng kiểm tra lại thông tin hoặc mở bàn theo quy trình khách trực tiếp."),
        51301 => TableOperationResult.Fail(51301, "Thao tác yêu cầu quyền Nhân viên hoặc Quản trị viên."),
        51302 => TableOperationResult.Fail(51302, "Booking đã bị hủy. Không thể Check-in bằng booking này; vui lòng hướng dẫn khách đặt lại hoặc mở bàn trực tiếp."),
        51303 => TableOperationResult.Fail(51303, "Khách đến sớm hơn 15 phút. Nếu quán có thể phục vụ sớm ngay, hãy nhấn 'Check-in phục vụ sớm'."),
        51306 => TableOperationResult.Fail(51306, "Booking này đã được Check-in trước đó. Không tạo Check-in trùng."),
        51307 => TableOperationResult.Fail(51307, "Khách đến muộn quá 15 phút: Booking đã vượt thời gian giữ chỗ và chuyển sang No-Show."),
        _ => TableOperationResult.Fail(ex.Number, "Không thể thực hiện thao tác đặt bàn do lỗi dữ liệu. Vui lòng thử lại.")
    };
}
