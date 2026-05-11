using System.Diagnostics;
using System.Timers;
using Microsoft.EntityFrameworkCore;
using Tshow.Data;
using Tshow.Models;
using Tshow.Native;
using Timer = System.Timers.Timer;

namespace Tshow.Services;

public class ProcessMonitorService : IDisposable
{
    private readonly Timer _timer;
    private readonly Dictionary<string, int> _openSessions = new();
    private bool _isRunning;

    public event Action<List<TaskbarProcess>>? OnProcessesUpdated;
    public event Action<UsageSession>? OnSessionStarted;
    public event Action<UsageSession>? OnSessionEnded;
    public event Action<string>? OnError;

    public bool IsRunning => _isRunning;

    public ProcessMonitorService()
    {
        _timer = new Timer(10000);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
    }

    public void Start()
    {
        if (_isRunning) return;

        CleanDanglingSessions();

        _isRunning = true;
        _timer.Start();

        var names = GetDistinctTrackedProcessNames();
        foreach (var name in names)
        {
            EnsureSession(name);
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _timer.Stop();

        CloseAllOpenSessions();
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!_isRunning) return;

        try
        {
            var names = GetDistinctTrackedProcessNames();

            foreach (var name in names)
            {
                EnsureSession(name);
            }

            var removedNames = _openSessions.Keys.Except(names).ToList();
            foreach (var name in removedNames)
            {
                CloseSession(name);
            }

            var processes = Win32Interop.EnumerateTaskbarProcesses();
            OnProcessesUpdated?.Invoke(processes);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"监测错误: {ex.Message}");
        }
    }

    public void StartProcessByName(string procName)
    {
        if (!ShouldTrack(procName)) return;
        EnsureSession(procName);
    }

    private void EnsureSession(string procName)
    {
        if (_openSessions.ContainsKey(procName)) return;

        var session = new UsageSession
        {
            ProcessName = procName,
            StartTime = DateTime.Now,
            StartDate = DateTime.Now.ToString("yyyy-MM-dd")
        };

        try
        {
            using var db = new AppDbContext();
            db.UsageSessions.Add(session);
            db.SaveChanges();

            _openSessions[procName] = session.Id;
            OnSessionStarted?.Invoke(session);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"记录启动失败({procName}): {ex.Message}");
        }
    }

    private void CloseSession(string procName)
    {
        if (!_openSessions.TryGetValue(procName, out var sessionId)) return;

        try
        {
            using var db = new AppDbContext();
            var session = db.UsageSessions.Find(sessionId);
            if (session != null)
            {
                session.EndTime = DateTime.Now;
                session.DurationSeconds = (long)(session.EndTime.Value - session.StartTime).TotalSeconds;
                db.SaveChanges();
                OnSessionEnded?.Invoke(session);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"记录结束失败({procName}): {ex.Message}");
        }

        _openSessions.Remove(procName);
    }

    private void CloseAllOpenSessions()
    {
        foreach (var name in _openSessions.Keys.ToList())
        {
            CloseSession(name);
        }
    }

    private void CleanDanglingSessions()
    {
        try
        {
            using var db = new AppDbContext();
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var dangling = db.UsageSessions
                .Where(s => s.StartDate == today && s.EndTime == null)
                .ToList();

            foreach (var s in dangling)
            {
                s.EndTime = DateTime.Now;
                s.DurationSeconds = (long)(s.EndTime.Value - s.StartTime).TotalSeconds;
            }

            if (dangling.Count > 0)
                db.SaveChanges();
        }
        catch { }
    }

    private bool ShouldTrack(string procName)
    {
        try
        {
            using var db = new AppDbContext();
            return db.TrackedProcesses.Any(p => p.ProcessName == procName && p.IsEnabled);
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<string> GetDistinctTrackedProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Win32Interop.EnumWindows((hWnd, lParam) =>
        {
            if (!Win32Interop.IsTaskbarWindow(hWnd)) return true;

            Win32Interop.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return true;

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                names.Add(proc.ProcessName);
            }
            catch { }

            return true;
        }, IntPtr.Zero);

        return names;
    }

    public List<UsageSession> GetTodaySessions()
    {
        try
        {
            using var db = new AppDbContext();
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            return db.UsageSessions
                .Where(s => s.StartDate == today)
                .OrderByDescending(s => s.StartTime)
                .ToList();
        }
        catch
        {
            return new List<UsageSession>();
        }
    }

    public TimeSpan GetTodayTotalUsage(string? processName = null)
    {
        try
        {
            using var db = new AppDbContext();
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var query = db.UsageSessions.Where(s => s.StartDate == today);

            if (!string.IsNullOrEmpty(processName))
            {
                query = query.Where(s => s.ProcessName == processName);
            }

            var sessions = query.ToList();
            var groups = sessions.GroupBy(s => s.ProcessName, StringComparer.OrdinalIgnoreCase);
            var total = 0L;

            foreach (var group in groups)
            {
                foreach (var s in group)
                {
                    if (s.DurationSeconds > 0)
                    {
                        total += s.DurationSeconds;
                    }
                    else if (s.EndTime == null)
                    {
                        total += (long)(DateTime.Now - s.StartTime).TotalSeconds;
                    }
                }
            }

            return TimeSpan.FromSeconds(total);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
