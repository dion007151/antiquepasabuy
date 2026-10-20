using System.Windows.Media;

namespace AntiquePasabuyPOS.Models;

public class PosColorTheme
{
    public string Name { get; }
    public SolidColorBrush SurfaceBrush { get; }
    public SolidColorBrush DepthBrush { get; }
    public SolidColorBrush BorderBrush { get; }

    public PosColorTheme(string name, string surfaceHex, string depthHex, string borderHex)
    {
        Name = name;
        SurfaceBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(surfaceHex));
        SurfaceBrush.Freeze();
        DepthBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(depthHex));
        DepthBrush.Freeze();
        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderHex));
        BorderBrush.Freeze();
    }
}

public static class PosColorPalette
{
    // Curated Enterprise Blue Tonal Palette: professional, calm, unified corporate POS aesthetic
    public static readonly PosColorTheme[] Themes =
    [
        new PosColorTheme("Primary Enterprise Blue", "#003591", "#001F58", "#2563EB"),
        new PosColorTheme("Royal Blue",             "#1D4ED8", "#172554", "#3B82F6"),
        new PosColorTheme("Deep Navy Blue",         "#0F294A", "#081628", "#1E3A5F"),
        new PosColorTheme("Slate Blue",             "#2B4C7E", "#1A3358", "#4069A5"),
        new PosColorTheme("Steel Blue",             "#1E40AF", "#172554", "#60A5FA"),
        new PosColorTheme("Teal Blue",              "#0B69A3", "#06456D", "#149ECE"),
        new PosColorTheme("Blue-Gray / Dark Slate",  "#334E68", "#1E2D3D", "#486581"),
        new PosColorTheme("Soft Indigo Blue",       "#314375", "#1E2B4D", "#4D649F")
    ];

    /// <summary>
    /// Computes a deterministic color theme for a product based on its ID and name.
    /// The same product will always receive the exact same color across application restarts.
    /// </summary>
    public static PosColorTheme GetDeterministicTheme(int productId, string productName)
    {
        uint hash = 2166136261;
        if (productId > 0)
        {
            hash = (hash ^ (uint)productId) * 16777619;
        }

        if (!string.IsNullOrEmpty(productName))
        {
            foreach (char c in productName)
            {
                hash = (hash ^ (uint)char.ToLowerInvariant(c)) * 16777619;
            }
        }

        int index = (int)(hash % (uint)Themes.Length);
        return Themes[index];
    }
}
