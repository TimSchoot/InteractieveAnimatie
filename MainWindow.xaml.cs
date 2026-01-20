using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InteractieveAnimatie
{
    public partial class MainWindow : Window
    {
        private readonly RadialGradientBrush _spotMask;
        private const double SpotRadius = 0.18; // tweak to make the lit circle larger/smaller
        private const int PixelsLeft = 1504; // image pixel dimensions
        private const int PixelsTop = 1004;

        // UDP listener for normalized hand coordinates ("x y" where x,y in [0..1] or -1 -1 when no hand)
        private const int ListenPort = 5005;
        private CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();

            // Set the image source dynamically - looks in the executable's directory
            string imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mooiste-natuur-europa.jpg");
            
            // Fallback to project directory if running in Debug/Release folder
            if (!File.Exists(imagePath))
            {
                string projectPath = Path.GetDirectoryName(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory));
                imagePath = Path.Combine(projectPath, "mooiste-natuur-europa.jpg");
            }

            if (File.Exists(imagePath))
            {
                MainImage.Source = new BitmapImage(new Uri(imagePath, UriKind.Absolute));
            }
            else
            {
                MessageBox.Show($"Image not found: {imagePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Create a radial opacity mask:
            _spotMask = new RadialGradientBrush
            {
                RadiusX = SpotRadius / PixelsLeft * PixelsTop,
                RadiusY = SpotRadius,
                GradientOrigin = new Point(-1, -1),
                Center = new Point(-1, -1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0),   // transparent center
                    new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.0) // opaque edge
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

                        // expected "x y" (e.g. "0.45 0.62") or "-1 -1"
                        var parts = msg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 &&
                            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                        {
                            // update UI on UI thread
                            Dispatcher.Invoke(() =>
                            {
                                if (x < 0 || y < 0)
                                {
                                    // no hand — hide spot
                                    _spotMask.Center = new Point(-1, -1);
                                    _spotMask.GradientOrigin = new Point(-1, -1);
                                }
                                else
                                {
                                    // coordinates are normalized 0..1; clamp defensively
                                    x = Math.Max(0.0, Math.Min(1.0, x));
                                    y = Math.Max(0.0, Math.Min(1.0, y));
                                    _spotMask.Center = new Point(x, y);
                                    _spotMask.GradientOrigin = new Point(x, y);
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