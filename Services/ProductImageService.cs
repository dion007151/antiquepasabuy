using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Offline-first product image management service.
/// Stores images locally in the ProductImages/ folder, prevents file locking via OnLoad caching,
/// safely handles missing/corrupt image files, and caches decoded bitmaps for rapid UI rendering.
/// </summary>
public static class ProductImageService
{
    public static readonly string ImageFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProductImages");

    private static readonly Dictionary<string, ImageSource?> MemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> NotFoundCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    static ProductImageService()
    {
        try
        {
            if (!Directory.Exists(ImageFolder))
            {
                Directory.CreateDirectory(ImageFolder);
            }
        }
        catch { }
    }

    public static void ClearCacheForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (CacheLock)
        {
            var p = path.Trim();
            var keysToRemove = MemoryCache.Keys.Where(k => k.StartsWith(p, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in keysToRemove) MemoryCache.Remove(k);
            var notFoundToRemove = NotFoundCache.Where(k => k.StartsWith(p, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in notFoundToRemove) NotFoundCache.Remove(k);
        }
    }

    /// <summary>
    /// Safely copies a user-selected image file into the local ProductImages/ directory with a unique name.
    /// Returns the relative filename (e.g. "ProductImages/prod_12_20260910_a1b2c3.png").
    /// </summary>
    public static string? SaveProductImage(string sourceFilePath, int? productId = null)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            return null;

        try
        {
            if (!Directory.Exists(ImageFolder))
                Directory.CreateDirectory(ImageFolder);

            var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

            var idPart = productId.HasValue && productId.Value > 0 ? productId.Value.ToString() : "temp";
            var timePart = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var randPart = Guid.NewGuid().ToString("N")[..6];
            var fileName = $"prod_{idPart}_{timePart}_{randPart}{ext}";

            var destinationPath = Path.Combine(ImageFolder, fileName);
            File.Copy(sourceFilePath, destinationPath, overwrite: true);

            var relativePath = Path.Combine("ProductImages", fileName);
            ClearCacheForPath(relativePath);

            return relativePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save product image: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolves any relative or absolute image path to an existing absolute file path.
    /// Returns null if file does not exist.
    /// </summary>
    public static string? ResolveImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            if (Path.IsPathRooted(path) && File.Exists(path))
                return path;

            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            if (File.Exists(fullPath))
                return fullPath;

            var inFolder = Path.Combine(ImageFolder, Path.GetFileName(path));
            if (File.Exists(inFolder))
                return inFolder;
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Loads a local image as an un-locked, frozen WPF BitmapImage with optional thumbnail decoding.
    /// Returns null safely if the image cannot be found or is corrupted (never crashes).
    /// </summary>
    public static ImageSource? LoadBitmap(string? imagePath, int decodePixelWidth = 140)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;

        var key = $"{imagePath.Trim()}__w{decodePixelWidth}";

        lock (CacheLock)
        {
            if (MemoryCache.TryGetValue(key, out var cached))
                return cached;
            if (NotFoundCache.Contains(key))
                return null;
        }

        var absPath = ResolveImagePath(imagePath);
        if (absPath == null)
        {
            lock (CacheLock)
            {
                NotFoundCache.Add(key);
            }
            return null;
        }

        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad; // Do not hold file locks
            bi.UriSource = new Uri(absPath, UriKind.Absolute);
            if (decodePixelWidth > 0)
            {
                bi.DecodePixelWidth = decodePixelWidth;
            }
            bi.EndInit();
            bi.Freeze();

            lock (CacheLock)
            {
                MemoryCache[key] = bi;
            }

            return bi;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load image '{imagePath}': {ex.Message}");
            lock (CacheLock)
            {
                NotFoundCache.Add(key);
            }
            return null;
        }
    }

    /// <summary>
    /// Safely deletes a previously saved product image from the ProductImages folder.
    /// Does not delete files outside the ProductImages folder.
    /// </summary>
    public static void DeleteProductImage(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return;

        try
        {
            ClearCacheForPath(imagePath);

            var absPath = ResolveImagePath(imagePath);
            if (absPath != null && absPath.StartsWith(ImageFolder, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(absPath))
                {
                    File.Delete(absPath);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not delete image '{imagePath}': {ex.Message}");
        }
    }
}
