using Bms.Web.Data;
using Bms.Web.Data.Entities;
using Bms.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bms.Web.Services;

public class InventoryService : IInventoryService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(ApplicationDbContext db, ILogger<InventoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<StockOperationResult> ChangeStockAsync(int productId, int quantityChange, string movementType,
        string? referenceType = null, int? referenceId = null, string? note = null, string? actorId = null)
    {
        if (quantityChange == 0)
        {
            var current = await _db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.StockQuantity).FirstOrDefaultAsync();
            return StockOperationResult.Ok(current, "Số lượng không đổi");
        }

        var isNewTx = _db.Database.CurrentTransaction == null;
        var tx = isNewTx ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var tracked = _db.ChangeTracker.Entries<Product>().FirstOrDefault(e => e.Entity.Id == productId);
            if (tracked != null)
            {
                await tracked.ReloadAsync();
            }

            var product = tracked?.Entity ?? await _db.Products
                .FromSqlInterpolated($"SELECT * FROM dbo.Products WITH (UPDLOCK, ROWLOCK) WHERE Id = {productId}")
                .FirstOrDefaultAsync();

            if (product == null)
            {
                if (tx != null) await tx.RollbackAsync();
                return StockOperationResult.Fail($"Không tìm thấy sản phẩm có mã #{productId}");
            }

            int newStock = product.StockQuantity + quantityChange;
            if (newStock < 0)
            {
                if (tx != null) await tx.RollbackAsync();
                return StockOperationResult.Fail($"Sản phẩm '{product.Name}' không đủ tồn kho (hiện còn {product.StockQuantity}, cần xuất {-quantityChange})");
            }

            product.StockQuantity = newStock;
            await _db.SaveChangesAsync();

            var movement = new StockMovement
            {
                ProductId = productId,
                QuantityChange = quantityChange,
                BalanceAfter = newStock,
                MovementType = movementType,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                Note = note,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedById = actorId
            };
            _db.StockMovements.Add(movement);
            await _db.SaveChangesAsync();

            if (tx != null) await tx.CommitAsync();

            _logger.LogInformation("Stock updated for Product {ProductId}: Change={Qty}, NewBalance={Balance}, Type={Type}",
                productId, quantityChange, newStock, movementType);

            return StockOperationResult.Ok(newStock, $"Cập nhật tồn kho thành công (Tồn mới: {newStock})");
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi cập nhật tồn kho sản phẩm #{ProductId}", productId);
            return StockOperationResult.Fail("Lỗi hệ thống khi cập nhật tồn kho: " + ex.Message);
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<StockOperationResult> AdjustStockAsync(int productId, int newQuantity, string reason, string actorId)
    {
        if (newQuantity < 0)
        {
            return StockOperationResult.Fail("Số lượng tồn kho sau điều chỉnh không được nhỏ hơn 0");
        }

        var isNewTx = _db.Database.CurrentTransaction == null;
        var tx = isNewTx ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            var tracked = _db.ChangeTracker.Entries<Product>().FirstOrDefault(e => e.Entity.Id == productId);
            if (tracked != null)
            {
                await tracked.ReloadAsync();
            }

            var product = tracked?.Entity ?? await _db.Products
                .FromSqlInterpolated($"SELECT * FROM dbo.Products WITH (UPDLOCK, ROWLOCK) WHERE Id = {productId}")
                .FirstOrDefaultAsync();

            if (product == null)
            {
                if (tx != null) await tx.RollbackAsync();
                return StockOperationResult.Fail($"Không tìm thấy sản phẩm #{productId}");
            }

            int diff = newQuantity - product.StockQuantity;
            product.StockQuantity = newQuantity;
            await _db.SaveChangesAsync();

            var movement = new StockMovement
            {
                ProductId = productId,
                QuantityChange = diff,
                BalanceAfter = newQuantity,
                MovementType = StockMovement.TypeAdjust,
                ReferenceType = "InventoryAudit",
                Note = $"Kiểm kê điều chỉnh kho: {reason}",
                CreatedAtUtc = DateTime.UtcNow,
                CreatedById = actorId
            };
            _db.StockMovements.Add(movement);
            await _db.SaveChangesAsync();

            if (tx != null) await tx.CommitAsync();
            return StockOperationResult.Ok(newQuantity, $"Đã điều chỉnh tồn kho thành {newQuantity}");
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            return StockOperationResult.Fail("Lỗi khi điều chỉnh tồn kho: " + ex.Message);
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<StockOperationResult> CreateReceiptAsync(StockReceiptInputModel model, string actorId)
    {
        if (model.Items == null || model.Items.Count == 0)
        {
            return StockOperationResult.Fail("Phiếu nhập kho phải có ít nhất 1 mặt hàng");
        }

        var normalizedCode = model.ReceiptCode.Trim().ToUpperInvariant();
        bool exists = await _db.StockReceipts.AnyAsync(r => r.ReceiptCode.ToUpper() == normalizedCode);
        if (exists)
        {
            return StockOperationResult.Fail($"Mã phiếu nhập '{model.ReceiptCode}' đã tồn tại trong hệ thống");
        }

        var isNewTx = _db.Database.CurrentTransaction == null;
        var tx = isNewTx ? await _db.Database.BeginTransactionAsync() : null;
        try
        {
            decimal totalAmount = model.Items.Sum(i => i.Quantity * i.CostPrice);

            var receipt = new StockReceipt
            {
                ReceiptCode = model.ReceiptCode.Trim(),
                SupplierName = model.SupplierName.Trim(),
                TotalAmount = totalAmount,
                Note = model.Note?.Trim(),
                ReceiptDateUtc = DateTime.UtcNow,
                CreatedById = actorId
            };
            _db.StockReceipts.Add(receipt);
            await _db.SaveChangesAsync();

            foreach (var item in model.Items)
            {
                if (item.Quantity <= 0)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return StockOperationResult.Fail($"Số lượng nhập cho món #{item.ProductId} phải lớn hơn 0");
                }
                if (item.CostPrice < 0)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return StockOperationResult.Fail($"Giá nhập cho món #{item.ProductId} không được âm");
                }

                var detail = new StockReceiptDetail
                {
                    ReceiptId = receipt.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    CostPrice = item.CostPrice
                };
                _db.StockReceiptDetails.Add(detail);

                // Cập nhật tồn kho qua thẻ kho
                var tracked = _db.ChangeTracker.Entries<Product>().FirstOrDefault(e => e.Entity.Id == item.ProductId);
                if (tracked != null)
                {
                    await tracked.ReloadAsync();
                }

                var product = tracked?.Entity ?? await _db.Products
                    .FromSqlInterpolated($"SELECT * FROM dbo.Products WITH (UPDLOCK, ROWLOCK) WHERE Id = {item.ProductId}")
                    .FirstOrDefaultAsync();

                if (product == null)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return StockOperationResult.Fail($"Không tìm thấy sản phẩm #{item.ProductId}");
                }

                product.StockQuantity += item.Quantity;

                var movement = new StockMovement
                {
                    ProductId = item.ProductId,
                    QuantityChange = item.Quantity,
                    BalanceAfter = product.StockQuantity,
                    MovementType = StockMovement.TypeImport,
                    ReferenceType = "StockReceipt",
                    ReferenceId = receipt.Id,
                    Note = $"Nhập kho theo phiếu {receipt.ReceiptCode} từ {receipt.SupplierName} (Giá nhập: {item.CostPrice:N0}đ)",
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedById = actorId
                };
                _db.StockMovements.Add(movement);
            }

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            return StockOperationResult.Ok(0, $"Đã lập phiếu nhập {receipt.ReceiptCode} thành công với {model.Items.Count} mặt hàng!");
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            _logger.LogError(ex, "Lỗi khi tạo phiếu nhập kho {Code}", model.ReceiptCode);
            return StockOperationResult.Fail("Lỗi hệ thống khi lưu phiếu nhập: " + ex.Message);
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<List<StockReceiptViewModel>> GetReceiptsAsync(int take = 50)
    {
        return await _db.StockReceipts.AsNoTracking()
            .Include(r => r.CreatedBy)
            .Include(r => r.Details)
            .OrderByDescending(r => r.ReceiptDateUtc)
            .Take(take)
            .Select(r => new StockReceiptViewModel
            {
                Id = r.Id,
                ReceiptCode = r.ReceiptCode,
                SupplierName = r.SupplierName,
                TotalAmount = r.TotalAmount,
                Note = r.Note,
                ReceiptDateUtc = r.ReceiptDateUtc,
                CreatedByName = r.CreatedBy != null ? r.CreatedBy.FullName : "Hệ thống",
                ItemCount = r.Details.Count
            })
            .ToListAsync();
    }

    public async Task<StockReceiptViewModel?> GetReceiptDetailAsync(int receiptId)
    {
        return await _db.StockReceipts.AsNoTracking()
            .Include(r => r.CreatedBy)
            .Include(r => r.Details)
                .ThenInclude(d => d.Product)
            .Where(r => r.Id == receiptId)
            .Select(r => new StockReceiptViewModel
            {
                Id = r.Id,
                ReceiptCode = r.ReceiptCode,
                SupplierName = r.SupplierName,
                TotalAmount = r.TotalAmount,
                Note = r.Note,
                ReceiptDateUtc = r.ReceiptDateUtc,
                CreatedByName = r.CreatedBy != null ? r.CreatedBy.FullName : "Hệ thống",
                ItemCount = r.Details.Count,
                Details = r.Details.Select(d => new StockReceiptDetailViewModel
                {
                    ProductId = d.ProductId,
                    ProductName = d.Product != null ? d.Product.Name : "Món #" + d.ProductId,
                    Quantity = d.Quantity,
                    CostPrice = d.CostPrice
                }).ToList()
            })
            .FirstOrDefaultAsync();
    }

    public async Task<List<StockMovementViewModel>> GetStockMovementsAsync(int? productId = null, int take = 100)
    {
        var q = _db.StockMovements.AsNoTracking()
            .Include(m => m.Product)
            .Include(m => m.CreatedBy)
            .AsQueryable();

        if (productId.HasValue && productId.Value > 0)
        {
            q = q.Where(m => m.ProductId == productId.Value);
        }

        return await q.OrderByDescending(m => m.CreatedAtUtc)
            .ThenByDescending(m => m.Id)
            .Take(take)
            .Select(m => new StockMovementViewModel
            {
                Id = m.Id,
                ProductId = m.ProductId,
                ProductName = m.Product != null ? m.Product.Name : "Món #" + m.ProductId,
                QuantityChange = m.QuantityChange,
                BalanceAfter = m.BalanceAfter,
                MovementType = m.MovementType,
                ReferenceType = m.ReferenceType,
                ReferenceId = m.ReferenceId,
                Note = m.Note,
                CreatedAtUtc = m.CreatedAtUtc,
                CreatedByName = m.CreatedBy != null ? m.CreatedBy.FullName : "Hệ thống"
            })
            .ToListAsync();
    }

    public async Task<List<ProductViewModel>> GetInventoryListAsync(string? search = null, int? categoryId = null)
    {
        var q = _db.Products.AsNoTracking()
            .Include(p => p.Category)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(term));
        }

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            q = q.Where(p => p.CategoryId == categoryId.Value);
        }

        return await q.OrderBy(p => p.CategoryId)
            .ThenBy(p => p.Name)
            .Select(p => new ProductViewModel
            {
                Id = p.Id,
                CategoryId = p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : "Chưa phân loại",
                Name = p.Name,
                Price = p.Price,
                StockQuantity = p.StockQuantity,
                IsActive = p.IsActive
            })
            .ToListAsync();
    }
}
