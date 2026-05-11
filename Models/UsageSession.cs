using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tshow.Models;

[Table("UsageSessions")]
public class UsageSession
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string ProcessName { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public long DurationSeconds { get; set; }

    [Required]
    [MaxLength(10)]
    public string StartDate { get; set; } = string.Empty;
}
