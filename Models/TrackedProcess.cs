using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Tshow.Models;

[Table("TrackedProcesses")]
public class TrackedProcess
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string ProcessName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;
}
