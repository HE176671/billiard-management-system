using System.ComponentModel.DataAnnotations;

namespace Bms.Web.Models;

public class CustomerListViewModel
{
    public List<CustomerRowViewModel> Customers  { get; set; } = new();
    public string?                    Search      { get; set; }
    public string?                    StatusFilter{ get; set; }
    public int?                       TierFilter  { get; set; }
    public int                        Page        { get; set; } = 1;
    public int                        TotalPages  { get; set; }
    public int                        TotalCount  { get; set; }
    public List<TierOptionViewModel>  TierOptions { get; set; } = new();
}

public class CustomerRowViewModel
{
    public string   Id           { get; set; } = "";
    public string   FullName     { get; set; } = "";
    public string?  UserName     { get; set; }
    public string?  Email        { get; set; }
    public string?  PhoneNumber  { get; set; }
    public bool     IsActive     { get; set; }
    public int      RewardPoints { get; set; }
    public string?  TierName     { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class TierOptionViewModel
{
    public int    Id       { get; set; }
    public string TierName { get; set; } = "";
}
