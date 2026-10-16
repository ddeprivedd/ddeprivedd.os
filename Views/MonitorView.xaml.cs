using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualBasic.Devices;

namespace ddeprivedd.os.Views
{
    public partial class MonitorView : UserControl
    {
        private const int MaxPoints = 60;

        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly ComputerInfo info = new ComputerInfo();
        private PerformanceCounter cpuCounter;

        private readonly Queue<double> cpuHistory = new Queue<double>();
        private readonly Queue<double> ramHistory = new Queue<double>();
        private readonly Queue<double> procHistory = new Queue<double>();

        public MonitorView()
        {
            InitializeComponent();

            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue();   // the first reading is always 0
            }
            catch
            {
                cpuCounter = null;        // counters unavailable: CPU stays at 0
            }

            timer.Tick += (s, e) => Tick();

            // Only poll while this page is actually on screen.
            Loaded += (s, e) =>
            {
                Anim.Stagger(PageRoot);
                Tick();
                timer.Start();
            };
            Unloaded += (s, e) => timer.Stop();
        }

        private void Tick()
        {
            // CPU
            double cpu = 0;
            try { if (cpuCounter != null) cpu = Math.Max(0, Math.Min(100, cpuCounter.NextValue())); }
            catch { }
            CpuText.Text = ((int)cpu) + "%";

            // RAM
            double usedGb = 0, totalGb = 0, ramPercent = 0;
            try
            {
                ulong total = info.TotalPhysicalMemory;
                ulong available = info.AvailablePhysicalMemory;
                ulong used = total > available ? total - available : 0;
                usedGb = used / 1073741824.0;
                totalGb = total / 1073741824.0;
                ramPercent = total > 0 ? used * 100.0 / total : 0;
            }
            catch { }
            RamText.Text = usedGb.ToString("0.0") + " GB";
            MemDetail.Text = usedGb.ToString("0.0") + " of " + totalGb.ToString("0.0") + " GB  (" + ((int)ramPercent) + "%)";
            Anim.SetProgress(MemBar, ramPercent);

            // Processes
            int processCount = 0;
            try
            {
                Process[] all = Process.GetProcesses();
                processCount = all.Length;
                foreach (Process p in all) p.Dispose();
            }
            catch { }
            ProcessText.Text = processCount.ToString();

            // Graphs
            Push(cpuHistory, cpu);
            Push(ramHistory, ramPercent);
            Push(procHistory, processCount);

            DrawGraph(CpuCanvas, CpuLine, CpuArea, cpuHistory, 0, 100);
            DrawGraph(RamCanvas, RamLine, RamArea, ramHistory, 0, 100);
            DrawGraph(ProcCanvas, ProcLine, ProcArea, procHistory,
                      Math.Max(0, procHistory.Min() - 10), procHistory.Max() + 10);
        }

        private static void Push(Queue<double> history, double value)
        {
            history.Enqueue(value);
            while (history.Count > MaxPoints) history.Dequeue();
        }

        private static void DrawGraph(Canvas canvas, Polyline line, Polygon area,
                                      Queue<double> data, double low, double high)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 0 || height <= 0 || high <= low) return;

            double step = width / (MaxPoints - 1);
            int offset = MaxPoints - data.Count;

            var points = new PointCollection();
            int i = 0;
            foreach (double value in data)
            {
                double x = (offset + i) * step;
                double y = height - ((value - low) / (high - low)) * (height - 4) - 2;
                points.Add(new Point(x, y));
                i++;
            }
            line.Points = points;

            var fill = new PointCollection(points);
            fill.Add(new Point(width, height));
            fill.Add(new Point(offset * step, height));
            area.Points = fill;
        }
    }
}
