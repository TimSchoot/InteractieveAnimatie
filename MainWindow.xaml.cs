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
<<<<<<< Updated upstream
                    new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.0) // opaque edge
=======
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.3),   // Changed from 0.6 to 0.3
                    new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.0)  // opaque edge
>>>>>>> Stashed changes
                }
            };

            OverlayRect.OpacityMask = _spotMask;

            DebugText.Text = $"Debug: listening port {ListenPort}";

            // start UDP listener to receive hand coordinates from an external tracker (see Python script)
            _cts = new CancellationTokenSource();
            Task.Run(() => UdpListenLoop(_cts.Token));
        }

<<<<<<< Updated upstream
=======
        private void LoadImagesFromFolder()
        {
            try
            {
                // Get the folder where the executable is running
                string exeFolder = AppDomain.CurrentDomain.BaseDirectory;
                
                // Search for Where's Waldo images (jpg, jpeg, png)
                var imageFiles = Directory.GetFiles(exeFolder, "Wheres-Waldo-*.*")
                    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f)
                    .ToArray();

                if (imageFiles.Length == 0)
                {
                    // Try parent folder (for debug mode)
                    string projectFolder = Directory.GetParent(exeFolder).Parent.Parent.FullName;
                    imageFiles = Directory.GetFiles(projectFolder, "Wheres-Waldo-*.*")
                        .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                   f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f)
                        .ToArray();
                }

                _imagePaths = imageFiles;
                
                Debug.WriteLine($"Found {imageFiles.Length} Where's Waldo images:");
                foreach (var img in imageFiles)
                {
                    Debug.WriteLine($"  - {System.IO.Path.GetFileName(img)}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading images: {ex.Message}");
                _imagePaths = new string[0];
            }
        }
        private void LoadHandGesturesImages()
        {
            MainImage.Source = new BitmapImage(new Uri("pack://application:,,,/Images/HandGestures.png"));
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

>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
=======

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
                                            case 1: // Closed_Fist - at least something now
                                                LoadHandGesturesImages();
                                                if (isNewGesture)
                                                {
                                                    Debug.WriteLine("?? FIST - No action assigned.");
                                                }
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
>>>>>>> Stashed changes
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