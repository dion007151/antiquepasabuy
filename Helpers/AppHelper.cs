namespace AntiquePasabuyPOS.Helpers;

public static class AppHelper
{
    public static string GetGreeting()
    {
        var h = DateTime.Now.Hour;
        return h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
    }

    public static string FormatCurrency(decimal amount) => $"₱{amount:N2}";
}
