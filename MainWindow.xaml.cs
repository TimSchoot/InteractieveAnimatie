using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
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
        private double _spotRadius = 0.22;  // Changed to variable for dynamic sizing
        private const double MinSpotRadius = 0.10;
        private const double MaxSpotRadius = 0.35;

        // Where's Waldo images
        private readonly string[] _imagePaths = new[]
        {
            @"C:\Users\odinw\Documents\GitHub\InteractieveAnimatie\Wheres-Waldo-Skiing.jpg",
            @"C:\Users\odinw\Documents\GitHub\InteractieveAnimatie\Wheres-Waldo-Beach.jpg",
            @"C:\Users\odinw\Documents\GitHub\InteractieveAnimatie\Wheres-Waldo-Toys.jpg",
            @"C:\Users\odinw\Documents\GitHub\InteractieveAnimatie\Wheres-Waldo-Film-Set.jpg",
            @"C:\Users\odinw\Documents\GitHub\InteractieveAnimatie\Wheres-Waldo-Underground.jpg",
        };
        private int _currentImageIndex = 0;
        private int _lastGestureId = 0;  // Track last gesture for debouncing
        private DateTime _lastImageChangeTime = DateTime.MinValue;  // Cooldown for image changes
        private const int ImageChangeCooldownMs = 2000;  // 2 second cooldown between image changes
        private DateTime _lastSizeChangeTime = DateTime.MinValue;  // Throttle for size changes
        private const int SizeChangeThrottleMs = 150;  // 150ms between size changes (slower)

        // UDP listener for normalized hand coordinates ("x1 y1 x2 y2 gesture_id")
        // gesture_id: 0=none, 1=fist(unused), 2=victory(prev), 3=thumb_up(size+), 4=thumb_down(size-), 5=open_palm(unused), 6=pointing_up(next), 7=middle_finger(restart)
        private const int ListenPort = 5005;
        private CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();

            // Load first image
            LoadCurrentImage();

            // Create radial opacity mask
            _spotMask = new RadialGradientBrush
            {
                RadiusX = _spotRadius,
                RadiusY = _spotRadius,
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

            DebugText.Text = $"Image {_currentImageIndex + 1}/{_imagePaths.Length} - Port {ListenPort}";

            // start UDP listener to receive hand coordinates from an external tracker
            _cts = new CancellationTokenSource();
            Task.Run(() => UdpListenLoop(_cts.Token));
        }

        private void LoadCurrentImage()
        {
            if (_currentImageIndex >= 0 && _currentImageIndex < _imagePaths.Length)
            {
                MainImage.Source = new BitmapImage(new Uri(_imagePaths[_currentImageIndex], UriKind.Absolute));
            }
        }

        private void NextImage()
        {
            _currentImageIndex = (_currentImageIndex + 1) % _imagePaths.Length;
            LoadCurrentImage();
            DebugText.Text = $"Image {_currentImageIndex + 1}/{_imagePaths.Length}";
        }

        private void PreviousImage()
        {
            _currentImageIndex = (_currentImageIndex - 1 + _imagePaths.Length) % _imagePaths.Length;
            LoadCurrentImage();
            DebugText.Text = $"Image {_currentImageIndex + 1}/{_imagePaths.Length}";
        }

        private void IncreaseSpotlightSize()
        {
            // Check throttle to prevent too-fast changes
            if ((DateTime.Now - _lastSizeChangeTime).TotalMilliseconds < SizeChangeThrottleMs)
                return;
            
            _spotRadius = Math.Min(MaxSpotRadius, _spotRadius + 0.02);
            _spotMask.RadiusX = _spotRadius;
            _spotMask.RadiusY = _spotRadius;
            _lastSizeChangeTime = DateTime.Now;
            DebugText.Text = $"Spotlight: {(_spotRadius * 100):F0}% - Image {_currentImageIndex + 1}/{_imagePaths.Length}";
        }

        private void DecreaseSpotlightSize()
        {
            // Check throttle to prevent too-fast changes
            if ((DateTime.Now - _lastSizeChangeTime).TotalMilliseconds < SizeChangeThrottleMs)
                return;
            
            _spotRadius = Math.Max(MinSpotRadius, _spotRadius - 0.02);
            _spotMask.RadiusX = _spotRadius;
            _spotMask.RadiusY = _spotRadius;
            _lastSizeChangeTime = DateTime.Now;
            DebugText.Text = $"Spotlight: {(_spotRadius * 100):F0}% - Image {_currentImageIndex + 1}/{_imagePaths.Length}";
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

                        // expected "x1 y1 x2 y2 gesture_id" (e.g. "0.45 0.62 0.30 0.50 1")
                        var parts = msg.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5 &&
                            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x1) &&
                            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y1) &&
                            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x2) &&
                            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double y2) &&
                            int.TryParse(parts[4], out int gestureId))
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
                                    // Always follow hand position
                                    x1 = Math.Max(0.0, Math.Min(1.0, x1));
                                    y1 = Math.Max(0.0, Math.Min(1.0, y1));
                                    _spotMask.Center = new Point(x1, y1);
                                    _spotMask.GradientOrigin = new Point(x1, y1);
                                }

                                // Gesture detection with debouncing
                                // Only trigger on gesture change (rising edge) for most gestures
                                // But allow continuous trigger for thumbs up/down (resize while holding)
                                if (gestureId > 0)
                                {
                                    // Check cooldown for image-changing gestures
                                    bool canChangeImage = (DateTime.Now - _lastImageChangeTime).TotalMilliseconds > ImageChangeCooldownMs;
                                    
                                    // For thumbs: allow continuous action while gesture is held
                                    // For others: only trigger on gesture change
                                    bool isNewGesture = gestureId != _lastGestureId;
                                    bool isContinuousGesture = (gestureId == 3 || gestureId == 4); // Thumbs up/down
                                    
                                    if (isNewGesture || isContinuousGesture)
                                    {
                                        switch (gestureId)
                                        {
                                            case 1: // Closed_Fist - Disabled (too many false positives)
                                                // No action - was causing issues during thumbs up/down
                                                break;
                                            case 2: // Victory/Peace
                                                if (isNewGesture && canChangeImage)
                                                {
                                                    PreviousImage();
                                                    _lastImageChangeTime = DateTime.Now;
                                                    Debug.WriteLine("?? PEACE - Previous image!");
                                                }
                                                break;
                                            case 3: // Thumb_Up (continuous)
                                                IncreaseSpotlightSize();
                                                if (isNewGesture) Debug.WriteLine("?? THUMBS UP - Spotlight bigger!");
                                                break;
                                            case 4: // Thumb_Down (continuous)
                                                DecreaseSpotlightSize();
                                                if (isNewGesture) Debug.WriteLine("?? THUMBS DOWN - Spotlight smaller!");
                                                break;
                                            case 5: // Open_Palm
                                                // No action - palm just tracks normally
                                                break;
                                            case 6: // Pointing_Up - Primary "next" gesture
                                                if (isNewGesture && canChangeImage)
                                                {
                                                    NextImage();
                                                    _lastImageChangeTime = DateTime.Now;
                                                    Debug.WriteLine("?? POINTING UP - Next image!");
                                                }
                                                break;
                                            case 7: // Middle_Finger
                                                if (isNewGesture)
                                                {
                                                    _currentImageIndex = 0;
                                                    _spotRadius = 0.22;
                                                    _spotMask.RadiusX = _spotRadius;
                                                    _spotMask.RadiusY = _spotRadius;
                                                    _lastImageChangeTime = DateTime.Now;
                                                    LoadCurrentImage();
                                                    DebugText.Text = $"RESET - Image 1/{_imagePaths.Length}";
                                                    Debug.WriteLine("?? MIDDLE FINGER - Full restart!");
                                                }
                                                break;
                                        }
                                    }
                                }
                                _lastGestureId = gestureId;
                            });
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
