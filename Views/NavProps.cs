using System.Windows;

namespace AntiquePasabuyPOS.Views;

public static class NavProps
{
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsActive",
            typeof(bool),
            typeof(NavProps),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static void SetIsActive(UIElement element, bool value) => element.SetValue(IsActiveProperty, value);
    public static bool GetIsActive(UIElement element) => (bool)element.GetValue(IsActiveProperty);
}
