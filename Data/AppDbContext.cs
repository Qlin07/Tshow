using System.IO;
using Microsoft.EntityFrameworkCore;
using Tshow.Models;

namespace Tshow.Data;

public class AppDbContext : DbContext
{
    public DbSet<TrackedProcess> TrackedProcesses => Set<TrackedProcess>();
    public DbSet<UsageSession> UsageSessions => Set<UsageSession>();

    public string DbPath { get; set; } = string.Empty;

    public AppDbContext()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tshow");
        Directory.CreateDirectory(folder);
        DbPath = Path.Combine(folder, "data.db");
    }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public List<DateTime> GetAvailableDates()
    {
        var dates = UsageSessions
            .Select(s => s.StartDate)
            .Distinct()
            .OrderByDescending(d => d)
            .AsEnumerable()
            .Select(d => DateTime.ParseExact(d, "yyyy-MM-dd", null))
            .ToList();

        if (dates.Count == 0)
            dates.Add(DateTime.Today);

        return dates;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            options.UseSqlite($"Data Source={DbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackedProcess>()
            .HasIndex(p => p.ProcessName)
            .IsUnique();

        modelBuilder.Entity<UsageSession>()
            .HasIndex(s => new { s.ProcessName, s.StartDate });
    }
}
