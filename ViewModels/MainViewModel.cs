using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tshow.Data;
using Tshow.Models;
using Tshow.Native;
using Tshow.Services;

namespace Tshow.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ProcessMonitorService _monitor;
    private readonly DispatcherTimer _refreshTimer;

    private int _currentPage;
    public int CurrentPage => _currentPage;

    public void SetPageWithoutAnimation(int page)
    {
        if (_currentPage == page) return;
        _currentPage = page;
        OnPropertyChanged(nameof(CurrentPage));
    }

    [ObservableProperty]
    private bool _isStartupEnabled;

    [ObservableProperty]
    private string _statusText = "监测运行中...";

    [ObservableProperty]
    private ObservableCollection<TrackedProcessItem> _trackedProcesses = new();

    [ObservableProperty]
    private ObservableCollection<CurrentProcessItem> _currentProcesses = new();

    [ObservableProperty]
    private ObservableCollection<TimelineProcessItem> _timelineProcesses = new();

    [ObservableProperty]
    private ObservableCollection<DateTime> _timelineDates = new();

    [ObservableProperty]
    private DateTime _selectedTimelineDate;

    public MainViewModel(ProcessMonitorService monitor)
    {
        _monitor = monitor;
        _monitor.OnProcessesUpdated += OnProcessesUpdated;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += (s, e) => RefreshProcessUsage();
        _refreshTimer.Start();

        IsStartupEnabled = StartupService.IsStartupEnabled();
        LoadTrackedProcesses();
        _monitor.Start();
    }

    partial void OnSelectedTimelineDateChanged(DateTime value)
    {
        LoadTimeline(value);
    }

    private void OnProcessesUpdated(List<TaskbarProcess> processes)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            CurrentProcesses.Clear();
            foreach (var p in processes)
            {
                var isTracked = TrackedProcesses.Any(t =>
                    string.Equals(t.ProcessName, p.ProcessName, StringComparison.OrdinalIgnoreCase));

                CurrentProcesses.Add(new CurrentProcessItem
                {
                    ProcessName = p.ProcessName,
                    WindowTitle = p.WindowTitle,
                    IsTracked = isTracked
                });
            }
        });
    }

    [RelayCommand]
    private void ToggleStartup()
    {
        if (IsStartupEnabled)
        {
            StartupService.DisableStartup();
            IsStartupEnabled = false;
        }
        else
        {
            StartupService.EnableStartup();
            IsStartupEnabled = true;
        }
    }

    [RelayCommand]
    private void AddProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;

        processName = processName.Trim();

        if (TrackedProcesses.Any(p =>
            string.Equals(p.ProcessName, processName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var tracked = new TrackedProcess
            {
                ProcessName = processName,
                IsEnabled = true
            };
            db.TrackedProcesses.Add(tracked);
            db.SaveChanges();

            LoadTrackedProcesses();
            _monitor.StartProcessByName(processName);
            LoadTimeline(SelectedTimelineDate);
        }
        catch (Exception ex)
        {
            StatusText = $"添加失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;

        try
        {
            using var db = new AppDbContext();
            var existing = db.TrackedProcesses
                .FirstOrDefault(p => p.ProcessName == processName);
            if (existing != null)
            {
                db.TrackedProcesses.Remove(existing);
                db.SaveChanges();
            }

            LoadTrackedProcesses();
            LoadTimeline(SelectedTimelineDate);
        }
        catch (Exception ex)
        {
            StatusText = $"移除失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RefreshCurrentProcesses()
    {
        var processes = Win32Interop.EnumerateTaskbarProcesses();
        CurrentProcesses.Clear();
        foreach (var p in processes)
        {
            var isTracked = TrackedProcesses.Any(t =>
                string.Equals(t.ProcessName, p.ProcessName, StringComparison.OrdinalIgnoreCase));

            CurrentProcesses.Add(new CurrentProcessItem
            {
                ProcessName = p.ProcessName,
                WindowTitle = p.WindowTitle,
                IsTracked = isTracked
            });
        }

        RefreshProcessUsage();
    }

    private void RefreshProcessUsage()
    {
        foreach (var item in TrackedProcesses)
        {
            var usage = _monitor.GetTodayTotalUsage(item.ProcessName);
            item.TodayUsage = $"{(int)usage.TotalHours:D2}:{usage.Minutes:D2}:{usage.Seconds:D2}";
        }
    }

    public void LoadDates()
    {
        try
        {
            using var db = new AppDbContext();
            var dates = db.GetAvailableDates();
            TimelineDates = new ObservableCollection<DateTime>(dates);
            if (dates.Count > 0)
                SelectedTimelineDate = dates[0];
        }
        catch { }
    }

    public void LoadTimeline(DateTime date)
    {
        try
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);
            var totalTicks = (dayEnd - dayStart).Ticks;

            using var db = new AppDbContext();

            var trackedNames = db.TrackedProcesses
                .Where(p => p.IsEnabled)
                .Select(p => p.ProcessName)
                .ToList();

            var allSessions = db.UsageSessions
                .Where(s => s.StartDate == dateStr && trackedNames.Contains(s.ProcessName))
                .OrderBy(s => s.StartTime)
                .ToList();

            var timeline = new ObservableCollection<TimelineProcessItem>();

            foreach (var name in trackedNames)
            {
                var sessions = allSessions
                    .Where(s => string.Equals(s.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var segments = new ObservableCollection<TimelineSegment>();

                foreach (var s in sessions)
                {
                    var start = s.StartTime;
                    var end = s.EndTime ?? DateTime.Now;

                    if (end < dayStart || start > dayEnd) continue;
                    if (start < dayStart) start = dayStart;
                    if (end > dayEnd) end = dayEnd;

                    var leftRatio = (double)(start - dayStart).Ticks / totalTicks;
                    var widthRatio = (double)(end - start).Ticks / totalTicks;

                    if (widthRatio < 0.001) continue;

                    segments.Add(new TimelineSegment
                    {
                        LeftRatio = leftRatio,
                        WidthRatio = widthRatio
                    });
                }

                if (segments.Count > 0)
                {
                    timeline.Add(new TimelineProcessItem
                    {
                        ProcessName = name,
                        Segments = segments
                    });
                }
            }

            TimelineProcesses = timeline;
        }
        catch (Exception ex)
        {
            StatusText = $"加载时间线失败: {ex.Message}";
        }
    }

    private void LoadTrackedProcesses()
    {
        try
        {
            using var db = new AppDbContext();
            db.Database.EnsureCreated();

            var processes = db.TrackedProcesses.OrderBy(p => p.ProcessName).ToList();
            TrackedProcesses.Clear();

            foreach (var p in processes)
            {
                var usage = _monitor.GetTodayTotalUsage(p.ProcessName);
                TrackedProcesses.Add(new TrackedProcessItem
                {
                    Id = p.Id,
                    ProcessName = p.ProcessName,
                    TodayUsage = $"{(int)usage.TotalHours:D2}:{usage.Minutes:D2}:{usage.Seconds:D2}"
                });
            }
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
    }
}

public partial class TrackedProcessItem : ObservableObject
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;

    [ObservableProperty]
    private string _todayUsage = "00:00:00";
}

public partial class CurrentProcessItem : ObservableObject
{
    private bool _isTracked;

    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;

    public bool IsTracked
    {
        get => _isTracked;
        set
        {
            _isTracked = value;
            OnPropertyChanged(nameof(IsTracked));
            OnPropertyChanged(nameof(AddVisibility));
            OnPropertyChanged(nameof(TrackedVisibility));
        }
    }

    public Visibility AddVisibility => IsTracked ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TrackedVisibility => IsTracked ? Visibility.Visible : Visibility.Collapsed;
}

public class TimelineProcessItem
{
    public string ProcessName { get; set; } = string.Empty;
    public ObservableCollection<TimelineSegment> Segments { get; set; } = new();
}

public class TimelineSegment
{
    public double LeftRatio { get; set; }
    public double WidthRatio { get; set; }
}
