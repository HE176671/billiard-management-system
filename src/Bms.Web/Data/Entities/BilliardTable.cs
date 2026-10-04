using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bms.Web.Data.Entities;

[Table("BilliardTables")]
public class BilliardTable
{
    public int Id { get; set; }

    [Required]
    [MaxLength(20)]
    public string TableCode { get; set; } = string.Empty;

    public int TableTypeId { get; set; }

    [ForeignKey(nameof(TableTypeId))]
    [InverseProperty(nameof(Entities.TableType.BilliardTables))]
    public TableType TableType { get; set; } = null!;

    public int FloorNumber { get; set; } = 1;

    [Required]
    [MaxLength(20)]
    [Column(TypeName = "varchar(20)")]
    public string Status { get; set; } = "Available";

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    [InverseProperty(nameof(PlaySession.BilliardTable))]
    public ICollection<PlaySession> PlaySessions { get; set; } = [];
}
