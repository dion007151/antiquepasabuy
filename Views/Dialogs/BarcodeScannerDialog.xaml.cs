using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;
using FlashCap;
using ZXing;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class BarcodeScannerDialog : Window
{
    private readonly AppDbContext _db;
    private readonly Action<Product> _onProductScanned;

    // ── Buying List Mode ─────────────────────────────────────────────────────
    // When opened from BuyingListPage, the dialog calls _onBarcodeScannedExtended or _onBarcodeScanned
    // with detected product details and actual/manual price.
    private readonly Action<string>? _onBarcodeScanned;
    private readonly Action<string, string?, decimal?>? _onBarcodeScannedExtended;
    private bool _buyingListMode => _onBarcodeScanned != null || _onBarcodeScannedExtended != null;

    private Product? _lastScannedProduct;

    private CaptureDevice? _captureDevice;
    private List<CaptureDeviceDescriptor> _descriptors = [];
    private ZXing.Windows.Compatibility.BarcodeReader _barcodeReader = null!;

    private volatile bool _isClosing = false;
    private readonly SemaphoreSlim _cameraLock = new(1, 1);
    private CancellationTokenSource? _cameraCts;

    private string _lastScannedBarcode = "";
    private DateTime _lastScannedTime = DateTime.MinValue;
    private const int ScanCooldownMilliseconds = 1800; // 1.8s cooldown for the same barcode

    private int _scannedCount = 0;
    private decimal _scannedTotal = 0m;

    // USB Hardware Barcode Gun Buffer
    private readonly System.Text.StringBuilder _scannerBuffer = new();
    private DateTime _lastCharTime = DateTime.MinValue;

    // Pending external product (waiting for manual price entry)
    private string? _pendingExternalProductName;
    private string? _pendingExternalBarcode;
    private string? _pendingExternalBrand;
    private string? _pendingExternalCategory;
    private string? _pendingExternalSource;
    private bool _isLookingUp = false;

    // ── Optimized Real-Time Camera Pipeline Fields ───────────────────────────
    // Drop-frame guard for live preview: 0 = idle/ready for next frame, 1 = rendering on UI
    private int _isPreviewUpdating = 0;

    // Decoupled barcode detection: fast continuous loop (~15-20 times per second)
    private int _isDecoding = 0; // 0 = idle, 1 = background decode in progress
    private DateTime _lastBarcodeScanTime = DateTime.MinValue;
    private const int BarcodeScanIntervalMs = 50; // 50ms = rapid, easy detection

    public BarcodeScannerDialog(AppDbContext db, Action<Product> onProductScanned)
    {
        InitializeComponent();
        _db = db;
        _onProductScanned = onProductScanned;
        _onBarcodeScanned = null;
        _onBarcodeScannedExtended = null;
        InitBarcodeReader();
        Loaded += async (_, _) => await InitializeCameraAsync();
        Closing += OnWindowClosing;
        KeyDown += OnWindowKeyDown;
    }

    /// <summary>
    /// Buying List mode: scans barcodes and calls back with the raw barcode string.
    /// </summary>
    public BarcodeScannerDialog(AppDbContext db, Action<string> onBarcodeScanned, string windowTitle = "Scanner — Buying List")
    {
        InitializeComponent();
        _db = db;
        _onProductScanned = _ => { };
        _onBarcodeScanned = onBarcodeScanned;
        _onBarcodeScannedExtended = null;
        Title = windowTitle;
        InitBarcodeReader();
        Loaded += async (_, _) => await InitializeCameraAsync();
        Closing += OnWindowClosing;
        KeyDown += OnWindowKeyDown;
    }

    /// <summary>
    /// Buying List mode with full detection: scans barcodes, looks up worldwide & PH databases,
    /// and returns the barcode, detected product name, and detected or user-entered price.
    /// </summary>
    public BarcodeScannerDialog(AppDbContext db, Action<string, string?, decimal?> onBarcodeScannedExtended, string windowTitle = "Scanner — Buying List")
    {
        InitializeComponent();
        _db = db;
        _onProductScanned = _ => { };
        _onBarcodeScanned = barcode => onBarcodeScannedExtended(barcode, null, null);
        _onBarcodeScannedExtended = onBarcodeScannedExtended;
        Title = windowTitle;
        InitBarcodeReader();
        Loaded += async (_, _) => await InitializeCameraAsync();
        Closing += OnWindowClosing;
        KeyDown += OnWindowKeyDown;
    }

    private void InitBarcodeReader()
    {
        var decodingOptions = new ZXing.Common.DecodingOptions
        {
            TryHarder = true,
            TryInverted = true,
            PossibleFormats = new List<BarcodeFormat>
            {
                BarcodeFormat.EAN_13,
                BarcodeFormat.UPC_A,
                BarcodeFormat.EAN_8,
                BarcodeFormat.UPC_E,
                BarcodeFormat.CODE_128,
                BarcodeFormat.CODE_39,
                BarcodeFormat.QR_CODE
            }
        };

        // Primary high-performance reader
        _barcodeReader = new ZXing.Windows.Compatibility.BarcodeReader
        {
            AutoRotate = true,
            Options = decodingOptions
        };
    }

    // ── Webcam Initialization & Lifecycle ────────────────────────────────────

    private async Task InitializeCameraAsync()
    {
        if (_isClosing) return;

        try
        {
            var devices = new CaptureDevices();
            _descriptors = devices.EnumerateDescriptors().ToList();

            if (_descriptors.Count == 0)
            {
                ShowNoCameraError("No webcam was detected on this computer.");
                return;
            }

            // Temporarily detach SelectionChanged while populating to avoid concurrent start
            CameraCombo.SelectionChanged -= OnCameraSelectionChanged;
            CameraCombo.ItemsSource = _descriptors.Select(d => d.Name).ToList();
            CameraCombo.SelectedIndex = 0;
            CameraCombo.SelectionChanged += OnCameraSelectionChanged;

            if (!_isClosing)
            {
                await StartSelectedCameraAsync();
            }
        }
        catch (Exception ex)
        {
            ShowNoCameraError($"Camera initialization failed: {ex.Message}");
        }
    }

    private async Task StartSelectedCameraAsync()
    {
        if (_isClosing) return;

        // Cancel previous capture CTS if any
        _cameraCts?.Cancel();
        _cameraCts = new CancellationTokenSource();
        var ct = _cameraCts.Token;

        await _cameraLock.WaitAsync();
        try
        {
            if (_isClosing || ct.IsCancellationRequested) return;

            // Stop existing capture device before starting a new one
            if (_captureDevice != null)
            {
                var prev = _captureDevice;
                _captureDevice = null;
                try
                {
                    await prev.StopAsync();
                    prev.Dispose();
                }
                catch { }
            }

            if (_isClosing || ct.IsCancellationRequested) return;

            if (CameraCombo.SelectedIndex < 0 || CameraCombo.SelectedIndex >= _descriptors.Count)
                return;

            var descriptor = _descriptors[CameraCombo.SelectedIndex];

            var candidates = descriptor.Characteristics
                .OrderBy(ScoreCharacteristic)
                .ToList();

            if (candidates.Count == 0)
            {
                ShowNoCameraError("No valid video formats found for this camera.");
                return;
            }

            NoCameraPanel.Visibility = Visibility.Collapsed;
            AimingOverlay.Visibility = Visibility.Visible;
            _isPreviewUpdating = 0;
            _isDecoding = 0;

            Exception? lastEx = null;
            foreach (var characteristic in candidates)
            {
                if (_isClosing || ct.IsCancellationRequested) return;

                try
                {
                    var dev = await descriptor.OpenAsync(characteristic, OnPixelBufferArrivedAsync);
                    if (_isClosing || ct.IsCancellationRequested)
                    {
                        try { await dev.StopAsync(); } catch { }
                        try { dev.Dispose(); } catch { }
                        return;
                    }

                    await dev.StartAsync();
                    if (_isClosing || ct.IsCancellationRequested)
                    {
                        try { await dev.StopAsync(); } catch { }
                        try { dev.Dispose(); } catch { }
                        return;
                    }

                    _captureDevice = dev;
                    SetStatusNormal("Camera active — point barcode at camera", "Hold the product still inside the target box");
                    return;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            ShowNoCameraError($"Failed to access webcam: {lastEx?.Message ?? "No compatible video format found."}\n\nPlease check that your laptop camera is enabled and not in use by another app.");
        }
        catch (Exception ex)
        {
            if (!_isClosing)
                ShowNoCameraError($"Failed to access webcam: {ex.Message}");
        }
        finally
        {
            _cameraLock.Release();
        }
    }

    private static int ScoreCharacteristic(VideoCharacteristics c)
    {
        double fps = c.FramesPerSecond.Denominator > 0
            ? (double)c.FramesPerSecond.Numerator / c.FramesPerSecond.Denominator
            : 30.0;
        bool is30Fps = Math.Abs(fps - 30.0) <= 2.0;

        // 1. 640x480 @ ~30 FPS (ideal 4:3 standard)
        if (c.Width == 640 && c.Height == 480 && is30Fps) return 0;

        // 2. 640x360 @ ~30 FPS (ideal 16:9 standard)
        if (c.Width == 640 && c.Height == 360 && is30Fps) return 1;

        // 3. 640x480 at any FPS (prefer closest to 30)
        if (c.Width == 640 && c.Height == 480) return 10 + (int)Math.Abs(fps - 30.0);

        // 4. 640x360 at any FPS (prefer closest to 30)
        if (c.Width == 640 && c.Height == 360) return 20 + (int)Math.Abs(fps - 30.0);

        // 5. Width == 640 with any other height
        if (c.Width == 640) return 30 + (int)Math.Abs(fps - 30.0);

        // 6. Other resolutions <= 640 (e.g. 320x240)
        if (c.Width < 640) return 40 + (640 - c.Width) / 10 + (int)Math.Abs(fps - 30.0);

        // 7. 720p (1280x720) or <= 1280 @ 30 FPS as fallback
        if (c.Width <= 1280) return 100 + (c.Width - 640) / 10 + (int)Math.Abs(fps - 30.0);

        // 8. Higher resolutions as last resort
        return 500 + c.Width / 10;
    }

    private async Task StopCameraAsync()
    {
        _isClosing = true;
        _cameraCts?.Cancel();
        try
        {
            await _cameraLock.WaitAsync();
            try
            {
                var device = _captureDevice;
                _captureDevice = null;
                if (device != null)
                {
                    try { await device.StopAsync(); } catch { }
                    try { device.Dispose(); } catch { }
                }
            }
            finally
            {
                _cameraLock.Release();
            }
        }
        catch { }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        _cameraCts?.Cancel();
        CameraPreviewImage.Source = null;

        var device = _captureDevice;
        _captureDevice = null;

        if (device != null)
        {
            try
            {
                // Synchronously wait up to 1.2s on background stop/dispose so DirectShow/MediaFoundation pin is freed before dialog closes
                var stopTask = Task.Run(async () =>
                {
                    try { await device.StopAsync(); } catch { }
                    try { device.Dispose(); } catch { }
                });
                stopTask.Wait(1200);
            }
            catch { }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _isClosing = true;
        _cameraCts?.Cancel();
        CameraPreviewImage.Source = null;
        if (_captureDevice != null)
        {
            var device = _captureDevice;
            _captureDevice = null;
            try
            {
                var stopTask = Task.Run(async () =>
                {
                    try { await device.StopAsync(); } catch { }
                    try { device.Dispose(); } catch { }
                });
                stopTask.Wait(1200);
            }
            catch { }
        }
    }

    // ── Frame Processing & Barcode Detection ──────────────────────────────────
    //
    // PIPELINE OPTIMIZATION:
    //   1. PREVIEW:
    //      - Zero-backpressure drop-frame pattern ensures preview ALWAYS displays real-time frame
    //      - Frame decompression happens on the capture worker thread, off the UI thread
    //      - BitmapImage is Frozen (cross-thread immutable, DirectX hardware accelerated)
    //      - UI thread only sets the Image.Source pointer (<0.01ms work on UI thread)
    //      - Latency is strictly 1 frame (~33ms) — moving hand responds immediately!
    //
    //   2. BARCODE DETECTION:
    //      - Completely decoupled from live video preview
    //      - Only sampled ~5 times per second (every 200ms), NOT on every frame
    //      - Runs entirely in Task.Run on thread pool — never stalls the camera capture loop
    //

    private Task OnPixelBufferArrivedAsync(PixelBufferScope scope)
    {
        if (_isClosing) return Task.CompletedTask;

        try
        {
            byte[] imageBytes = scope.Buffer.CopyImage();
            var now = DateTime.UtcNow;

            // 1. LIVE PREVIEW: Drop-frame pattern for real-time responsiveness
            if (Interlocked.CompareExchange(ref _isPreviewUpdating, 1, 0) == 0)
            {
                try
                {
                    // Decompress JPEG frame on camera worker thread
                    var bitmapImage = new BitmapImage();
                    using (var ms = new MemoryStream(imageBytes))
                    {
                        bitmapImage.BeginInit();
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.StreamSource = ms;
                        bitmapImage.EndInit();
                    }
                    bitmapImage.Freeze(); // Freeze allows cross-thread use and DirectX GPU caching

                    // Assign to Image control on UI thread
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!_isClosing)
                            {
                                CameraPreviewImage.Source = bitmapImage;
                            }
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _isPreviewUpdating, 0);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Render);
                }
                catch
                {
                    Interlocked.Exchange(ref _isPreviewUpdating, 0);
                }
            }

            // 2. BARCODE DETECTION: Sampled frames (fast 50ms interval, ~20 checks/sec)
            // Decoupled onto background thread pool — NEVER blocks camera preview
            if ((now - _lastBarcodeScanTime).TotalMilliseconds >= BarcodeScanIntervalMs &&
                Interlocked.CompareExchange(ref _isDecoding, 1, 0) == 0)
            {
                _lastBarcodeScanTime = now;

                _ = Task.Run(() =>
                {
                    try
                    {
                        using var ms = new MemoryStream(imageBytes);
                        using var bitmap = new System.Drawing.Bitmap(ms);
                        var result = TryDecodeBitmap(bitmap);

                        if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                        {
                            var barcode = result.Text.Trim();

                            // Filter out noisy partial reads
                            if (result.BarcodeFormat == BarcodeFormat.CODE_39 && barcode.Length < 4)
                                return;

                            // Validate GS1 check digit for standard numerical barcodes (EAN-13, UPC-A, EAN-8)
                            if (!BarcodeNormalizer.ValidateCheckDigit(barcode))
                                return;

                            Dispatcher.InvokeAsync(() => HandleDetectedBarcode(barcode));
                        }
                    }
                    catch
                    {
                        // Detection error — retry on next sampled frame
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isDecoding, 0);
                    }
                });
            }
        }
        catch
        {
            // Frame capture error — ignore single frame
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Multi-pass detection pipeline designed specifically for laptop webcams:
    /// Pass 1: Center ROI crop (where user aims barcode) with Hybrid binarizer
    /// Pass 2: Center ROI crop with Global Histogram binarizer (catches faint/reflective barcodes)
    /// Pass 3: Center ROI crop with Contrast-Stretching enhancement
    /// Pass 4: Full frame fallback
    /// </summary>
    private Result? TryDecodeBitmap(System.Drawing.Bitmap bitmap)
    {
        // 1. Center ROI crop (middle 70% width, 55% height)
        int cropW = Math.Max(100, (int)(bitmap.Width * 0.70));
        int cropH = Math.Max(80, (int)(bitmap.Height * 0.55));
        int cropX = (bitmap.Width - cropW) / 2;
        int cropY = (bitmap.Height - cropH) / 2;
        var cropRect = new System.Drawing.Rectangle(cropX, cropY, cropW, cropH);

        using (var cropped = bitmap.Clone(cropRect, bitmap.PixelFormat))
        {
            // Pass 1: Center ROI crop with primary reader
            var res = _barcodeReader.Decode(cropped);
            if (res != null && !string.IsNullOrWhiteSpace(res.Text)) return res;

            // Pass 2: Contrast stretched center crop (handles low-light, blur, glare)
            try
            {
                using var enhanced = EnhanceBarcodeContrast(cropped);
                res = _barcodeReader.Decode(enhanced);
                if (res != null && !string.IsNullOrWhiteSpace(res.Text)) return res;
            }
            catch { }
        }

        // Pass 3: Full frame fallback
        var fullRes = _barcodeReader.Decode(bitmap);
        if (fullRes != null && !string.IsNullOrWhiteSpace(fullRes.Text)) return fullRes;

        // Pass 4: Contrast stretched full frame
        try
        {
            using var enhancedFull = EnhanceBarcodeContrast(bitmap);
            fullRes = _barcodeReader.Decode(enhancedFull);
            if (fullRes != null && !string.IsNullOrWhiteSpace(fullRes.Text)) return fullRes;
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Stretches dynamic range of pixels to pure 0..255 black & white.
    /// Turns washed-out webcam images into crisp high-contrast barcodes.
    /// </summary>
    private static System.Drawing.Bitmap EnhanceBarcodeContrast(System.Drawing.Bitmap src)
    {
        var dst = new System.Drawing.Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var srcData = src.LockBits(new System.Drawing.Rectangle(0, 0, src.Width, src.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var dstData = dst.LockBits(new System.Drawing.Rectangle(0, 0, dst.Width, dst.Height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        try
        {
            int bytes = Math.Abs(srcData.Stride) * src.Height;
            byte[] rgbValues = new byte[bytes];
            byte[] dstValues = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(srcData.Scan0, rgbValues, 0, bytes);

            byte min = 255;
            byte max = 0;
            for (int i = 0; i < bytes; i += 3)
            {
                byte gray = (byte)((rgbValues[i] * 29 + rgbValues[i + 1] * 150 + rgbValues[i + 2] * 77) >> 8);
                if (gray < min) min = gray;
                if (gray > max) max = gray;
            }

            int range = max - min;
            if (range < 25) range = 25;

            for (int i = 0; i < bytes; i += 3)
            {
                byte gray = (byte)((rgbValues[i] * 29 + rgbValues[i + 1] * 150 + rgbValues[i + 2] * 77) >> 8);
                int stretched = (gray - min) * 255 / range;
                byte val = (byte)Math.Clamp(stretched, 0, 255);
                dstValues[i] = val;
                dstValues[i + 1] = val;
                dstValues[i + 2] = val;
            }

            System.Runtime.InteropServices.Marshal.Copy(dstValues, 0, dstData.Scan0, bytes);
        }
        finally
        {
            src.UnlockBits(srcData);
            dst.UnlockBits(dstData);
        }

        return dst;
    }

    // ── Barcode Handling ──────────────────────────────────────────────────────

    private async void HandleDetectedBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return;

        var now = DateTime.UtcNow;
        // Check cooldown for identical barcode to prevent rapid duplicate accidental scans
        if (string.Equals(barcode, _lastScannedBarcode, StringComparison.OrdinalIgnoreCase) &&
            (now - _lastScannedTime).TotalMilliseconds < ScanCooldownMilliseconds)
        {
            return;
        }

        _lastScannedBarcode = barcode;
        _lastScannedTime = now;

        // Beep once when detected
        PlayScanBeep();

        // Hide any pending manual panels
        ManualPricePanel.Visibility = Visibility.Collapsed;
        if (QuickRegisterPanel != null) QuickRegisterPanel.Visibility = Visibility.Collapsed;
        if (EditFoundPriceBtn != null) EditFoundPriceBtn.Visibility = Visibility.Collapsed;

        // Normalize barcode
        var candidates = BarcodeNormalizer.GetNormalizedCandidates(barcode);
        var canonicalBarcode = candidates.Count > 0 ? candidates[0] : barcode.Trim();

        // 1. Search the local DIONIX Products database first
        var product = FindProductByBarcode(canonicalBarcode);

        if (product != null)
        {
            _lastScannedProduct = product;
            var displayBarcode = product.Barcode ?? canonicalBarcode;

            if (product.Price > 0)
            {
                if (_buyingListMode)
                {
                    _onBarcodeScannedExtended?.Invoke(canonicalBarcode, product.Name, product.Price);
                    _onBarcodeScanned?.Invoke(canonicalBarcode);
                }
                else
                {
                    _onProductScanned(product);
                }

                _scannedCount++;
                _scannedTotal += product.Price;

                SetStatusFoundWithPrice(product.Name, "DIONIX Local Database", displayBarcode, product.Price);

                SessionStatsLabel.Text = $"{_scannedCount} item(s) scanned (₱{_scannedTotal:N2})";
                LastScannedProductName.Text = product.Name;
                LastScannedBarcodeText.Text = $"Source: DIONIX Local Database • Barcode: {displayBarcode}";
                LastScannedPriceText.Text = $"₱{product.Price:N2}";
                return;
            }
            else
            {
                // Found locally but price is 0 or missing — prompt for price
                _pendingExternalProductName = product.Name;
                _pendingExternalBarcode = displayBarcode;
                _pendingExternalCategory = product.Category;
                _pendingExternalBrand = null;
                _pendingExternalSource = "Local Database";

                SetStatusFoundNoPrice(product.Name, "Local Database", displayBarcode);

                LastScannedProductName.Text = product.Name;
                LastScannedBarcodeText.Text = $"Source: Local Database • Barcode: {displayBarcode}";
                LastScannedPriceText.Text = "PRICE NOT SET";
                return;
            }
        }

        // 2. Not found locally — search external worldwide & Philippine barcode databases
        if (_isLookingUp) return; // Prevent overlapping lookups
        _isLookingUp = true;

        SetStatusSearching(canonicalBarcode);

        try
        {
            var result = await BarcodeLookupService.LookupAsync(canonicalBarcode);

            if (result != null)
            {
                var matchedBarcode = !string.IsNullOrWhiteSpace(result.Barcode) ? result.Barcode : canonicalBarcode;

                // Check if reliable external price exists
                if (result.Price.HasValue && result.Price.Value > 0)
                {
                    var externalProduct = new Product
                    {
                        Name = result.ProductName,
                        Price = result.Price.Value,
                        Category = result.Category ?? "General",
                        Store = "General",
                        Unit = "pc",
                        Barcode = matchedBarcode,
                        IsActive = true
                    };

                    SaveOrUpdateProductInDb(externalProduct);
                    _lastScannedProduct = externalProduct;

                    if (_buyingListMode)
                    {
                        _onBarcodeScannedExtended?.Invoke(matchedBarcode, externalProduct.Name, externalProduct.Price);
                        _onBarcodeScanned?.Invoke(matchedBarcode);
                    }
                    else
                    {
                        _onProductScanned(externalProduct);
                    }

                    _scannedCount++;
                    _scannedTotal += externalProduct.Price;

                    SetStatusFoundWithPrice(result.ProductName, result.Source, matchedBarcode, externalProduct.Price);

                    SessionStatsLabel.Text = $"{_scannedCount} item(s) scanned (₱{_scannedTotal:N2})";
                    LastScannedProductName.Text = result.ProductName;
                    LastScannedBarcodeText.Text = $"Source: {result.Source} • Barcode: {matchedBarcode}";
                    LastScannedPriceText.Text = $"₱{externalProduct.Price:N2}";
                }
                else
                {
                    // Found name worldwide/PH but price unavailable — prompt user to put price!
                    _pendingExternalProductName = result.ProductName;
                    _pendingExternalBarcode = matchedBarcode;
                    _pendingExternalBrand = result.Brand;
                    _pendingExternalCategory = result.Category;
                    _pendingExternalSource = result.Source;

                    SetStatusFoundNoPrice(result.ProductName, result.Source, matchedBarcode);

                    LastScannedProductName.Text = result.ProductName;
                    LastScannedBarcodeText.Text = $"Source: {result.Source} • Barcode: {matchedBarcode}";
                    LastScannedPriceText.Text = "PRICE NOT FOUND (PLEASE ENTER PRICE)";
                }
            }
            else
            {
                // Not found anywhere — prompt to quick register product name & price
                SetStatusNotFound(canonicalBarcode);
            }
        }
        catch
        {
            SetStatusNotFound(canonicalBarcode);
        }
        finally
        {
            _isLookingUp = false;
        }
    }

    private Product? FindProductByBarcode(string barcode)
    {
        var candidates = BarcodeNormalizer.GetNormalizedCandidates(barcode);

        foreach (var candidate in candidates)
        {
            var prod = _db.Products.FirstOrDefault(p => p.IsActive && p.Barcode == candidate);
            if (prod != null) return prod;

            if (int.TryParse(candidate, out int id))
            {
                prod = _db.Products.FirstOrDefault(p => p.IsActive && p.Id == id);
                if (prod != null) return prod;
            }
        }

        return null;
    }

    private void SaveOrUpdateProductInDb(Product product)
    {
        try
        {
            var existing = _db.Products.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(product.Barcode) && p.Barcode == product.Barcode) ||
                (p.Name != null && p.Name.ToLower() == product.Name.ToLower()));

            if (existing != null)
            {
                if (product.Price > 0) existing.Price = product.Price;
                if (string.IsNullOrWhiteSpace(existing.Barcode) && !string.IsNullOrWhiteSpace(product.Barcode))
                {
                    existing.Barcode = product.Barcode;
                }
                existing.IsActive = true;
            }
            else
            {
                _db.Products.Add(product);
            }
            _db.SaveChanges();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving product to DB: {ex.Message}");
        }
    }

    private void PlayScanBeep()
    {
        try
        {
            // High-pitched, crisp confirmation beep (similar to standard retail POS scanners)
            Console.Beep(2100, 110);
        }
        catch
        {
            try
            {
                System.Media.SystemSounds.Beep.Play();
            }
            catch { }
        }
    }

    // ── Status Banner Presentation ───────────────────────────────────────────

    private void SetStatusNormal(string title, string sub)
    {
        StatusCard.Background = new SolidColorBrush(Color.FromRgb(241, 237, 250));
        StatusCard.BorderBrush = new SolidColorBrush(Color.FromRgb(229, 225, 240));
        StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.Camera;
        StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(69, 43, 134));
        StatusTitleText.Text = title;
        StatusTitleText.Foreground = new SolidColorBrush(Color.FromRgb(31, 27, 45));
        StatusSubtext.Text = sub;
        StatusSubtext.Foreground = new SolidColorBrush(Color.FromRgb(113, 107, 128));
        DismissAlertBtn.Visibility = Visibility.Collapsed;
        if (AddCustomBtn != null) AddCustomBtn.Visibility = Visibility.Collapsed;
        if (EditFoundPriceBtn != null) EditFoundPriceBtn.Visibility = Visibility.Collapsed;
        if (QuickRegisterPanel != null) QuickRegisterPanel.Visibility = Visibility.Collapsed;
        ManualPricePanel.Visibility = Visibility.Collapsed;
    }

    private void SetStatusSuccess(string title, string sub)
    {
        StatusCard.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231)); // Emerald-100
        StatusCard.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172)); // Emerald-300
        StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
        StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));   // Emerald-600
        StatusTitleText.Text = title;
        StatusTitleText.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
        StatusSubtext.Text = sub;
        StatusSubtext.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
        DismissAlertBtn.Visibility = Visibility.Collapsed;
        if (AddCustomBtn != null) AddCustomBtn.Visibility = Visibility.Collapsed;
        if (QuickRegisterPanel != null) QuickRegisterPanel.Visibility = Visibility.Collapsed;
        ManualPricePanel.Visibility = Visibility.Collapsed;
    }

    private void SetStatusSearching(string barcode)
    {
        StatusCard.Background = new SolidColorBrush(Color.FromRgb(219, 234, 254)); // Blue-100
        StatusCard.BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253)); // Blue-300
        StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CloudSearch;
        StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));   // Blue-600
        StatusTitleText.Text = "Looking up product...";
        StatusTitleText.Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175));
        StatusSubtext.Text = $"Searching databases for barcode: {barcode}";
        StatusSubtext.Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175));
        DismissAlertBtn.Visibility = Visibility.Collapsed;
        if (AddCustomBtn != null) AddCustomBtn.Visibility = Visibility.Collapsed;
        if (EditFoundPriceBtn != null) EditFoundPriceBtn.Visibility = Visibility.Collapsed;
        if (QuickRegisterPanel != null) QuickRegisterPanel.Visibility = Visibility.Collapsed;
        ManualPricePanel.Visibility = Visibility.Collapsed;
    }

    private void SetStatusFoundWithPrice(string productName, string source, string barcode, decimal price)
    {
        StatusCard.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231)); // Emerald-100
        StatusCard.BorderBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172)); // Emerald-300
        StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircle;
        StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));   // Emerald-600
        StatusTitleText.Text = $"FOUND: {productName}";
        StatusTitleText.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
        StatusSubtext.Text = $"Source: {source}\nBarcode: {barcode}\n\nPRICE: ₱{price:N2}";
        StatusSubtext.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
        DismissAlertBtn.Visibility = Visibility.Collapsed;
        if (AddCustomBtn != null) AddCustomBtn.Visibility = Visibility.Collapsed;
        if (EditFoundPriceBtn != null) EditFoundPriceBtn.Visibility = Visibility.Visible;
        if (QuickRegisterPanel != null) QuickRegisterPanel.Visibility = Visibility.Collapsed;
        ManualPricePanel.Visibility = Visibility.Collapsed;
    }

    private void SetStatusFoundNoPrice(string productName, string source, string barcode)
    {
        StatusCard.Background = new SolidColorBrush(Color.FromRgb(254, 249, 195)); // Amber-100
        StatusCard.BorderBrush = new SolidColorBrush(Color.FromRgb(253, 230, 138)); // Amber-300
        StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.TagOutline;
        StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 6));   // Amber-600
        StatusTitleText.Text = $"FOUND: {productName}";
        StatusTitleText.Foreground = new SolidColorBrush(Color.FromRgb(146, 64, 14));
        StatusSubtext.Text = $"Source: {source}\nBarcode: {barcode}\n\nPRICE NOT FOUND — PLEASE ENTER PRICE BELOW";
        StatusSubtext.Foreground = new SolidColorBrush(Color.FromRgb(146, 64, 14));
        DismissAlertBtn.Visibility = Visibility.Visible;
        if (AddCustomBtn != null) AddCustomBtn.Visibility = Visibility.Collapsed;
        if (EditFoundPriceBtn != null) EditFoundPriceBtn.Visibility = Visibility.Collapsed;
        if (QuickRegisterPanel != null) QuickRegisterPanel.Visibility = Visibility.Collapsed;

        // Show manual price entry
        ManualPriceProductLabel.Text = $"Enter price for: {productName}";
        ManualPriceInput.Text = "";
        ManualPricePanel.Visibility = Visibility.Visible;
        ManualPriceInput.Focus();
    }

    private void SetStatusNotFound(string barcode)
    {
        StatusCard.Background = new SolidColorBrush(Color.FromRgb(254, 226, 226)); // Red-100
        StatusCard.BorderBrush = new SolidColorBrush(Color.FromRgb(252, 165, 165)); // Red-300
        StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.AlertCircleOutline;
        StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38));   // Red-600
        StatusTitleText.Text = "PRODUCT NOT FOUND";
        StatusTitleText.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
        StatusSubtext.Text = $"Barcode: {barcode}\nEnter name and price below to save and add immediately.";
        StatusSubtext.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
        DismissAlertBtn.Visibility = Visibility.Visible;
        if (AddCustomBtn != null) AddCustomBtn.Visibility = Visibility.Visible;
        if (EditFoundPriceBtn != null) EditFoundPriceBtn.Visibility = Visibility.Collapsed;
        ManualPricePanel.Visibility = Visibility.Collapsed;

        if (QuickRegisterPanel != null)
        {
            QuickRegisterBarcodeLabel.Text = $"New Barcode: {barcode} • Enter product details:";
            QuickRegisterNameInput.Text = "";
            QuickRegisterPriceInput.Text = "";
            QuickRegisterPanel.Visibility = Visibility.Visible;
            QuickRegisterNameInput.Focus();
        }

        LastScannedProductName.Text = "PRODUCT NOT FOUND";
        LastScannedBarcodeText.Text = $"Barcode: {barcode}";
        LastScannedPriceText.Text = "";
    }

    private void ShowNoCameraError(string msg)
    {
        NoCameraPanel.Visibility = Visibility.Visible;
        AimingOverlay.Visibility = Visibility.Collapsed;
        NoCameraMessage.Text = msg;
        SetStatusNormal("Camera inactive", "You can still look up products with the manual barcode input above");
    }

    // ── UI Events ────────────────────────────────────────────────────────────

    private void OnEditFoundPriceClick(object sender, RoutedEventArgs e)
    {
        if (_lastScannedProduct == null) return;

        _pendingExternalProductName = _lastScannedProduct.Name;
        _pendingExternalBarcode = _lastScannedProduct.Barcode;
        _pendingExternalCategory = _lastScannedProduct.Category;
        _pendingExternalSource = "Price Override";

        ManualPriceProductLabel.Text = $"Update price for: {_lastScannedProduct.Name}";
        ManualPriceInput.Text = _lastScannedProduct.Price > 0 ? _lastScannedProduct.Price.ToString("0.##") : "";
        ManualPricePanel.Visibility = Visibility.Visible;
        if (QuickRegisterPanel != null) QuickRegisterPanel.Visibility = Visibility.Collapsed;
        ManualPriceInput.SelectAll();
        ManualPriceInput.Focus();
    }

    private void OnAddCustomFromScanClick(object sender, RoutedEventArgs e)
    {
        var stores = _db.Stores.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => s.Name).ToList();
        var dlg = new AddItemDialog(stores) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result is not null)
        {
            var p = new Product
            {
                Name = dlg.Result.Name,
                Price = dlg.Result.Price,
                Unit = dlg.Result.Unit,
                Store = dlg.Result.Store,
                Barcode = _lastScannedBarcode,
                IsActive = true
            };
            SaveOrUpdateProductInDb(p);
            _lastScannedProduct = p;

            if (_buyingListMode)
            {
                _onBarcodeScannedExtended?.Invoke(p.Barcode ?? _lastScannedBarcode, p.Name, p.Price);
                _onBarcodeScanned?.Invoke(p.Barcode ?? _lastScannedBarcode);
            }
            else
            {
                _onProductScanned?.Invoke(p);
            }

            _scannedCount++;
            _scannedTotal += p.Price;
            SessionStatsLabel.Text = $"{_scannedCount} item{(_scannedCount != 1 ? "s" : "")} scanned (₱{_scannedTotal:N2})";
            SetStatusSuccess($"Added: {p.Name}", $"Added custom item to order. Ready for next scan.");
        }
    }

    private void OnDismissAlertClick(object sender, RoutedEventArgs e)
    {
        ManualPricePanel.Visibility = Visibility.Collapsed;
        if (QuickRegisterPanel != null) QuickRegisterPanel.Visibility = Visibility.Collapsed;
        if (EditFoundPriceBtn != null) EditFoundPriceBtn.Visibility = Visibility.Collapsed;
        if (AddCustomBtn != null) AddCustomBtn.Visibility = Visibility.Collapsed;
        _pendingExternalProductName = null;
        _pendingExternalBarcode = null;
        _pendingExternalBrand = null;
        _pendingExternalCategory = null;
        _pendingExternalSource = null;
        SetStatusNormal("Camera active — ready to scan", "Position the barcode towards your laptop camera");
    }

    private async void OnCameraSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            await StartSelectedCameraAsync();
        }
    }

    private async void OnRefreshCamerasClick(object sender, RoutedEventArgs e)
    {
        await InitializeCameraAsync();
    }

    private void OnManualBarcodeSubmit(object sender, RoutedEventArgs e)
    {
        var text = ManualBarcodeInput.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            HandleDetectedBarcode(text);
            ManualBarcodeInput.Text = "";
        }
    }

    private void OnManualBarcodeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnManualBarcodeSubmit(sender, e);
            e.Handled = true;
        }
    }

    // ── Manual Price Entry Events ─────────────────────────────────────────────

    private void OnManualPriceSubmit(object sender, RoutedEventArgs e)
    {
        SubmitManualPrice();
    }

    private void OnManualPriceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitManualPrice();
            e.Handled = true;
        }
    }

    private void SubmitManualPrice()
    {
        if (string.IsNullOrWhiteSpace(_pendingExternalProductName))
            return;

        var priceText = ManualPriceInput.Text?.Trim();
        if (string.IsNullOrEmpty(priceText) || !decimal.TryParse(priceText, out decimal price) || price <= 0)
        {
            MessageBox.Show("Please enter a valid price (e.g. 100 or 49.50).",
                "Invalid Price", MessageBoxButton.OK, MessageBoxImage.Warning);
            ManualPriceInput.Focus();
            return;
        }

        var source = _pendingExternalSource ?? "Manual Entry";
        var barcode = _pendingExternalBarcode ?? "";

        var product = new Product
        {
            Name = _pendingExternalProductName,
            Price = price,
            Category = _pendingExternalCategory ?? "General",
            Store = "General",
            Unit = "pc",
            Barcode = barcode,
            IsActive = true
        };

        SaveOrUpdateProductInDb(product);
        _lastScannedProduct = product;
        if (EditFoundPriceBtn != null) EditFoundPriceBtn.Visibility = Visibility.Visible;

        // Cache the product with this entered price for repeat scans in this session
        if (!string.IsNullOrWhiteSpace(barcode))
        {
            BarcodeLookupService.CacheResult(barcode, new BarcodeLookupService.LookupResult
            {
                ProductName = _pendingExternalProductName,
                Price = price,
                Brand = _pendingExternalBrand,
                Category = _pendingExternalCategory,
                Source = source,
                Barcode = barcode
            });
        }

        if (_buyingListMode)
        {
            _onBarcodeScannedExtended?.Invoke(barcode, product.Name, product.Price);
            _onBarcodeScanned?.Invoke(barcode);
        }
        else
        {
            _onProductScanned(product);
        }

        _scannedCount++;
        _scannedTotal += price;

        PlayScanBeep();
        SetStatusFoundWithPrice(_pendingExternalProductName, source, barcode, price);

        SessionStatsLabel.Text = $"{_scannedCount} item(s) scanned (₱{_scannedTotal:N2})";
        LastScannedProductName.Text = _pendingExternalProductName;
        LastScannedBarcodeText.Text = $"Source: {source} • Barcode: {barcode}";
        LastScannedPriceText.Text = $"₱{price:N2}";

        ManualPricePanel.Visibility = Visibility.Collapsed;

        // Clear pending state
        _pendingExternalProductName = null;
        _pendingExternalBarcode = null;
        _pendingExternalBrand = null;
        _pendingExternalCategory = null;
        _pendingExternalSource = null;
    }

    // ── Quick Register Unknown Product Events ────────────────────────────────

    private void OnQuickRegisterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitQuickRegister();
            e.Handled = true;
        }
    }

    private void OnQuickRegisterSubmit(object sender, RoutedEventArgs e)
    {
        SubmitQuickRegister();
    }

    private void SubmitQuickRegister()
    {
        var name = QuickRegisterNameInput.Text?.Trim();
        var priceText = QuickRegisterPriceInput.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter a product name.", "Missing Name", MessageBoxButton.OK, MessageBoxImage.Warning);
            QuickRegisterNameInput.Focus();
            return;
        }

        if (string.IsNullOrEmpty(priceText) || !decimal.TryParse(priceText, out decimal price) || price < 0)
        {
            MessageBox.Show("Please enter a valid price (e.g. 25.00).", "Invalid Price", MessageBoxButton.OK, MessageBoxImage.Warning);
            QuickRegisterPriceInput.Focus();
            return;
        }

        var barcode = _lastScannedBarcode.Trim();

        var product = new Product
        {
            Name = name,
            Price = price,
            Category = "General",
            Store = "General",
            Unit = "pc",
            Barcode = barcode,
            IsActive = true
        };

        SaveOrUpdateProductInDb(product);
        _lastScannedProduct = product;
        if (EditFoundPriceBtn != null) EditFoundPriceBtn.Visibility = Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(barcode))
        {
            BarcodeLookupService.CacheResult(barcode, new BarcodeLookupService.LookupResult
            {
                ProductName = name,
                Price = price,
                Source = "Quick Register",
                Barcode = barcode
            });
        }

        if (_buyingListMode)
        {
            _onBarcodeScannedExtended?.Invoke(barcode, product.Name, product.Price);
            _onBarcodeScanned?.Invoke(barcode);
        }
        else
        {
            _onProductScanned(product);
        }

        _scannedCount++;
        _scannedTotal += price;

        PlayScanBeep();
        if (QuickRegisterPanel != null) QuickRegisterPanel.Visibility = Visibility.Collapsed;
        SetStatusSuccess($"Added: {name}", $"Registered to barcode {barcode} • ₱{price:N2}");

        SessionStatsLabel.Text = $"{_scannedCount} item(s) scanned (₱{_scannedTotal:N2})";
        LastScannedProductName.Text = name;
        LastScannedBarcodeText.Text = $"Source: Quick Register • Barcode: {barcode}";
        LastScannedPriceText.Text = $"₱{price:N2}";
    }

    // ── Window Events ────────────────────────────────────────────────────────

    public void OnWindowPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCharTime).TotalMilliseconds > 150)
        {
            _scannerBuffer.Clear();
        }
        _lastCharTime = now;
        _scannerBuffer.Append(e.Text);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        // Hardware USB Barcode Scanner Gun Support (burst typing ending with Enter)
        if (e.Key == Key.Enter && _scannerBuffer.Length >= 3)
        {
            var scannedCode = _scannerBuffer.ToString().Trim();
            _scannerBuffer.Clear();
            HandleDetectedBarcode(scannedCode);
            e.Handled = true;
        }
    }

    private async void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _isClosing = true;
        await StopCameraAsync();
        Close();
    }
}
