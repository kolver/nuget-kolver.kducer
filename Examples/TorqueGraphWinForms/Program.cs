/*
 * This example uses the Kolver Kducer library to fetch torque data and display it in a line graph.
 * It uses WinForm (only compatible with Windows) to create the GUI.
 * The graph updates in real-time as new data is received from the KDU device, if the KDU version is v41 or higher. Otherwise, the graph is shown after a tightening is completed.
 * Do not be intimidated by the size of this program, as most of the code in this example is related to the GUI and the graph rendering (LineGraphForm and PlotPanel).
 * Scroll to the end (Program class) to see how the Kducer library is used in a few simple lines of code.
 * Modify the variable KDU1A_IP_ADDRESS to the IP address of your KDU device to run this example.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Kolver;

public partial class LineGraphForm : Form
{
    private List<PointF> dataPoints = new List<PointF>();
    private string xLabel = "X";
    private string yLabel = "Y";
    private Color lineColor = Color.Blue;
    private int lineWidth = 2;
    private Button clearButton;
    private PlotPanel plotPanel;

    // Event for when clear button is clicked
    public event EventHandler ClearRequested;

    public LineGraphForm(string kduIp)
    {
        InitializeComponent(kduIp);
    }

    private void InitializeComponent(string kduIp)
    {
        Text = "K-DUCER Torque-vs-Angle Graphing";
        Size = new Size(800, 650);
        BackColor = Color.White;

        clearButton = new Button();
        clearButton.Text = "Clear Plot";
        clearButton.Size = new Size(100, 30);
        clearButton.Location = new Point(10, 10);
        clearButton.Click += ClearButton_Click;
        Controls.Add(clearButton);

        Label label = new Label();
        label.Text = $"Listening to KDU IP: {kduIp} (modify this in the source code)\nPerform a tightening to show the graph. Press \"Clear Plot\" to repeat.";
        label.Location = new Point(200, 10);
        label.Size = new Size(600, 60);
        Controls.Add(label);

        plotPanel = new PlotPanel();
        plotPanel.Location = new Point(0, 50);
        plotPanel.Size = new Size(Width, Height - 50);
        plotPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        plotPanel.Paint += PlotPanel_Paint;
        Controls.Add(plotPanel);
    }

    private void ClearButton_Click(object sender, EventArgs e)
    {
        ClearPlot();
        ClearRequested?.Invoke(this, EventArgs.Empty);
    }

    public void UpdatePlot(IEnumerable<PointF> points, string xAxisLabel = "X", string yAxisLabel = "Y")
    {
        dataPoints = points.ToList();
        xLabel = xAxisLabel;
        yLabel = yAxisLabel;
        plotPanel.Invalidate();
    }

    public void ClearPlot()
    {
        dataPoints.Clear();
        plotPanel.Invalidate();
    }

    private void PlotPanel_Paint(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Define plot area with margins
        int margin = 60;
        Rectangle plotArea = new Rectangle(margin, margin,
                                         plotPanel.Width - 2 * margin,
                                         plotPanel.Height - 2 * margin);

        // Draw axes
        using (Pen axisPen = new Pen(Color.Black, 2))
        {
            g.DrawLine(axisPen, plotArea.Left, plotArea.Bottom,
                      plotArea.Right, plotArea.Bottom);
            g.DrawLine(axisPen, plotArea.Left, plotArea.Top,
                      plotArea.Left, plotArea.Bottom);
        }

        if (dataPoints.Count == 0) return;

        // Calculate data bounds
        float minX = dataPoints.Min(p => p.X);
        float maxX = dataPoints.Max(p => p.X);
        float minY = dataPoints.Min(p => p.Y);
        float maxY = dataPoints.Max(p => p.Y);

        // Add padding to bounds
        float xRange = maxX - minX;
        float yRange = maxY - minY;
        if (xRange == 0) xRange = 1;
        if (yRange == 0) yRange = 1;
        minX -= xRange * 0.05f;
        maxX += xRange * 0.05f;
        minY -= yRange * 0.05f;
        maxY += yRange * 0.05f;

        // Draw grid and labels
        using (Pen gridPen = new Pen(Color.LightGray, 1))
        using (Font labelFont = new Font("Arial", 9))
        using (Brush labelBrush = new SolidBrush(Color.Black))
        {
            // X-axis grid and labels
            for (int i = 0; i <= 10; i++)
            {
                float x = plotArea.Left + (plotArea.Width * i / 10f);
                float dataX = minX + (maxX - minX) * i / 10f;

                g.DrawLine(gridPen, x, plotArea.Top, x, plotArea.Bottom);

                string label = dataX.ToString("F1");
                SizeF labelSize = g.MeasureString(label, labelFont);
                g.DrawString(label, labelFont, labelBrush,
                           x - labelSize.Width / 2, plotArea.Bottom + 5);
            }

            // Y-axis grid and labels
            for (int i = 0; i <= 10; i++)
            {
                float y = plotArea.Bottom - (plotArea.Height * i / 10f);
                float dataY = minY + (maxY - minY) * i / 10f;

                g.DrawLine(gridPen, plotArea.Left, y, plotArea.Right, y);

                string label = dataY.ToString("F1");
                SizeF labelSize = g.MeasureString(label, labelFont);
                g.DrawString(label, labelFont, labelBrush,
                           plotArea.Left - labelSize.Width - 5,
                           y - labelSize.Height / 2);
            }
        }

        // Draw axis labels
        using (Font axisFont = new Font("Arial", 12, FontStyle.Bold))
        using (Brush axisBrush = new SolidBrush(Color.Black))
        {
            SizeF xLabelSize = g.MeasureString(xLabel, axisFont);
            g.DrawString(xLabel, axisFont, axisBrush,
                        plotArea.Left + plotArea.Width / 2 - xLabelSize.Width / 2,
                        plotArea.Bottom + 35);

            g.TranslateTransform(15, plotArea.Top + plotArea.Height / 2);
            g.RotateTransform(-90);
            SizeF yLabelSize = g.MeasureString(yLabel, axisFont);
            g.DrawString(yLabel, axisFont, axisBrush,
                        -yLabelSize.Width / 2, -yLabelSize.Height / 2);
            g.ResetTransform();
        }

        // Draw line graph
        if (dataPoints.Count > 1)
        {
            using (Pen linePen = new Pen(lineColor, lineWidth))
            {
                PointF[] screenPoints = new PointF[dataPoints.Count];

                for (int i = 0; i < dataPoints.Count; i++)
                {
                    PointF point = dataPoints[i];
                    float screenX = plotArea.Left +
                                   (point.X - minX) / (maxX - minX) * plotArea.Width;
                    float screenY = plotArea.Bottom -
                                   (point.Y - minY) / (maxY - minY) * plotArea.Height;

                    screenPoints[i] = new PointF(screenX, screenY);
                }

                g.DrawLines(linePen, screenPoints);
            }
        }
    }
}

// Custom panel class with double buffering
public class PlotPanel : Panel
{
    public PlotPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.DoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }
}

// Usage example
public class Program
{
    private static string KDU1A_IP_ADDRESS = "192.168.5.14";
    private static LineGraphForm plotForm;
    private static ConcurrentQueue<Tuple<ushort,ushort,ushort>> dataQueue;
    private static List<PointF> plotPoints = new List<PointF>();
    private static Kducer kdu;
    private static bool tighteningCompleted = false;

    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        //plotForm = new ScatterPlotForm();
        plotForm = new LineGraphForm(KDU1A_IP_ADDRESS);

        // Subscribe to the clear event
        plotForm.ClearRequested += OnClearRequested;

        plotForm.Show();

        kdu = new Kducer(KDU1A_IP_ADDRESS);
        kdu.SetHighResGraphModeAsync(true).Wait();
        kdu.ClearResultsQueue();

        // only KDU-1A v41 supports real time graphing, but torque graphing still works (after tightening is finished) on older versions
        try { dataQueue = kdu.StartRealtimeTimeTorqueAngleDataStreamForNextTightening(); }
        catch (InvalidOperationException) { dataQueue = new ConcurrentQueue<Tuple<ushort, ushort, ushort>>(); }

        // Timer for real-time updates
        System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
        updateTimer.Interval = 20; // Update every 20ms
        updateTimer.Tick += UpdatePlot;
        updateTimer.Start();

        Application.Run(plotForm);
    }

    private static void UpdatePlot(object sender, EventArgs e)
    {
        if (tighteningCompleted)
            return;
        
        // Dequeue all available data points
        while (dataQueue.TryDequeue(out Tuple<ushort, ushort, ushort> data))
        {
            plotPoints.Add(new PointF(data.Item1, data.Item2));
            // Debug.WriteLine($"Received: {data.Item1} {data.Item2} {data.Item3}");
        }

        // Check if tightening is completed
        if (kdu.IsRealtimeTorqueAngleDataStreamTaskCompleted() || kdu.HasNewResult())
        {
            tighteningCompleted = true;
            
            KducerTighteningResult res = kdu.GetResult();
            KducerTorqueAngleTimeGraph fullGraph = res.GetKducerTorqueAngleTimeGraph();
            ushort[] angles = fullGraph.getAngleSeries();
            ushort[] torques = fullGraph.getTorqueSeries();
            ClearPlot();
            for (int i = 0; i < angles.Length; i++)
            {
                plotPoints.Add(new PointF(angles[i], torques[i]));
            }
        }

        plotForm.UpdatePlot(plotPoints, "Time [ms]", "Torque [mNm]");
    }

    // Event handler for clear button clicks
    private static void OnClearRequested(object sender, EventArgs e)
    {
        // Clear the local data
        ClearPlot();
        // Restart data acquisition 
        tighteningCompleted = false;
        try
        {
            kdu.StopRealtimeTorqueAngleDataStreamTask();
            dataQueue = kdu.StartRealtimeTimeTorqueAngleDataStreamForNextTightening();
        }
        catch (InvalidOperationException) { dataQueue = new ConcurrentQueue<Tuple<ushort, ushort, ushort>>(); }

    }

    // Function to clear the plot
    public static void ClearPlot()
    {
        plotPoints.Clear();
        plotForm.ClearPlot();
    }
}
