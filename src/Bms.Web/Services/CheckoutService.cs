using System.Data;
using Bms.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Web.Services;

public record CheckoutCommand(
    string? CustomerId,
    string ReferenceCode,
    int? SessionId,
    decimal SubmittedPlaytimeAmount,
    decimal SubmittedFandBAmount,
    int PointsToRedeem,
    string? VoucherCode,
    string? CashierId,
    decimal? CashReceived = null);

public record CheckoutPaymentResult(
    bool Success,
    bool Duplicate,
    bool Pending,
    string Message,
    int? InvoiceId = null,
    decimal FinalAmount = 0,
    decimal ChangeAmount = 0,
    string? ReferenceCode = null);

public interface ICheckoutService
{
    Task<CheckoutPaymentResult> CompleteCashAsync(CheckoutCommand command, CancellationToken ct = default);
    Task<CheckoutPaymentResult> CreateTransferIntentAsync(CheckoutCommand command, CancellationToken ct = default);
    Task<CheckoutPaymentResult> ConfirmTransferAsync(string referenceCode, string providerTransactionId, decimal receivedAmount, CancellationToken ct = default);
}

public class CheckoutService : ICheckoutService
{
    private readonly ApplicationDbContext _db;
    private readonly ILoyaltyService _loyalty;
    private readonly ILogger<CheckoutService> _logger;

    public CheckoutService(ApplicationDbContext db, ILoyaltyService loyalty, ILogger<CheckoutService> logger)
    {
        _db = db;
        _loyalty = loyalty;
        _logger = logger;
    }

    public async Task<CheckoutPaymentResult> CompleteCashAsync(CheckoutCommand command, CancellationToken ct = default)
    {
        await using var tx = await BeginCheckoutTransactionAsync("Cash", ct);
        try
        {
            var duplicate = await FindCompletedByReferenceAsync(command.ReferenceCode, ct);
            if (duplicate != null)
            {
                await tx.RollbackAsync(ct);
                return duplicate;
            }

            var prepared = await PrepareInvoiceAsync(command, ct);
            if (!prepared.Success)
            {
                await tx.RollbackAsync(ct);
                return prepared.Result!;
            }

            var finalAmount = await CalculateFinalAmountAsync(command, prepared, ct, commitLoyalty: false);
            if (!finalAmount.Success)
            {
                await tx.RollbackAsync(ct);
                return finalAmount.Result!;
            }

            var cashReceived = command.CashReceived ?? finalAmount.Amount;
            if (cashReceived < finalAmount.Amount)
            {
                await tx.RollbackAsync(ct);
                return new(false, false, false,
                    $"Số tiền khách đưa ({VnFormat.Money(cashReceived)}) nhỏ hơn tổng thanh toán ({VnFormat.Money(finalAmount.Amount)}).",
                    prepared.InvoiceId, finalAmount.Amount, 0, command.ReferenceCode);
            }

            var settled = await SettleAsync(command, prepared, finalAmount.Amount, "Cash", command.ReferenceCode, true, ct);
            if (!settled.Success)
            {
                await tx.RollbackAsync(ct);
                return settled;
            }

            await tx.CommitAsync(ct);
            return settled with { ChangeAmount = cashReceived - finalAmount.Amount };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Thanh toán tiền mặt thất bại. ReferenceCode={ReferenceCode}", command.ReferenceCode);
            return new(false, false, false, "Không thể ghi nhận thanh toán. Không có dữ liệu nào được thay đổi; vui lòng thử lại.", ReferenceCode: command.ReferenceCode);
        }
    }

    public async Task<CheckoutPaymentResult> CreateTransferIntentAsync(CheckoutCommand command, CancellationToken ct = default)
    {
        await using var tx = await BeginCheckoutTransactionAsync("Intent", ct);
        try
        {
            var refCode = NormalizeReference(command.ReferenceCode);
            var existing = await _db.PaymentIntents.AsNoTracking().FirstOrDefaultAsync(x => x.ReferenceCode == refCode, ct);
            if (existing != null)
            {
                await tx.RollbackAsync(ct);
                return new(existing.Status == PaymentIntent.StatusConfirmed, true,
                    existing.Status == PaymentIntent.StatusPending,
                    existing.Status == PaymentIntent.StatusPending
                        ? "Yêu cầu chuyển khoản này đang chờ ngân hàng xác nhận."
                        : "Yêu cầu chuyển khoản này đã được xử lý.",
                    existing.InvoiceId, existing.ExpectedAmount, 0, existing.ReferenceCode);
            }

            var prepared = await PrepareInvoiceAsync(command with { ReferenceCode = refCode }, ct);
            if (!prepared.Success)
            {
                await tx.RollbackAsync(ct);
                return prepared.Result!;
            }

            var finalAmount = await CalculateFinalAmountAsync(command with { ReferenceCode = refCode }, prepared, ct, commitLoyalty: false);
            if (!finalAmount.Success)
            {
                await tx.RollbackAsync(ct);
                return finalAmount.Result!;
            }

            var now = DateTime.UtcNow;
            _db.PaymentIntents.Add(new PaymentIntent
            {
                InvoiceId = prepared.InvoiceId,
                ReferenceCode = refCode,
                ExpectedAmount = finalAmount.Amount,
                Provider = "SePay",
                Status = PaymentIntent.StatusPending,
                CustomerId = command.CustomerId,
                SessionId = prepared.SessionId,
                PlaytimeAmount = prepared.PlaytimeAmount,
                FandBAmount = prepared.FandBAmount,
                PointsToRedeem = command.PointsToRedeem,
                VoucherCode = NormalizeOptional(command.VoucherCode),
                CashierId = command.CashierId,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(15)
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new(true, false, true,
                "Đã tạo yêu cầu VietQR. Hóa đơn chỉ được ghi nhận đã thanh toán sau khi ngân hàng xác nhận.",
                prepared.InvoiceId, finalAmount.Amount, 0, refCode);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Tạo yêu cầu VietQR thất bại; toàn bộ thay đổi đã được hoàn tác. ReferenceCode={ReferenceCode}", command.ReferenceCode);
            return new(false, false, false, "Không thể tạo yêu cầu VietQR; vui lòng thử lại.", ReferenceCode: command.ReferenceCode);
        }
    }

    public async Task<CheckoutPaymentResult> ConfirmTransferAsync(
        string referenceCode,
        string providerTransactionId,
        decimal receivedAmount,
        CancellationToken ct = default)
    {
        await using var tx = await BeginCheckoutTransactionAsync("Webhook", ct);
        try
        {
            var refCode = NormalizeReference(referenceCode);
            var intent = await _db.PaymentIntents
                .FromSqlInterpolated($"SELECT * FROM dbo.PaymentIntents WITH (UPDLOCK, HOLDLOCK) WHERE ReferenceCode = {refCode}")
                .SingleOrDefaultAsync(ct);

            if (intent == null)
            {
                await tx.RollbackAsync(ct);
                return new(false, false, false, "Không tìm thấy yêu cầu thanh toán tương ứng.", ReferenceCode: refCode);
            }

            if (intent.Status == PaymentIntent.StatusConfirmed)
            {
                await tx.RollbackAsync(ct);
                return new(true, true, false, "Giao dịch đã được xác nhận trước đó.", intent.InvoiceId, intent.ExpectedAmount, 0, refCode);
            }

            if (intent.Status != PaymentIntent.StatusPending)
            {
                await tx.RollbackAsync(ct);
                return new(false, false, false, $"Yêu cầu thanh toán đang ở trạng thái {intent.Status}.", intent.InvoiceId, intent.ExpectedAmount, 0, refCode);
            }

            if (DateTime.UtcNow > intent.ExpiresAtUtc)
            {
                intent.Status = PaymentIntent.StatusExpired;
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return new(false, false, false, "Yêu cầu thanh toán đã hết hạn.", intent.InvoiceId, intent.ExpectedAmount, 0, refCode);
            }

            if (receivedAmount != intent.ExpectedAmount)
            {
                await tx.RollbackAsync(ct);
                return new(false, false, false,
                    $"Số tiền nhận được {VnFormat.Money(receivedAmount)} không khớp số tiền cần thanh toán {VnFormat.Money(intent.ExpectedAmount)}.",
                    intent.InvoiceId, intent.ExpectedAmount, 0, refCode);
            }

            var providerDuplicate = await _db.PaymentIntents.AsNoTracking()
                .AnyAsync(x => x.ProviderTransactionId == providerTransactionId && x.Id != intent.Id, ct);
            if (providerDuplicate)
            {
                await tx.RollbackAsync(ct);
                return new(false, true, false, "Mã giao dịch ngân hàng đã được dùng cho một thanh toán khác.", ReferenceCode: refCode);
            }

            var command = new CheckoutCommand(
                intent.CustomerId, intent.ReferenceCode, intent.SessionId,
                intent.PlaytimeAmount, intent.FandBAmount, intent.PointsToRedeem,
                intent.VoucherCode, intent.CashierId);
            var prepared = new PreparedInvoice(true, intent.InvoiceId, intent.SessionId ?? 0,
                intent.PlaytimeAmount, intent.FandBAmount, null);

            var settled = await SettleAsync(command, prepared, intent.ExpectedAmount, "Transfer", providerTransactionId, false, ct);
            if (!settled.Success)
            {
                await tx.RollbackAsync(ct);
                return settled;
            }

            intent.Status = PaymentIntent.StatusConfirmed;
            intent.ProviderTransactionId = providerTransactionId;
            intent.ConfirmedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return settled;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Xác nhận webhook thất bại. ReferenceCode={ReferenceCode}, ProviderTransactionId={ProviderTransactionId}", referenceCode, providerTransactionId);
            return new(false, false, false, "Không thể hoàn tất giao dịch; webhook có thể thử gửi lại.", ReferenceCode: referenceCode);
        }
    }

    private async Task<CheckoutPaymentResult?> FindCompletedByReferenceAsync(string referenceCode, CancellationToken ct)
    {
        var refCode = NormalizeReference(referenceCode);
        var payment = await _db.PaymentTransactions.AsNoTracking().FirstOrDefaultAsync(x => x.GatewayRefCode == refCode, ct);
        return payment == null
            ? null
            : new(true, true, false, "Giao dịch đã được ghi nhận trước đó.", payment.InvoiceId, payment.AmountPaid, 0, refCode);
    }

    private async Task<PreparedInvoice> PrepareInvoiceAsync(CheckoutCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.CashierId))
            return PreparedInvoice.Fail("Không xác định được nhân viên thực hiện thanh toán.", command.ReferenceCode);

        decimal playtime = command.SubmittedPlaytimeAmount;
        decimal fandB = command.SubmittedFandBAmount;
        int sessionId = command.SessionId ?? 0;

        if (sessionId > 0)
        {
            bool hasPendingOrders = await _db.Orders.AsNoTracking()
                .AnyAsync(o => o.SessionId == sessionId
                            && (o.Status == OrderConstants.StatusPending
                                || o.Status == OrderConstants.StatusPreparing), ct);
            if (hasPendingOrders)
            {
                return PreparedInvoice.Fail(
                    "Bàn đang có đơn F&B chờ Staff xác nhận. Vui lòng duyệt hoặc hủy đơn trước khi thanh toán.",
                    command.ReferenceCode);
            }

            var rows = await _db.Database.SqlQuery<CheckoutAmountsRow>($"""
                SELECT TOP 1
                    s.Id AS SessionId,
                    CASE WHEN s.PlaytimeAmount IS NOT NULL AND s.PlaytimeAmount > 0 THEN s.PlaytimeAmount
                         ELSE CAST(ROUND((CASE WHEN DATEDIFF(MINUTE, s.StartAtUtc, ISNULL(s.EndAtUtc, SYSUTCDATETIME())) < 1
                                              THEN 1 ELSE DATEDIFF(MINUTE, s.StartAtUtc, ISNULL(s.EndAtUtc, SYSUTCDATETIME())) END / 60.0)
                                              * s.HourlyRateSnapshot, -3) AS decimal(18,2)) END AS PlaytimeAmount,
                    ISNULL((SELECT SUM(od.Subtotal) FROM dbo.Orders o
                            INNER JOIN dbo.OrderDetails od ON od.OrderId = o.Id
                            WHERE o.SessionId = s.Id AND o.Status = 'Served'), 0) AS FandBAmount
                FROM dbo.PlaySessions s
                WHERE s.Id = {sessionId};
                """).ToListAsync(ct);
            var row = rows.SingleOrDefault();

            if (row == null)
                return PreparedInvoice.Fail("Không tìm thấy phiên bàn cần thanh toán.", command.ReferenceCode);

            playtime = row.PlaytimeAmount;
            fandB = row.FandBAmount;
        }
        else
        {
            if (playtime < 0 || fandB < 0 || playtime + fandB <= 0)
                return PreparedInvoice.Fail("Tổng tiền thanh toán phải lớn hơn 0 và không được âm.", command.ReferenceCode);

            var tableId = await _db.BilliardTables.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync(ct);
            if (tableId <= 0)
                return PreparedInvoice.Fail("Không có bàn để tạo hóa đơn tại quầy.", command.ReferenceCode);

            sessionId = (await _db.Database.SqlQuery<int>($"""
                INSERT INTO dbo.PlaySessions (TableId, CustomerId, OpenedById, ClosedById, StartAtUtc, EndAtUtc, HourlyRateSnapshot, PlaytimeAmount, Status)
                OUTPUT INSERTED.Id AS [Value]
                VALUES ({tableId}, {command.CustomerId}, {command.CashierId}, {command.CashierId}, DATEADD(hour, -1, SYSUTCDATETIME()), SYSUTCDATETIME(), {playtime}, {playtime}, 'Closed');
                """).ToListAsync(ct)).Single();
        }

        var invoiceId = (await _db.Database.SqlQuery<int>($"""
            DECLARE @InvoiceId int = (SELECT TOP 1 Id FROM dbo.Invoices WITH (UPDLOCK, HOLDLOCK) WHERE SessionId = {sessionId});
            IF @InvoiceId IS NULL
            BEGIN
                INSERT INTO dbo.Invoices (SessionId, CustomerId, TotalPlaytime, TotalFandB, DiscountAmount, Status, CreatedAtUtc)
                VALUES ({sessionId}, {command.CustomerId}, {playtime}, {fandB}, 0, 'Unpaid', SYSUTCDATETIME());
                SET @InvoiceId = SCOPE_IDENTITY();
            END
            ELSE
            BEGIN
                UPDATE dbo.Invoices
                SET CustomerId = {command.CustomerId}, TotalPlaytime = {playtime}, TotalFandB = {fandB}
                WHERE Id = @InvoiceId AND Status <> 'Paid';
            END
            SELECT @InvoiceId AS [Value];
            """).ToListAsync(ct)).Single();

        var alreadyPaid = await _db.PaymentTransactions.AsNoTracking().AnyAsync(x => x.InvoiceId == invoiceId, ct);
        if (alreadyPaid)
            return PreparedInvoice.Fail("Hóa đơn của phiên này đã được thanh toán.", command.ReferenceCode, invoiceId);

        return new(true, invoiceId, sessionId, playtime, fandB, null);
    }

    private async Task<CalculatedAmount> CalculateFinalAmountAsync(
        CheckoutCommand command,
        PreparedInvoice prepared,
        CancellationToken ct,
        bool commitLoyalty = true)
    {
        if (string.IsNullOrWhiteSpace(command.CustomerId))
            return new(true, prepared.PlaytimeAmount + prepared.FandBAmount, null);

        var request = new CheckoutLoyaltyRequest(
            command.CustomerId, NormalizeReference(command.ReferenceCode), prepared.InvoiceId,
            prepared.PlaytimeAmount, prepared.FandBAmount, command.PointsToRedeem,
            NormalizeOptional(command.VoucherCode), command.CashierId);

        var preview = await _loyalty.PreviewCheckoutAsync(request, ct);
        if (!preview.IsValid)
            return CalculatedAmount.Fail(string.Join(" ", preview.Errors), command.ReferenceCode, prepared.InvoiceId);

        if (!commitLoyalty)
            return new(true, preview.FinalAmount, null);

        var result = await _loyalty.CommitCheckoutAsync(request, ct);
        if (!result.Success)
            return CalculatedAmount.Fail(result.Message, command.ReferenceCode, prepared.InvoiceId);

        return new(true, result.FinalAmount, null);
    }

    private async Task<CheckoutPaymentResult> SettleAsync(
        CheckoutCommand command,
        PreparedInvoice prepared,
        decimal expectedAmount,
        string method,
        string gatewayReference,
        bool receivedAtCounter,
        CancellationToken ct)
    {
        var calculated = await CalculateFinalAmountAsync(command, prepared, ct);
        if (!calculated.Success)
            return calculated.Result!;

        if (calculated.Amount != expectedAmount)
            return new(false, false, false, "Số tiền hóa đơn đã thay đổi; vui lòng tạo lại yêu cầu thanh toán.", prepared.InvoiceId, calculated.Amount, 0, command.ReferenceCode);

        var discount = prepared.PlaytimeAmount + prepared.FandBAmount - calculated.Amount;
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.Invoices
            SET CustomerId = {command.CustomerId}, TotalPlaytime = {prepared.PlaytimeAmount},
                TotalFandB = {prepared.FandBAmount}, DiscountAmount = {discount}, Status = 'Paid'
            WHERE Id = {prepared.InvoiceId} AND Status <> 'Paid';

            IF OBJECT_ID(N'dbo.PlaySessionTableSegments', N'U') IS NOT NULL
                UPDATE dbo.PlaySessionTableSegments SET EndAtUtc = SYSUTCDATETIME()
                WHERE SessionId = {prepared.SessionId} AND EndAtUtc IS NULL;

            UPDATE dbo.PlaySessions
            SET Status = 'Closed', EndAtUtc = ISNULL(EndAtUtc, SYSUTCDATETIME()),
                ClosedById = ISNULL(ClosedById, {command.CashierId}),
                CustomerId = ISNULL(CustomerId, {command.CustomerId}), PlaytimeAmount = {prepared.PlaytimeAmount}
            WHERE Id = {prepared.SessionId};

            UPDATE t SET t.Status = 'Available'
            FROM dbo.BilliardTables t
            INNER JOIN dbo.PlaySessions s ON s.TableId = t.Id
            WHERE s.Id = {prepared.SessionId} AND t.Status = 'InUse';
            """, ct);

        var activeShiftId = receivedAtCounter
            ? await _db.WorkShifts.AsNoTracking()
                .Where(x => x.StaffId == command.CashierId && x.Status == WorkShift.StatusActive)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct)
            : null;

        _db.PaymentTransactions.Add(new PaymentTransaction
        {
            InvoiceId = prepared.InvoiceId,
            PaymentMethod = method,
            AmountPaid = calculated.Amount,
            GatewayRefCode = gatewayReference,
            WorkShiftId = activeShiftId,
            ReceivedById = receivedAtCounter ? command.CashierId : null,
            TransactionTimeUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        return new(true, false, false, "Thanh toán đã được ghi nhận thành công.", prepared.InvoiceId,
            calculated.Amount, 0, NormalizeReference(command.ReferenceCode));
    }

    private static string NormalizeReference(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0) normalized = $"HD-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        return normalized.Length <= 50 ? normalized : normalized[..50];
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private async Task<CheckoutTransactionScope> BeginCheckoutTransactionAsync(string prefix, CancellationToken ct)
    {
        var ambient = _db.Database.CurrentTransaction;
        if (ambient == null)
        {
            var owned = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            return new CheckoutTransactionScope(owned, ownsTransaction: true, savepoint: null);
        }

        var suffix = Guid.NewGuid().ToString("N")[..16];
        var savepoint = $"CO_{prefix}_{suffix}";
        await ambient.CreateSavepointAsync(savepoint, ct);
        return new CheckoutTransactionScope(ambient, ownsTransaction: false, savepoint);
    }

    private sealed class CheckoutAmountsRow
    {
        public int SessionId { get; set; }
        public decimal PlaytimeAmount { get; set; }
        public decimal FandBAmount { get; set; }
    }

    private sealed record PreparedInvoice(bool Success, int InvoiceId, int SessionId, decimal PlaytimeAmount, decimal FandBAmount, CheckoutPaymentResult? Result)
    {
        public static PreparedInvoice Fail(string message, string referenceCode, int invoiceId = 0) =>
            new(false, invoiceId, 0, 0, 0, new(false, false, false, message, invoiceId == 0 ? null : invoiceId, ReferenceCode: referenceCode));
    }

    private sealed record CalculatedAmount(bool Success, decimal Amount, CheckoutPaymentResult? Result)
    {
        public static CalculatedAmount Fail(string message, string referenceCode, int invoiceId) =>
            new(false, 0, new(false, false, false, message, invoiceId, ReferenceCode: referenceCode));
    }

    private sealed class CheckoutTransactionScope : IAsyncDisposable
    {
        private readonly IDbContextTransaction _transaction;
        private readonly bool _ownsTransaction;
        private readonly string? _savepoint;
        private bool _completed;

        public CheckoutTransactionScope(IDbContextTransaction transaction, bool ownsTransaction, string? savepoint)
        {
            _transaction = transaction;
            _ownsTransaction = ownsTransaction;
            _savepoint = savepoint;
        }

        public async Task CommitAsync(CancellationToken ct)
        {
            if (_completed) return;
            if (_ownsTransaction)
                await _transaction.CommitAsync(ct);
            else if (_savepoint != null)
                await _transaction.ReleaseSavepointAsync(_savepoint, ct);
            _completed = true;
        }

        public async Task RollbackAsync(CancellationToken ct)
        {
            if (_completed) return;
            if (_ownsTransaction)
                await _transaction.RollbackAsync(ct);
            else if (_savepoint != null)
                await _transaction.RollbackToSavepointAsync(_savepoint, ct);
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_ownsTransaction)
                await _transaction.DisposeAsync();
        }
    }
}
