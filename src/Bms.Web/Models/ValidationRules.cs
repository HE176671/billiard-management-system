using System.ComponentModel.DataAnnotations;
using Bms.Web.Services;

namespace Bms.Web.Models;

/// <summary>
/// Quy tắc nhập liệu dùng chung cho các form tài khoản.
/// Regex viết theo cú pháp chạy được cả trên server (.NET) lẫn client (jQuery Validation).
/// </summary>
public static class ValidationRules
{
    /// <summary>Chữ cái (kể cả tiếng Việt có dấu), khoảng trắng, dấu chấm, nháy đơn, gạch ngang.</summary>
    public const string FullName = @"^[A-Za-zÀ-ỹ][A-Za-zÀ-ỹ .'-]*$";
    public const string FullNameMessage = "Họ tên chỉ gồm chữ cái và khoảng trắng.";

    /// <summary>Bắt đầu bằng chữ; chỉ chữ không dấu, số, dấu chấm, gạch dưới, gạch ngang.</summary>
    public const string UserName = @"^[A-Za-z][A-Za-z0-9._-]*$";
    public const string UserNameMessage = "Tên đăng nhập bắt đầu bằng chữ, chỉ gồm chữ không dấu, số, dấu . _ -";

    /// <summary>Số di động Việt Nam: 0xxxxxxxxx hoặc +84xxxxxxxxx, đầu số 3/5/7/8/9.</summary>
    public const string VnPhone = @"^(0|\+84)(3|5|7|8|9)[0-9]{8}$";
    public const string VnPhoneMessage = "Số điện thoại không hợp lệ (vd: 0901234567).";

    /// <summary>Khớp cấu hình Identity: ≥ 8 ký tự, có chữ thường, chữ hoa, số và ký tự đặc biệt.</summary>
    public const string Password = @"^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^A-Za-z0-9]).{8,100}$";
    public const string PasswordMessage = "Mật khẩu ≥ 8 ký tự, có chữ thường, chữ hoa, số và ký tự đặc biệt.";

    /// <summary>Email phải có tên miền dạng abc.xyz.</summary>
    public const string Email = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
    public const string EmailMessage = "Email không hợp lệ.";

    public static readonly DateOnly MinHireDate = new(2000, 1, 1);

    /// <summary>Gộp khoảng trắng thừa: "  Nguyễn   Văn  A " → "Nguyễn Văn A".</summary>
    public static string CollapseSpaces(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>Ngày vào làm từ 01/01/2000 đến hôm nay (giờ Việt Nam).</summary>
public class HireDateAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not DateOnly date) return ValidationResult.Success;
        if (date < ValidationRules.MinHireDate)
            return new ValidationResult("Ngày vào làm không được trước 01/01/2000.");
        if (date > VnTime.Today)
            return new ValidationResult("Ngày vào làm không được ở tương lai.");
        return ValidationResult.Success;
    }
}
