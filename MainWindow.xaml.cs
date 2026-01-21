using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace InteractieveAnimatie
{
    public partial class MainWindow : Window
    {
        private readonly RadialGradientBrush _spotMask;
        private const double SpotRadius = 0.22;

        // UDP listener for normalized hand coordinates ("x1 y1 x2 y2" where -1 -1 means no hand)
        private const int ListenPort = 5005;
        private CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();

            // Set the image source (absolute file path you previously used)
            MainImage.Source = new BitmapImage(new Uri(
                @"C:\Users\odinw\Documents\GitHub\InteractieveAnimatie\Wheres-Waldo-Ski.jpg",
                UriKind.Absolute));

            // Create radial opacity mask for hand 1 only
            _spotMask = new RadialGradientBrush
            {
                RadiusX = SpotRadius,
                RadiusY = SpotRadius,
                GradientOrigin = new Point(-1, -1),
                Center = new Point(-1, -1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0),   // transparent center (LICHT)
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.6),   // gradient voor zachte overgang
                    new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.0) // opaque edge (DONKER)
                }
            };

            OverlayRect.OpacityMask = _spotMask;

            DebugText.Text = $"Debug: listening port {ListenPort}";

            // start UDP listener to receive hand coordinates from an external tracker (see Python script)
            _cts = new CancellationTokenSource();
            Task.Run(() => UdpListenLoop(_cts.Token));
        }

        private async Task UdpListenLoop(CancellationToken token)
        {
            using (var udp = new UdpClient(ListenPort))
            {
                udp.Client.ReceiveTimeout = 2000;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var result = await udp.ReceiveAsync().ConfigureAwait(false);
                        var msg = Encoding.UTF8.GetString(result.Buffer).Trim();
                        Debug.WriteLine($"UDP recv: '{msg}'");
                        Dispatcher.Invoke(() => DebugText.Text = $"UDP: {msg}");

                        // expected "x1 y1 x2 y2" (e.g. "0.45 0.62 0.30 0.50") or "-1 -1 -1 -1"
                        var parts = msg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4 &&
                            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x1) &&
                            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y1) &&
                            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x2) &&
                            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double y2))
                        {
                            // update UI on UI thread
                            Dispatcher.Invoke(() =>
                            {
                                // Update hand 1 position only (ignore hand 2)
                                if (x1 < 0 || y1 < 0)
                                {
                                    // no hand — hide spot
                                    _spotMask.Center = new Point(-1, -1);
                                    _spotMask.GradientOrigin = new Point(-1, -1);
                                }
                                else
                                {
                                    x1 = Math.Max(0.0, Math.Min(1.0, x1));
                                    y1 = Math.Max(0.0, Math.Min(1.0, y1));
                                    _spotMask.Center = new Point(x1, y1);
                                    _spotMask.GradientOrigin = new Point(x1, y1);
                                }
                            });
                        }
                        else
                        {
                            Debug.WriteLine($"Malformed UDP message: '{msg}'");
                            Dispatcher.Invoke(() => DebugText.Text = $"Malformed: {msg}");
                        }
                    }
                    catch (SocketException ex)
                    {
                        // timeout or socket error — continue loop
                        Debug.WriteLine($"SocketException: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"UDP loop error: {ex}");
                    }
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            base.OnClosed(e);
        }

        // (Optional) keep these for debug or fallback mouse control
        private void RootGrid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var pos = e.GetPosition(RootGrid);
            double nx = pos.X / RootGrid.ActualWidth;
            double ny = pos.Y / RootGrid.ActualHeight;
            nx = double.IsNaN(nx) ? 0.5 : Math.Max(0, Math.Min(1, nx));
            ny = double.IsNaN(ny) ? 0.5 : Math.Max(0, Math.Min(1, ny));
            _spotMask.Center = new Point(nx, ny);
            _spotMask.GradientOrigin = new Point(nx, ny);
        }

        private void RootGrid_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _spotMask.Center = new Point(-1, -1);
            _spotMask.GradientOrigin = new Point(-1, -1);
        }
    }
}