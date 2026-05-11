using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Tshow.Native;
using Tshow.ViewModels;

namespace Tshow;

public partial class MainWindow : Window
{
    private static readonly Color[] ChartColors =
    {
        Color.FromRgb(0x00, 0x78, 0xD4),
        Color.FromRgb(0x6B, 0xCB, 0x77),
        Color.FromRgb(0xFF, 0xD9, 0x3D),
        Color.FromRgb(0xFF, 0x6B, 0x6B),
        Color.FromRgb(0xA7, 0x7B, 0xE8),
        Color.FromRgb(0x4E, 0xC9, 0xB0),
        Color.FromRgb(0xF7, 0x8C, 0x6C),
        Color.FromRgb(0x82, 0xAA, 0xFF),
        Color.FromRgb(0xC3, 0xE8, 0x8D),
        Color.FromRgb(0xFF, 0x53, 0x7E),
        Color.FromRgb(0x89, 0xDD, 0xFF),
        Color.FromRgb(0xE0, 0x40, 0xFB),
    };

    private const double LabelWidth = 70;
    private const double RowHeight = 36;
    private const double AxisHeight = 24;

    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.TimelineProcesses))
            {
                Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(DrawTimeline));
            }
        };

        Loaded += OnWindowLoaded;
        TimelineCanvas.SizeChanged += (s, e) => DrawTimeline();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.LoadDates();
        UpdateTabStyles();
    }

    private void TimelineTab_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.SetPageWithoutAnimation(0);
        MonitorPage.Visibility = Visibility.Collapsed;
        TimelinePage.Visibility = Visibility.Visible;
        UpdateTabStyles();
        vm.LoadDates();
    }

    private void MonitorTab_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        vm.SetPageWithoutAnimation(1);
        TimelinePage.Visibility = Visibility.Collapsed;
        MonitorPage.Visibility = Visibility.Visible;
        UpdateTabStyles();
    }

    private void UpdateTabStyles()
    {
        var vm = (MainViewModel)DataContext;
        var activeBrush = (Brush)FindResource("AccentLightBrush");
        var inactiveBrush = (Brush)FindResource("TextSecondaryBrush");
        var activeBg = (Brush)FindResource("SurfaceLightBrush");

        TimelineTab.Foreground = vm.CurrentPage == 0 ? activeBrush : inactiveBrush;
        TimelineTab.Background = vm.CurrentPage == 0 ? activeBg : Brushes.Transparent;
        MonitorTab.Foreground = vm.CurrentPage == 1 ? activeBrush : inactiveBrush;
        MonitorTab.Background = vm.CurrentPage == 1 ? activeBg : Brushes.Transparent;
    }

    private void DrawTimeline()
    {
        TimelineCanvas.Children.Clear();
        var vm = (MainViewModel)DataContext;

        if (vm.TimelineProcesses.Count == 0) return;

        var canvasWidth = TimelineCanvas.ActualWidth;
        if (canvasWidth <= LabelWidth + 10) return;

        var chartLeft = LabelWidth + 8;
        var chartWidth = canvasWidth - chartLeft;

        var dayStart = DateTime.Today;
        var dayEnd = dayStart.AddDays(1);
        var totalTicks = (dayEnd - dayStart).Ticks;

        var textBrush = (Brush)FindResource("TextSecondaryBrush");
        var bgBrush = (Brush)FindResource("SurfaceLightBrush");

        DrawTimeAxisLabels(textBrush, chartLeft, chartWidth);

        double y = AxisHeight + 12;

        for (int i = 0; i < vm.TimelineProcesses.Count; i++)
        {
            var proc = vm.TimelineProcesses[i];
            var color = ChartColors[i % ChartColors.Length];

            if (i % 2 == 0)
            {
                var bg = new Rectangle
                {
                    Width = canvasWidth,
                    Height = RowHeight,
                    Fill = bgBrush,
                    Opacity = 0.3
                };
                Canvas.SetTop(bg, y);
                TimelineCanvas.Children.Add(bg);
            }

            var nameLabel = new System.Windows.Controls.TextBlock
            {
                Text = proc.ProcessName,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                FontSize = 11,
                Width = LabelWidth,
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
                ToolTip = proc.ProcessName
            };
            Canvas.SetLeft(nameLabel, 4);
            Canvas.SetTop(nameLabel, y + 10);
            TimelineCanvas.Children.Add(nameLabel);

            foreach (var seg in proc.Segments)
            {
                var left = chartLeft + seg.LeftRatio * chartWidth;
                var width = Math.Max(seg.WidthRatio * chartWidth, 2);

                var bar = new Border
                {
                    Width = width,
                    Height = 20,
                    Background = new SolidColorBrush(color),
                    CornerRadius = new CornerRadius(3),
                    Opacity = 0.85
                };
                Canvas.SetLeft(bar, left);
                Canvas.SetTop(bar, y + 8);
                TimelineCanvas.Children.Add(bar);
            }

            y += RowHeight;
        }

        TimelineCanvas.MinHeight = y + 12;
    }

    private void DrawTimeAxisLabels(Brush labelBrush, double left, double width)
    {
        for (int h = 0; h <= 24; h += 2)
        {
            var x = left + width * h / 24.0;
            var label = new System.Windows.Controls.TextBlock
            {
                Text = $"{h:D2}:00",
                Foreground = labelBrush,
                FontSize = 9
            };
            Canvas.SetLeft(label, x - 12);
            Canvas.SetTop(label, 2);
            TimelineCanvas.Children.Add(label);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var hwndSource = HwndSource.FromHwnd(hwnd);

        if (hwndSource != null)
        {
            hwndSource.AddHook(WndProc);

            var margins = new Win32Interop.MARGINS
            {
                LeftWidth = -1,
                RightWidth = -1,
                TopHeight = -1,
                BottomHeight = -1
            };
            Win32Interop.DwmExtendFrameIntoClientArea(hwnd, ref margins);

            var useDarkMode = 1;
            Win32Interop.DwmSetWindowAttribute(
                hwnd,
                Win32Interop.DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref useDarkMode,
                Marshal.SizeOf<int>());
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO && IsLoaded)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.ptMaxSize.y = (int)SystemParameters.PrimaryScreenHeight + 12;
            mmi.ptMaxTrackSize.y = (int)SystemParameters.PrimaryScreenHeight;
            Marshal.StructureToPtr(mmi, lParam, true);
        }
        return IntPtr.Zero;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }
}
