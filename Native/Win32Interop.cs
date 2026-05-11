using System.Runtime.InteropServices;
using System.Text;

namespace Tshow.Native;

internal static class Win32Interop
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public const int GWL_EXSTYLE = -20;
    public const uint GW_OWNER = 4;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int LeftWidth;
        public int RightWidth;
        public int TopHeight;
        public int BottomHeight;
    }

    public static bool IsTaskbarWindow(IntPtr hWnd)
    {
        if (!IsWindowVisible(hWnd))
            return false;

        var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
            return false;

        var owner = GetWindow(hWnd, GW_OWNER);
        if (owner != IntPtr.Zero)
            return false;

        var length = GetWindowTextLength(hWnd);
        if (length == 0)
            return false;

        var hasAppWindow = (exStyle & WS_EX_APPWINDOW) != 0;
        if (hasAppWindow)
            return true;

        return owner == IntPtr.Zero;
    }

    public static string GetWindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;

        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static List<TaskbarProcess> EnumerateTaskbarProcesses()
    {
        var processes = new Dictionary<uint, TaskbarProcess>();

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsTaskbarWindow(hWnd)) return true;

            GetWindowThreadProcessId(hWnd, out uint processId);
            if (processId == 0) return true;

            if (!processes.ContainsKey(processId))
            {
                try
                {
                    var proc = System.Diagnostics.Process.GetProcessById((int)processId);
                    processes[processId] = new TaskbarProcess
                    {
                        ProcessId = processId,
                        ProcessName = proc.ProcessName,
                        WindowTitle = GetWindowTitle(hWnd)
                    };
                }
                catch
                {
                }
            }

            return true;
        }, IntPtr.Zero);

        return processes.Values.OrderBy(p => p.ProcessName).ToList();
    }
}

public class TaskbarProcess
{
    public uint ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
}
