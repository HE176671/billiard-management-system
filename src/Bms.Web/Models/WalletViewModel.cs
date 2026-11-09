using Bms.Web.Data;

namespace Bms.Web.Models;

public class WalletViewModel
{
    public bool TransferEnabled { get; set; }
    public bool AutomaticConfirmationConfigured { get; set; }
    public string QrBaseUrl { get; set; } = "";
    public string BankCode { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string PaymentMethod { get; set; } = "Cash";
    public WalletTopUpIntent? SelectedTransfer { get; set; }
    public List<WalletTopUpIntent> RecentTransfers { get; set; } = new();
    public string CustomerId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public decimal Balance { get; set; }
    public string TierName { get; set; } = "";
    public int RewardPoints { get; set; }
    public int PointMultiplier { get; set; } = 1;
    public bool CanTopUp { get; set; }
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public List<WalletTransaction> Transactions { get; set; } = new();
    public Dictionary<string, string> ActorNames { get; set; } = new();
    public int Page { get; set; } = 1;
    public bool HasNextPage { get; set; }
}
