using Microsoft.AspNetCore.Identity;

namespace Bms.Web.Data;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? EmployeeCode { get; set; }
    public DateOnly? HireDate { get; set; }
}
