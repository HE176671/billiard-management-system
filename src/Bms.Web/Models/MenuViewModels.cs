using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

// ── 1. Danh mục Sản phẩm (ProductCategories) ──

public class CategoryItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ProductCount { get; set; }
}

public class CreateCategoryViewModel
{
    [Required(ErrorMessage = "Tên danh mục không được để trống.")]
    [StringLength(60, MinimumLength = 1, ErrorMessage = "Tên danh mục từ 1 đến 60 ký tự.")]
    public string Name { get; set; } = string.Empty;
}

public class EditCategoryViewModel
{
    [Required]
    public int Id { get; set; }

    [Required(ErrorMessage = "Tên danh mục không được để trống.")]
    [StringLength(60, MinimumLength = 1, ErrorMessage = "Tên danh mục từ 1 đến 60 ký tự.")]
    public string Name { get; set; } = string.Empty;
}

// ── 2. Danh sách Sản phẩm & Kho hàng (Products) ──

public class ProductRowViewModel
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
    public string RowVersionBase64 { get; set; } = string.Empty;
}

public class MenuIndexViewModel
{
    public List<ProductRowViewModel> Products { get; set; } = new();
    public List<CategoryItemViewModel> Categories { get; set; } = new();

    // Bộ lọc
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public string? StatusFilter { get; set; }

    // Phân trang & Thống kê
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
    public int OutOfStockCount { get; set; }
}

// ── 3. Tạo mới & Cập nhật Sản phẩm ──

public class CreateProductViewModel
{
    [Required(ErrorMessage = "Vui lòng chọn danh mục sản phẩm.")]
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục hợp lệ.")]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "Tên sản phẩm không được để trống.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tên sản phẩm tối đa 100 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập đơn giá bán.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Giá sản phẩm phải lớn hơn 0 VNĐ.")]
    public decimal Price { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập số lượng tồn kho ban đầu.")]
    [Range(0, int.MaxValue, ErrorMessage = "Số lượng tồn kho không được âm.")]
    public int StockQuantity { get; set; } = 0;

    public List<CategoryItemViewModel> AvailableCategories { get; set; } = new();
}

public class EditProductViewModel
{
    [Required]
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn danh mục sản phẩm.")]
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục hợp lệ.")]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "Tên sản phẩm không được để trống.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tên sản phẩm tối đa 100 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập đơn giá bán.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Giá sản phẩm phải lớn hơn 0 VNĐ.")]
    public decimal Price { get; set; }

    public int CurrentStockQuantity { get; set; }
    public bool IsActive { get; set; } = true;

    [Required(ErrorMessage = "Thiếu mã kiểm soát phiên bản (RowVersion).")]
    public string RowVersionBase64 { get; set; } = string.Empty;

    public List<CategoryItemViewModel> AvailableCategories { get; set; } = new();
}
