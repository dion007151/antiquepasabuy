using System.IO;
using System.Printing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Services;

public static class PrinterService
{
    #region WinSpool RAW Printing P/Invoke
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName = "POS Receipt";
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile = null;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType = "RAW";
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static bool SendRawBytesToPrinter(string szPrinterName, byte[] bytes)
    {
        try
        {
            var pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);
            var di = new DOCINFOA { pDocName = "Antique Pasabuy POS Receipt", pDataType = "RAW" };
            var success = false;
            if (OpenPrinter(szPrinterName, out var hPrinter, IntPtr.Zero))
            {
                if (StartDocPrinter(hPrinter, 1, di))
                {
                    if (StartPagePrinter(hPrinter))
                    {
                        success = WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out _);
                        EndPagePrinter(hPrinter);
                    }
                    EndDocPrinter(hPrinter);
                }
                ClosePrinter(hPrinter);
            }
            Marshal.FreeCoTaskMem(pUnmanagedBytes);
            return success;
        }
        catch { return false; }
    }
    #endregion

    /// <summary>
    /// Enumerates all installed printers on the system (USB, Network, Thermal, Virtual, PDF).
    /// Returns the list of names and the detected default printer.
    /// </summary>
    public static (List<string> printers, string defaultPrinter) GetInstalledPrinters()
    {
        var printers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultPrinter = string.Empty;

        // 1. Native WPF LocalPrintServer
        try
        {
            var server = new LocalPrintServer();
            var queues = server.GetPrintQueues(new[] {
                EnumeratedPrintQueueTypes.Local,
                EnumeratedPrintQueueTypes.Connections
            });
            foreach (var pq in queues)
            {
                var name = !string.IsNullOrWhiteSpace(pq.FullName) ? pq.FullName : pq.Name;
                if (!string.IsNullOrWhiteSpace(name)) printers.Add(name);
            }

            try
            {
                var defQueue = LocalPrintServer.GetDefaultPrintQueue();
                if (defQueue != null)
                {
                    defaultPrinter = !string.IsNullOrWhiteSpace(defQueue.FullName)
                        ? defQueue.FullName
                        : defQueue.Name;
                }
            }
            catch { }
        }
        catch { }

        // 2. Windows Registry - CurrentUser Devices
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Devices");
            if (key is not null)
            {
                foreach (var name in key.GetValueNames())
                {
                    if (!string.IsNullOrWhiteSpace(name)) printers.Add(name);
                }
            }
        }
        catch { }

        // 3. Default printer from registry if not already detected
        if (string.IsNullOrEmpty(defaultPrinter))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Windows");
                var device = key?.GetValue("Device")?.ToString() ?? "";
                var part = device.Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(part)) defaultPrinter = part;
            }
            catch { }
        }

        // 4. PowerShell fallback if list is still empty
        if (printers.Count == 0)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("powershell",
                    "-NoProfile -Command \"Get-Printer | Select-Object -ExpandProperty Name\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(4000);
                    foreach (var line in output.Split('\n'))
                    {
                        var name = line.Trim();
                        if (!string.IsNullOrWhiteSpace(name)) printers.Add(name);
                    }
                }
            }
            catch { }
        }

        // 5. Always guarantee at least PDF printer option
        if (printers.Count == 0)
        {
            printers.Add("Microsoft Print to PDF");
            defaultPrinter = "Microsoft Print to PDF";
        }

        // Put physical/hardware printers first, virtual/PDF printers last
        var physicalPrinters = printers.Where(p => !IsVirtualOrPdfPrinter(p)).OrderBy(x => x).ToList();
        var virtualPrinters = printers.Where(p => IsVirtualOrPdfPrinter(p)).OrderBy(x => x).ToList();

        // If detected defaultPrinter is empty or a virtual/PDF printer, but physical printers exist,
        // automatically prefer the physical printer as default!
        if (physicalPrinters.Count > 0 && (string.IsNullOrEmpty(defaultPrinter) || IsVirtualOrPdfPrinter(defaultPrinter)))
        {
            defaultPrinter = physicalPrinters[0];
        }

        var list = new List<string>();
        list.AddRange(physicalPrinters);
        list.AddRange(virtualPrinters);

        if (string.IsNullOrEmpty(defaultPrinter) && list.Count > 0)
            defaultPrinter = list[0];

        // Put default printer at index 0 for convenience
        if (!string.IsNullOrEmpty(defaultPrinter) && list.Contains(defaultPrinter))
        {
            list.Remove(defaultPrinter);
            list.Insert(0, defaultPrinter);
        }

        return (list, defaultPrinter);
    }

    /// <summary>
    /// Checks if a printer is a virtual document exporter (PDF, XPS, OneNote, Fax) rather than a physical paper printer.
    /// </summary>
    public static bool IsVirtualOrPdfPrinter(string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return false;
        var lower = printerName.ToLowerInvariant();
        return lower.Contains("pdf") ||
               lower.Contains("xps") ||
               lower.Contains("onenote") ||
               lower.Contains("fax") ||
               lower.Contains("send to") ||
               lower.Contains("writer") ||
               lower.Contains("document");
    }

    /// <summary>
    /// Detects if a printer is a thermal receipt/roll printer based on its name and driver characteristics.
    /// Standard inkjet/laser printers (like Epson L120, Canon G-series, HP DeskJet) return false.
    /// </summary>
    public static bool IsThermalPrinter(string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return false;
        var lower = printerName.ToLowerInvariant();

        // Explicit desktop inkjets/lasers are never thermal
        if (lower.Contains("l120") || lower.Contains("l3110") || lower.Contains("l3210") || lower.Contains("l1110") ||
            lower.Contains("deskjet") || lower.Contains("laserjet") || lower.Contains("pixma") || lower.Contains("ecotank"))
            return false;

        return lower.Contains("thermal") ||
               lower.Contains("pos") ||
               lower.Contains("receipt") ||
               lower.Contains("58mm") ||
               lower.Contains("80mm") ||
               lower.Contains("rp-") ||
               lower.Contains("xp-") ||
               lower.Contains("tm-t") ||
               lower.Contains("tm-m") ||
               lower.Contains("xprinter") ||
               lower.Contains("zijiang") ||
               lower.Contains("munbyn") ||
               lower.Contains("rongta") ||
               lower.Contains("gprinter") ||
               lower.Contains("hprt") ||
               lower.Contains("star micronics");
    }

    /// <summary>
    /// Inspects the Windows Print Spooler queue for a printer to detect stuck jobs or error states.
    /// </summary>
    public static (int jobCount, string statusSummary) GetPrinterQueueStatus(string? targetPrinter)
    {
        try
        {
            var queue = ResolvePrintQueue(targetPrinter);
            if (queue == null) return (0, "Printer not found / Offline");
            queue.Refresh();

            var count = queue.NumberOfJobs;
            if (queue.IsOffline) return (count, "Printer is Offline");
            if (queue.IsInError) return (count, "Printer in Error state");
            if (queue.IsPaperJammed) return (count, "Paper Jam");
            if (queue.HasPaperProblem) return (count, "Paper Out / Paper Feed Error");
            if (count > 0) return (count, $"{count} job(s) pending in queue");
            return (0, "Ready (Queue empty)");
        }
        catch (Exception ex)
        {
            return (0, $"Status: {ex.Message}");
        }
    }

    /// <summary>
    /// Purges all pending and stuck jobs from a printer's spooler queue to prevent reprinting old receipts.
    /// </summary>
    public static (bool success, string message) ClearPrinterQueue(string? targetPrinter)
    {
        try
        {
            var queue = ResolvePrintQueue(targetPrinter);
            if (queue == null) return (false, "Printer queue could not be resolved.");
            queue.Refresh();

            int cancelled = 0;
            try
            {
                var jobs = queue.GetPrintJobInfoCollection();
                foreach (var job in jobs)
                {
                    try
                    {
                        job.Cancel();
                        cancelled++;
                    }
                    catch { }
                }
            }
            catch { }

            try
            {
                queue.Purge();
            }
            catch { }

            queue.Refresh();
            return (true, $"Cleared print queue for '{queue.Name}'. Stuck jobs removed.");
        }
        catch (Exception ex)
        {
            return (false, $"Error clearing queue: {ex.Message}");
        }
    }

    /// <summary>
    /// Intelligently resolves a System.Printing.PrintQueue for the given printer name or finds the best physical printer available.
    /// Prevents silent fallback to "Microsoft Print to PDF".
    /// </summary>
    public static PrintQueue? ResolvePrintQueue(string? targetPrinter)
    {
        LocalPrintServer? server = null;
        try { server = new LocalPrintServer(); } catch { return null; }

        // Strategy 1: Exact match with targetPrinter
        if (!string.IsNullOrWhiteSpace(targetPrinter))
        {
            try
            {
                var q = server.GetPrintQueue(targetPrinter);
                if (q != null) return q;
            }
            catch { }
        }

        // Retrieve all available print queues
        List<PrintQueue> allQueues = new();
        try
        {
            allQueues = server.GetPrintQueues(new[]
            {
                EnumeratedPrintQueueTypes.Local,
                EnumeratedPrintQueueTypes.Connections
            }).ToList();
        }
        catch { }

        if (allQueues.Count == 0)
        {
            try
            {
                var def = LocalPrintServer.GetDefaultPrintQueue();
                if (def != null) return def;
            }
            catch { }
            return null;
        }

        // Strategy 2: Match targetPrinter by Name or FullName (case-insensitive)
        if (!string.IsNullOrWhiteSpace(targetPrinter))
        {
            var match = allQueues.FirstOrDefault(q =>
                string.Equals(q.Name, targetPrinter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(q.FullName, targetPrinter, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // Strategy 3: Partial match (handles "\\server\printer" UNC or partial driver names)
            match = allQueues.FirstOrDefault(q =>
                (q.Name ?? "").Contains(targetPrinter, StringComparison.OrdinalIgnoreCase) ||
                (q.FullName ?? "").Contains(targetPrinter, StringComparison.OrdinalIgnoreCase) ||
                targetPrinter.Contains(q.Name ?? "", StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        // Strategy 4: If targetPrinter is empty or not matched, prefer any real physical printer
        var physical = allQueues.FirstOrDefault(q =>
            !IsVirtualOrPdfPrinter(q.Name) && !IsVirtualOrPdfPrinter(q.FullName));
        if (physical != null) return physical;

        // Strategy 5: System default print queue
        try
        {
            var def = LocalPrintServer.GetDefaultPrintQueue();
            if (def != null) return def;
        }
        catch { }

        // Strategy 6: First available queue
        return allQueues.FirstOrDefault();
    }

    #region Format Helpers
    /// <summary>
    /// Builds a centered logo block for receipt headers.
    /// Loads the Antique Pasabuy logo from the embedded resource and scales it
    /// proportionally to fit the given content width.
    /// Returns null gracefully if the resource cannot be loaded.
    /// </summary>
    private static BlockUIContainer? BuildLogoBlock(double contentWidth, double maxHeight = 55, string? logoResource = null)
    {
        try
        {
            var logo = logoResource ?? BusinessProfileService.ActiveProfile.LogoResource;
            if (string.IsNullOrWhiteSpace(logo)) return null;

            var uri = new Uri(logo, UriKind.RelativeOrAbsolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource  = uri;
            bmp.DecodePixelHeight = (int)maxHeight * 2; // high-DPI decode
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            // Calculate proportional width
            double aspect = bmp.PixelWidth / (double)bmp.PixelHeight;
            double imgH   = maxHeight;
            double imgW   = Math.Min(contentWidth, imgH * aspect);

            var img = new Image
            {
                Source  = bmp,
                Width   = imgW,
                Height  = imgH,
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var container = new BlockUIContainer(img)
            {
                Margin = new Thickness(0, 4, 0, 2)
            };
            return container;
        }
        catch
        {
            return null; // Fail silently — receipt still prints without logo
        }
    }

    public static SolidColorBrush Hex(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(c);
    }

    public static string FormatOrderStatus(object? status)
    {
        var s = status?.ToString()?.Trim()?.ToUpperInvariant() ?? "";
        return s switch
        {
            "WAITING_CONFIRMATION" or "WAITING CONFIRMATION" or "PENDING" or "DRAFT" or "CONFIRMED" or "READY_TO_BUY" or "READY TO BUY" or "FOR_BUYING" or "FOR BUYING" or "WAITING TO BUY" => "Waiting to Buy",
            "BUYING_NOW" or "BUYING NOW" or "BUYING" or "PURCHASING"                                 => "Buying",
            "BOUGHT" or "PURCHASED"                                                                 => "Bought",
            "COMPLETED" or "COMPLETE" or "DELIVERED" or "PICKED_UP" or "ORDER FINISHED"             => "Order Finished",
            "CANCELLED" or "UNAVAILABLE"                                                            => "Cancelled",
            _                                                                                       => "Waiting to Buy"
        };
    }

    public static string FormatPaymentMethod(PaymentMethod method)
    {
        return method switch
        {
            PaymentMethod.Cash         => "CASH / ON HAND",
            PaymentMethod.GCash        => "GCASH",
            PaymentMethod.BankTransfer => "BANK TRANSFER",
            PaymentMethod.Maya         => "MAYA",
            PaymentMethod.COD          => "COD (CASH ON DELIVERY)",
            _                          => method.ToString().ToUpperInvariant()
        };
    }

    public static string FormatPaymentStatus(Order order, decimal estimatedTotal)
    {
        var status = Calc.PaymentStatus(estimatedTotal, Calc.TotalPaid(order.Payments));
        return status switch
        {
            Models.PaymentStatus.PAID    => "Paid",
            Models.PaymentStatus.PARTIAL => order.IsCOD ? "COD (Pending)" : "Partly Paid",
            _                            => order.IsCOD ? "COD (Pending)" : "Not Paid"
        };
    }

    public static string FormatFulfillmentStatus(Order order)
    {
        if (order.DeliveryType == DeliveryType.PICKUP)
        {
            return order.Status == OrderStatus.COMPLETED ? "READY FOR PICKUP" : "FOR PICKUP";
        }
        return order.Status == OrderStatus.COMPLETED ? "DELIVERED" : "FOR DELIVERY";
    }
    #endregion

    /// <summary>
    /// Formats an Order into a clean, authentic POS thermal receipt string (58mm/80mm compatible).
    /// Pure monochrome, compact, readable, no fake cards or unnecessary decorations.
    /// </summary>
    public static string GenerateReceiptText(Order order, string businessName = "DIONIX POS", string footerMsg = "Thank you for trusting DIONIX POS!", int width = 36)
    {
        var sb = new StringBuilder();
        int W = width < 32 ? 32 : width;
        string Line(char c = '-') => new string(c, W);
        string DblLine() => new string('=', W);
        string Center(string s) => s.Length >= W ? s : s.PadLeft((W + s.Length) / 2).PadRight(W);
        string TwoCol(string left, string right)
        {
            var spaces = W - left.Length - right.Length;
            return spaces > 0 ? left + new string(' ', spaces) + right : $"{left} {right}";
        }

        var profile = BusinessProfileService.GetProfileById(order.BusinessId) ?? BusinessProfileService.ActiveProfile;

        // Header Section
        sb.AppendLine(Center(profile.SystemName));
        sb.AppendLine(Center(profile.Subtitle));
        sb.AppendLine(Center("PURCHASE ORDER"));
        sb.AppendLine(DblLine());

        // Order Metadata
        sb.AppendLine(TwoCol("PO #:", order.OrderNo));
        sb.AppendLine(TwoCol("Date:", order.CreatedAt.ToString("MMM dd, yyyy  h:mm tt")));
        sb.AppendLine(TwoCol("Customer:", order.Customer?.Name ?? "Walk-in"));
        sb.AppendLine(TwoCol("Shopping Day:", order.BuyingDay));
        sb.AppendLine(Line('-'));

        // Items Section — consolidated by product name+price (no store grouping on receipt)
        var allItems = order.Items?.Where(i => !string.IsNullOrWhiteSpace(i.ProductName)).ToList() ?? new List<OrderItem>();
        // Group identical items: same product name (case-insensitive), unit, and effective price → sum quantities
        var consolidatedItems = allItems
            .GroupBy(i => (
                Name: i.ProductName.Trim(),
                Unit: (i.Unit ?? "").Trim(),
                Price: i.ActualPrice ?? i.EstimatedPrice,
                Status: i.Status,
                Source: i.Source
            ))
            .Select(g => new {
                ProductName = g.Key.Name,
                Unit = g.Key.Unit,
                Price = g.Key.Price,
                Status = g.Key.Status,
                Source = g.Key.Source,
                Qty = g.Sum(i => i.Qty),
                CostPrice = g.First().CostPrice,
                PurchaseNotes = g.Select(i => i.PurchaseNotes ?? i.Notes).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            })
            .OrderBy(i => i.ProductName)
            .ToList();

        if (W >= 38)
        {
            sb.AppendLine("QTY  ITEM                     PRICE    TOTAL");
            sb.AppendLine(Line('-'));
        }

        foreach (var item in consolidatedItems)
        {
            var total = item.Price * (decimal)item.Qty;
            var qtyStr = item.Qty % 1 == 0 ? $"{item.Qty:0}" : $"{item.Qty:0.##}";
            var isUnavailable = item.Status == ItemStatus.UNAVAILABLE;
            var isBoughtOnHand = item.Source == ItemSource.STOCK || item.Status == ItemStatus.BOUGHT_FROM_STOCK;
            var isReorder = item.Status == ItemStatus.REORDER;

            if (W >= 38)
            {
                var qCol = qtyStr.PadLeft(3);
                var prCol = $"{item.Price:N2}".PadLeft(8);
                var totCol = isUnavailable ? "   N/A  " : $"{total:N2}".PadLeft(8);
                var maxNameLen = W - 3 - 1 - 8 - 1 - 8 - 1;
                var name = item.ProductName;
                if (name.Length > maxNameLen)
                {
                    sb.AppendLine($"{qCol} {name.Substring(0, maxNameLen)} {prCol} {totCol}");
                    sb.AppendLine($"    {name.Substring(maxNameLen)}");
                }
                else
                {
                    sb.AppendLine($"{qCol} {name.PadRight(maxNameLen)} {prCol} {totCol}");
                }
            }
            else
            {
                sb.AppendLine(item.ProductName);
                if (isUnavailable)
                    sb.AppendLine(TwoCol($"  {qtyStr} x ₱{item.Price:N2}", "[N/A]"));
                else
                    sb.AppendLine(TwoCol($"  {qtyStr} x ₱{item.Price:N2}", $"₱{total:N2}"));
            }

            // Status tags
            if (isUnavailable)
                sb.AppendLine($"  [NOT AVAILABLE]");
            else if (isBoughtOnHand)
                sb.AppendLine($"  [BOUGHT ON HAND]");
            else if (isReorder)
                sb.AppendLine($"  [REORDER]");

            if (item.CostPrice.HasValue && item.CostPrice.Value > item.Price)
            {
                var unitSavings = item.CostPrice.Value - item.Price;
                sb.AppendLine($"    Orig: ₱{item.CostPrice.Value:N2} | Saved: ₱{unitSavings:N2}");
            }

            if (!string.IsNullOrWhiteSpace(item.PurchaseNotes))
            {
                sb.AppendLine($"  * {item.PurchaseNotes}");
            }
        }

        // Keep validItems for summary calculations below
        var validItems = allItems;

        if (validItems.Count == 0)
        {
            sb.AppendLine("  (No items recorded)");
        }

        // Summary Calculations (Delivery Fee removed per specification)
        decimal purchaseBudget = order.ItemsSubtotal;
        decimal pasabuyFee = order.PasabuyFee;
        decimal discount = order.Discount;
        decimal estimatedTotal = Math.Max(0m, Calc.OrderTotal(purchaseBudget, pasabuyFee) - discount);
        decimal totalPaidOnReceipt = Calc.TotalPaid(order.Payments);
        decimal balanceDueOnReceipt = Calc.BalanceDue(estimatedTotal, totalPaidOnReceipt);
        decimal overpaidOnReceipt = Calc.OverpaidAmount(estimatedTotal, totalPaidOnReceipt);

        sb.AppendLine(Line('-'));
        sb.AppendLine(TwoCol("PURCHASE BUDGET:", $"₱{purchaseBudget:N2}"));
        if (profile.EnablePasabuyFee)
        {
            sb.AppendLine(TwoCol($"{profile.PasabuyFeeLabel.ToUpper()}:", $"₱{pasabuyFee:N2}"));
        }
        if (discount > 0)
        {
            sb.AppendLine(TwoCol("DISCOUNT:", $"-₱{discount:N2}"));
        }
        var totalSavings = validItems
            .Where(i => i.CostPrice.HasValue && i.CostPrice.Value > (i.ActualPrice ?? i.EstimatedPrice))
            .Sum(i => (i.CostPrice!.Value - (i.ActualPrice ?? i.EstimatedPrice)) * (decimal)i.Qty);
        if (totalSavings > 0)
        {
            sb.AppendLine(TwoCol("TOTAL SAVED:", $"₱{totalSavings:N2}"));
        }
        sb.AppendLine(Line('-'));
        sb.AppendLine(TwoCol("ESTIMATED TOTAL:", $"₱{estimatedTotal:N2}"));
        sb.AppendLine(DblLine());

        // PO Status
        sb.AppendLine(TwoCol("PO STATUS:", FormatOrderStatus(order.Status)));
        sb.AppendLine(Line('-'));

        // Payment Section
        string payStatus = FormatPaymentStatus(order, estimatedTotal);
        sb.AppendLine(TwoCol("PAYMENT STATUS:", payStatus));

        var lastPayment = order.Payments?.OrderByDescending(p => p.PaymentDate ?? p.CreatedAt).FirstOrDefault();
        if (payStatus != "UNPAID" && payStatus != "Not Paid" && payStatus != "COD (Pending)")
        {
            if (lastPayment != null)
            {
                sb.AppendLine(TwoCol("PAYMENT METHOD:", FormatPaymentMethod(lastPayment.Method)));
            }
            sb.AppendLine(TwoCol("AMOUNT PAID:", $"₱{totalPaidOnReceipt:N2}"));
            if (overpaidOnReceipt > 0)
            {
                sb.AppendLine(TwoCol("OVERPAID:", $"₱{overpaidOnReceipt:N2}"));
            }
            if (payStatus == "Partly Paid")
            {
                sb.AppendLine(TwoCol("BALANCE DUE:", $"₱{balanceDueOnReceipt:N2}"));
            }
            var payDate = lastPayment?.PaymentDate ?? lastPayment?.CreatedAt;
            if (payDate.HasValue)
            {
                sb.AppendLine(TwoCol("PAYMENT DATE:", payDate.Value.ToString("MMM dd, yyyy")));
            }
            if (!string.IsNullOrWhiteSpace(lastPayment?.Reference))
            {
                sb.AppendLine(TwoCol("REF #:", lastPayment.Reference));
            }
        }
        else
        {
            sb.AppendLine(TwoCol("BALANCE DUE:", $"₱{estimatedTotal:N2}"));
        }

        sb.AppendLine(Line('-'));

        // Fulfillment Section
        sb.AppendLine(TwoCol("FULFILLMENT:", FormatFulfillmentStatus(order)));
        if (order.DeliveryType == DeliveryType.DELIVERY && !string.IsNullOrWhiteSpace(order.Address))
        {
            sb.AppendLine($"Address: {order.Address}");
        }

        // Footer Section
        sb.AppendLine(Line('-'));
        string footer = string.IsNullOrWhiteSpace(footerMsg) || footerMsg.Contains("Thank you for using") || footerMsg.Contains("Thank you for trusting")
            ? profile.ReceiptFooter
            : footerMsg;
        sb.AppendLine(Center(footer));
        sb.AppendLine(Center($"Printed: {DateTime.Now:MMM dd, yyyy  h:mm tt}"));
        sb.AppendLine();
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Creates a formatted FlowDocument suitable for receipt printing (thermal or standard paper).
    /// </summary>
    public static FlowDocument CreateReceiptDocument(string receiptText, double printableWidth = 280)
    {
        var doc = new FlowDocument
        {
            PageWidth   = printableWidth,
            PagePadding = new Thickness(8, 12, 8, 12),
            ColumnWidth = double.PositiveInfinity,
            FontFamily  = new FontFamily("Consolas, 'Courier New', Lucida Console, monospace"),
            FontSize    = 9.5,
            LineHeight  = 13.5,
            Background  = Brushes.White,
            Foreground  = Brushes.Black,
        };

        var p = new Paragraph
        {
            Margin        = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            FontFamily    = new FontFamily("Consolas, 'Courier New', Lucida Console, monospace"),
        };
        p.Inlines.Add(new Run(receiptText));
        doc.Blocks.Add(p);
        return doc;
    }

    /// <summary>
    /// Legacy compatibility wrapper for rich receipt creation. Routes directly to CreateThermalPurchaseOrderFlowDocument.
    /// </summary>
    public static FlowDocument CreateRichReceiptDocument(
        Order order, string? businessName = null, string? footerMsg = null, double pageWidth = 280)
    {
        var profile = BusinessProfileService.GetProfileById(order?.BusinessId) ?? BusinessProfileService.ActiveProfile;
        string bName = string.IsNullOrWhiteSpace(businessName) ? profile.SystemName : businessName;
        string fMsg = string.IsNullOrWhiteSpace(footerMsg) ? profile.ReceiptFooter : footerMsg;
        if (order is null) return new FlowDocument();
        return CreateThermalPurchaseOrderFlowDocument(order, bName, fMsg, pageWidth: pageWidth);
    }

    public static (bool success, string message) PrintContent(
        string text,
        string title,
        string? targetPrinter = null,
        bool showDialog = false)
    {
        try
        {
            var pd = new PrintDialog();
            var queue = ResolvePrintQueue(targetPrinter);
            if (queue != null) pd.PrintQueue = queue;

            // Only show dialog if explicitly asked or no queue was resolvable
            if (showDialog || pd.PrintQueue == null)
            {
                var result = pd.ShowDialog();
                if (result != true)
                {
                    return (false, "Print cancelled by user.");
                }
            }

            // Determine page width (for 58mm/80mm or standard A4)
            var width = pd.PrintableAreaWidth > 50 ? pd.PrintableAreaWidth : 280;
            if (width > 350) width = 350; // Keep receipt column neat on standard wide printers

            var doc = CreateReceiptDocument(text, width);
            IDocumentPaginatorSource dps = doc;
            pd.PrintDocument(dps.DocumentPaginator, title);

            var printerName = pd.PrintQueue?.FullName ?? pd.PrintQueue?.Name ?? targetPrinter ?? "Printer";
            return (true, $"Printed successfully to {printerName}.");
        }
        catch (Exception ex)
        {
            // Fallback: try raw spooler if a target printer name was specified
            if (!string.IsNullOrWhiteSpace(targetPrinter))
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    if (SendRawBytesToPrinter(targetPrinter, bytes))
                    {
                        return (true, $"Printed via Raw Spooler to {targetPrinter}.");
                    }
                }
                catch { }
            }

            return (false, $"Printing error: {ex.Message}");
        }
    }

    /// <summary>
    /// Prints a WPF FlowDocument to any installed printer or opens the Windows Print Dialog.
    /// Supports automatic scaling to thermal (58mm/80mm) or standard A4 printer pages.
    /// </summary>
    public static (bool success, string message) PrintFlowDocument(
        FlowDocument doc,
        string title,
        string? targetPrinter = null,
        bool showDialog = false)
    {
        try
        {
            var pd = new PrintDialog();
            var queue = ResolvePrintQueue(targetPrinter);
            if (queue != null) pd.PrintQueue = queue;

            if (showDialog || pd.PrintQueue == null)
            {
                var result = pd.ShowDialog();
                if (result != true)
                {
                    return (false, "Print cancelled by user.");
                }
            }

            var printerName = pd.PrintQueue?.FullName ?? pd.PrintQueue?.Name ?? targetPrinter ?? "Printer";
            bool isThermal = IsThermalPrinter(printerName);

            // Adapt page width and height to printer printable area
            if (isThermal)
            {
                var printableWidth = pd.PrintableAreaWidth > 50 ? pd.PrintableAreaWidth : 302;
                doc.PageWidth = Math.Min(printableWidth, 380);
                if (double.IsNaN(doc.PageHeight) || doc.PageHeight < 100)
                    doc.PageHeight = 1500;
            }
            else
            {
                var printableWidth = pd.PrintableAreaWidth > 100 ? pd.PrintableAreaWidth : 793.7;
                var printableHeight = pd.PrintableAreaHeight > 100 ? pd.PrintableAreaHeight : 1122.5;
                if (double.IsNaN(doc.PageWidth) || doc.PageWidth < 400)
                    doc.PageWidth = printableWidth;
                if (double.IsNaN(doc.PageHeight) || doc.PageHeight < 100)
                    doc.PageHeight = printableHeight;
            }
            doc.ColumnWidth = double.PositiveInfinity;

            IDocumentPaginatorSource dps = doc;
            pd.PrintDocument(dps.DocumentPaginator, title);

            return (true, $"Printed successfully to {printerName}.");
        }
        catch (Exception ex)
        {
            return (false, $"Printing error: {ex.Message}");
        }
    }

    /// <summary>
    /// Prints an Order Receipt.
    /// Automatically selects standard A4 Purchase Order layout for desktop printers (e.g. Epson L120)
    /// or continuous thermal receipt layout for POS thermal roll printers (58mm/80mm).
    /// Falls back to raw text printing if needed.
    /// </summary>
    public static (bool success, string message) PrintReceipt(
        Order order,
        string? businessName = null,
        string? footerMsg = null,
        string? targetPrinter = null,
        bool showDialog = false)
    {
        var profile = BusinessProfileService.GetProfileById(order?.BusinessId) ?? BusinessProfileService.ActiveProfile;
        string bName = string.IsNullOrWhiteSpace(businessName) || businessName == "ANTIQUE PASABUY POS" ? profile.SystemName : businessName;
        string fMsg = string.IsNullOrWhiteSpace(footerMsg) || footerMsg.Contains("Thank you") ? profile.ReceiptFooter : footerMsg;
        try
        {
            var pd = new PrintDialog();
            var queue = ResolvePrintQueue(targetPrinter);
            if (queue != null) pd.PrintQueue = queue;

            if (showDialog || pd.PrintQueue == null)
            {
                var result = pd.ShowDialog();
                if (result != true)
                {
                    return (false, "Print cancelled by user.");
                }
            }

            if (order is null) return (false, "No order provided.");

            var printerName = pd.PrintQueue?.FullName ?? pd.PrintQueue?.Name ?? targetPrinter ?? "Printer";
            bool isThermal = IsThermalPrinter(printerName);

            FlowDocument doc;
            if (isThermal)
            {
                var printableWidth = pd.PrintableAreaWidth > 50 ? pd.PrintableAreaWidth : 302;
                var width = Math.Min(printableWidth, 380);
                doc = CreateThermalPurchaseOrderFlowDocument(order, bName, fMsg, pageWidth: width);
                doc.PageHeight = 1500;
            }
            else
            {
                // Standard desktop printer (A4 / Letter / Epson L120)
                var printableWidth = pd.PrintableAreaWidth > 100 ? pd.PrintableAreaWidth : 793.7;
                var printableHeight = pd.PrintableAreaHeight > 100 ? pd.PrintableAreaHeight : 1122.5;
                doc = CreatePurchaseOrderFlowDocument(order, bName, fMsg, pageWidth: printableWidth);
                doc.PageHeight = printableHeight;
            }

            doc.ColumnWidth = double.PositiveInfinity;
            IDocumentPaginatorSource dps = doc;
            pd.PrintDocument(dps.DocumentPaginator, $"Receipt - {order.OrderNo}");

            return (true, $"Printed successfully to {printerName}.");
        }
        catch (Exception ex)
        {
            // Fallback to text mode if flow document print had an issue
            try
            {
                var text = GenerateReceiptText(order ?? new Order(), bName, fMsg);
                return PrintContent(text, $"Receipt - {order?.OrderNo ?? "N/A"}", targetPrinter, showDialog);
            }
            catch
            {
                return (false, $"Receipt generation failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Prints a POS Test Page to verify printer connection.
    /// </summary>
    public static (bool success, string message) PrintTestPage(
        string? targetPrinter = null,
        bool showDialog = false,
        string businessName = "ANTIQUE PASABUY POS")
    {
        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine(string.IsNullOrWhiteSpace(businessName) ? "        ANTIQUE PASABUY POS" : $"        {businessName.ToUpper()}");
        sb.AppendLine("           PRINTER TEST PAGE");
        sb.AppendLine("========================================");
        sb.AppendLine($"Target:    {targetPrinter ?? "(Windows Default)"}");
        sb.AppendLine($"Date/Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("Status:    Connected & Available");
        sb.AppendLine("========================================");
        sb.AppendLine("Sample Item: 1x Test Package     ₱150.00");
        sb.AppendLine("PASABUY FEE:                      ₱75.00");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("TOTAL:                           ₱225.00");
        sb.AppendLine("========================================");
        sb.AppendLine("If this prints clearly, your printer is");
        sb.AppendLine("ready for all Antique Pasabuy receipts!");
        sb.AppendLine("========================================");
        sb.AppendLine();
        sb.AppendLine();

        return PrintContent(sb.ToString(), "Antique Pasabuy - Printer Test", targetPrinter, showDialog);
    }

    /// <summary>
    /// Generates a store-grouped Buying Checklist document for a single Purchase Order,
    /// formatted with checkboxes for easy in-store shopping following the exact owner spec.
    /// </summary>
    public static FlowDocument CreateOrderBuyingChecklistDocument(Order order, double pageWidth = 793.7)
    {
        double padH = 36;
        double padV = 30;
        var font = new FontFamily("Segoe UI, Arial, sans-serif");

        var doc = new FlowDocument
        {
            PageWidth   = pageWidth,
            PagePadding = new Thickness(padH, padV, padH, padV),
            ColumnWidth = double.PositiveInfinity,
            Background  = Brushes.White,
            Foreground  = Brushes.Black,
            FontFamily  = font
        };

        // Header — Logo + Title
        double cwCL = Math.Max(400, pageWidth - padH * 2);
        var logoBlockCL = BuildLogoBlock(cwCL, maxHeight: 60);
        if (logoBlockCL != null) doc.Blocks.Add(logoBlockCL);

        var pHeader = new Paragraph { Margin = new Thickness(0, 2, 0, 4), TextAlignment = TextAlignment.Center };
        pHeader.Inlines.Add(new Run("DIONIX POS\n") { FontSize = 16, FontWeight = FontWeights.Black, Foreground = Brushes.Black });
        pHeader.Inlines.Add(new Run("ANTIQUE PASABUY\n") { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Hex("#452B86") });
        pHeader.Inlines.Add(new Run("SHOPPING CHECKLIST\n") { FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Hex("#0F172A") });
        doc.Blocks.Add(pHeader);

        // PO meta block
        var pPOMeta = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
        pPOMeta.Inlines.Add(new Run($"PO #: {order.OrderNo}    ") { FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Hex("#0F172A") });
        pPOMeta.Inlines.Add(new Run($"Date: {order.CreatedAt:MMM dd, yyyy}\n") { FontSize = 10, Foreground = Hex("#334155") });
        pPOMeta.Inlines.Add(new Run($"Customer: {order.Customer?.Name ?? "Walk-in"}\n") { FontSize = 11.5, FontWeight = FontWeights.SemiBold });
        // Buying Day kept in DB — not printed on customer-facing checklist.
        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            pPOMeta.Inlines.Add(new Run($"Notes: {order.Notes}\n") { FontSize = 10, FontStyle = FontStyles.Italic, Foreground = Hex("#475569") });
        }
        doc.Blocks.Add(pPOMeta);

        // Divider
        doc.Blocks.Add(new Paragraph { BorderBrush = Hex("#CBD5E1"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 12) });

        var validItems = order.Items?.Where(i => !string.IsNullOrWhiteSpace(i.ProductName)).ToList() ?? new List<OrderItem>();
        var storeGroups = validItems
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Store) ? "General Store" : i.Store)
            .OrderBy(g => g.Key);

        foreach (var group in storeGroups)
        {
            var pStore = new Paragraph(new Run($"STORE: {group.Key.ToUpper()}"))
            {
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Hex("#452B86"),
                Margin = new Thickness(0, 10, 0, 4)
            };
            doc.Blocks.Add(pStore);

            var itemsList = new Paragraph { Margin = new Thickness(12, 2, 0, 8), LineHeight = 22 };
            foreach (var item in group)
            {
                itemsList.Inlines.Add(new Run("☐ ") { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
                itemsList.Inlines.Add(new Run($"{item.ProductName}") { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black });
                var qtyStr = item.Qty % 1 == 0 ? $"{item.Qty:0}" : $"{item.Qty:0.##}";
                itemsList.Inlines.Add(new Run($" — Qty: {qtyStr}") { FontSize = 11, Foreground = Hex("#334155") });
                if (!string.IsNullOrWhiteSpace(item.Notes))
                {
                    itemsList.Inlines.Add(new Run($" ({item.Notes})") { FontSize = 10, FontStyle = FontStyles.Italic, Foreground = Hex("#64748B") });
                }
                itemsList.Inlines.Add(new LineBreak());
            }
            doc.Blocks.Add(itemsList);
        }

        // Summary Divider
        doc.Blocks.Add(new Paragraph { BorderBrush = Hex("#CBD5E1"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 10, 0, 8) });

        var totalBudget = validItems.Sum(i => (decimal)i.Qty * i.EstimatedPrice);
        var pSummary = new Paragraph { Margin = new Thickness(0, 4, 0, 0) };
        pSummary.Inlines.Add(new Run($"TOTAL ITEMS: {validItems.Count}\n") { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pSummary.Inlines.Add(new Run($"ESTIMATED BUDGET: ₱{totalBudget:N2}\n") { FontSize = 13, FontWeight = FontWeights.Black, Foreground = Hex("#452B86") });
        doc.Blocks.Add(pSummary);

        return doc;
    }

    /// <summary>
    /// Generates thermal POS text for single PO buying checklist.
    /// </summary>
    public static string GenerateOrderBuyingChecklistText(Order order, int width = 36)
    {
        var sb = new StringBuilder();
        int W = Math.Max(32, width);
        string Line(char c = '-') => new string(c, W);
        string Center(string s) => s.Length >= W ? s : s.PadLeft((W + s.Length) / 2).PadRight(W);

        sb.AppendLine(Center("DIONIX POS"));
        sb.AppendLine(Center("ANTIQUE PASABUY"));
        sb.AppendLine();
        sb.AppendLine("PURCHASE ORDER");
        sb.AppendLine(order.OrderNo);
        sb.AppendLine();
        sb.AppendLine($"Customer: {order.Customer?.Name ?? "Walk-in"}");
        sb.AppendLine($"Shopping Day: {order.BuyingDay}");
        if (!string.IsNullOrWhiteSpace(order.Notes))
            sb.AppendLine($"Notes: {order.Notes}");
        sb.AppendLine(Line('-'));

        var validItems = order.Items?.Where(i => !string.IsNullOrWhiteSpace(i.ProductName)).ToList() ?? new List<OrderItem>();
        var storeGroups = validItems
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Store) ? "General Store" : i.Store)
            .OrderBy(g => g.Key);

        foreach (var group in storeGroups)
        {
            sb.AppendLine();
            sb.AppendLine($"STORE: {group.Key.ToUpper()}");
            sb.AppendLine();
            foreach (var item in group)
            {
                var qtyStr = item.Qty % 1 == 0 ? $"{item.Qty:0}" : $"{item.Qty:0.##}";
                var line = $"[ ] {item.ProductName} — Qty: {qtyStr}";
                if (!string.IsNullOrWhiteSpace(item.Notes)) line += $" ({item.Notes})";
                sb.AppendLine(line);
            }
        }

        sb.AppendLine(Line('-'));
        var totalBudget = validItems.Sum(i => (decimal)i.Qty * i.EstimatedPrice);
        sb.AppendLine($"TOTAL ITEMS: {validItems.Count}");
        sb.AppendLine($"ESTIMATED BUDGET: ₱{totalBudget:N2}");
        sb.AppendLine();
        sb.AppendLine();
        return sb.ToString();
    }

    public static (bool success, string message) PrintOrderBuyingChecklist(
        Order order,
        string? targetPrinter = null,
        bool showDialog = true)
    {
        var text = GenerateOrderBuyingChecklistText(order);
        return PrintContent(text, $"Checklist - {order.OrderNo}", targetPrinter, showDialog);
    }

    /// <summary>
    /// Builds a formal Purchase Order FlowDocument for preview or printing.
    /// </summary>
    /// <summary>
    /// Builds a formal Purchase Order FlowDocument for preview or printing on standard sheets (A4, Letter, Legal, A5).
    /// Professional business layout: clear store grouping, human-readable status, no delivery fees/riders, separate payment/fulfillment.
    /// </summary>
    /// <summary>
    /// Builds a formal Purchase Order FlowDocument for preview or printing on standard sheets (A4, Letter, Legal, A5).
    /// Professional business layout: clear store grouping, human-readable status, no delivery fees/riders, separate payment/fulfillment.
    /// </summary>
    public static FlowDocument CreatePurchaseOrderFlowDocument(
        Order order,
        string businessName = "DIONIX POS",
        string footerMsg = "Thank you for trusting DIONIX POS!",
        double pageWidth = 793.7)
    {
        double padH = 36;
        double padV = 30;
        var font = new FontFamily("Segoe UI, Arial, sans-serif");

        var doc = new FlowDocument
        {
            PageWidth   = pageWidth,
            PagePadding = new Thickness(padH, padV, padH, padV),
            ColumnWidth = double.PositiveInfinity,
            Background  = Brushes.White,
            Foreground  = Brushes.Black,
            FontFamily  = font
        };

        double cw = Math.Max(400, pageWidth - padH * 2);

        // ─── HEADER: Logo + Brand Title (left) | Order Metadata (right) ────────
        var profile = BusinessProfileService.GetProfileById(order.BusinessId) ?? BusinessProfileService.ActiveProfile;
        var logoBlockA4 = BuildLogoBlock(cw, maxHeight: 60, profile.LogoResource);
        if (logoBlockA4 != null) doc.Blocks.Add(logoBlockA4);

        var headTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 10) };
        headTable.Columns.Add(new TableColumn { Width = new GridLength(cw * 0.55) });
        headTable.Columns.Add(new TableColumn { Width = new GridLength(cw * 0.45) });
        var hg = new TableRowGroup();
        var hr = new TableRow();

        var pLeft = new Paragraph { Margin = new Thickness(0) };
        pLeft.Inlines.Add(new Run($"{profile.SystemName}\n") { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pLeft.Inlines.Add(new Run($"{profile.Subtitle}\n") { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pLeft.Inlines.Add(new Run("PURCHASE ORDER\n") { FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        hr.Cells.Add(new TableCell(pLeft));

        var pRight = new Paragraph { Margin = new Thickness(0), TextAlignment = TextAlignment.Right, LineHeight = 18 };
        pRight.Inlines.Add(new Run($"PO #: {order.OrderNo}\n") { FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pRight.Inlines.Add(new Run($"Date: {order.CreatedAt:MMM dd, yyyy  h:mm tt}\n") { FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        var custName = order.Customer?.Name ?? "Walk-in";
        pRight.Inlines.Add(new Run($"Customer: {custName}\n") { FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        hr.Cells.Add(new TableCell(pRight));

        hg.Rows.Add(hr);
        headTable.RowGroups.Add(hg);
        doc.Blocks.Add(headTable);

        // Divider
        doc.Blocks.Add(new Paragraph
        {
            BorderBrush     = Brushes.Black,
            BorderThickness = new Thickness(0, 1.5, 0, 0),
            Margin          = new Thickness(0, 2, 0, 12)
        });

        // Items Table — consolidated items, NO STORE column, bold black text, unavailable in red
        var allItems = order.Items?.Where(i => !string.IsNullOrWhiteSpace(i.ProductName)).ToList() ?? new List<OrderItem>();
        var consolidatedItems = allItems
            .GroupBy(i => (
                Name: i.ProductName.Trim(),
                Unit: (i.Unit ?? "").Trim(),
                Price: i.ActualPrice ?? i.EstimatedPrice,
                Status: i.Status,
                Source: i.Source
            ))
            .Select(g => new {
                ProductName = g.Key.Name,
                Unit = g.Key.Unit,
                Price = g.Key.Price,
                Status = g.Key.Status,
                Source = g.Key.Source,
                Qty = g.Sum(i => i.Qty),
                CostPrice = g.First().CostPrice,
                PurchaseNotes = g.Select(i => i.PurchaseNotes ?? i.Notes).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            })
            .OrderBy(i => i.ProductName)
            .ToList();

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 10) };
        double wQty   = 65;
        double wPrice = 110;
        double wTotal = 110;
        double wItem  = Math.Max(180, cw - wQty - wPrice - wTotal);

        table.Columns.Add(new TableColumn { Width = new GridLength(wQty) });
        table.Columns.Add(new TableColumn { Width = new GridLength(wItem) });
        table.Columns.Add(new TableColumn { Width = new GridLength(wPrice) });
        table.Columns.Add(new TableColumn { Width = new GridLength(wTotal) });

        var thg = new TableRowGroup();
        var thr = new TableRow { Background = Hex("#F1F5F9") };

        void AddTh(string text, TextAlignment align)
        {
            thr.Cells.Add(new TableCell(new Paragraph(new Run(text))
            {
                FontSize      = 10,
                FontWeight    = FontWeights.Bold,
                Foreground    = Brushes.Black,
                TextAlignment = align,
                Margin        = new Thickness(0)
            })
            {
                Padding         = new Thickness(6, 6, 6, 6),
                BorderBrush     = Brushes.Black,
                BorderThickness = new Thickness(0, 1, 0, 1.5)
            });
        }
        AddTh("QTY", TextAlignment.Center);
        AddTh("ITEM / PRODUCT", TextAlignment.Left);
        AddTh("PRICE", TextAlignment.Right);
        AddTh("TOTAL", TextAlignment.Right);
        thg.Rows.Add(thr);
        table.RowGroups.Add(thg);

        var bg = new TableRowGroup();
        foreach (var item in consolidatedItems)
        {
            var isUnavailable = item.Status == ItemStatus.UNAVAILABLE;
            var isBoughtOnHand = item.Source == ItemSource.STOCK || item.Status == ItemStatus.BOUGHT_FROM_STOCK;
            var isReorder = item.Status == ItemStatus.REORDER;

            var price = item.Price;
            var total = price * (decimal)item.Qty;
            var qtyStr = item.Qty % 1 == 0 ? $"{item.Qty:0}" : $"{item.Qty:0.##}";

            var itemBrush = isUnavailable ? Brushes.Red : Brushes.Black;
            var tr = new TableRow();

            // Qty
            tr.Cells.Add(new TableCell(new Paragraph(new Run(qtyStr))
            {
                FontSize      = 10,
                FontWeight    = FontWeights.Bold,
                Foreground    = itemBrush,
                TextAlignment = TextAlignment.Center,
                Margin        = new Thickness(0)
            }) { Padding = new Thickness(6, 5, 6, 5), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 0.5) });

            // Item Name + Notes + Status tags
            var pItm = new Paragraph
            {
                FontSize   = 10,
                FontWeight = FontWeights.Bold,
                Foreground = itemBrush,
                Margin     = new Thickness(0)
            };
            pItm.Inlines.Add(new Run(item.ProductName));
            if (isUnavailable)
            {
                pItm.Inlines.Add(new Run(" [NOT AVAILABLE]")
                {
                    FontSize   = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Red
                });
            }
            else if (isBoughtOnHand)
            {
                pItm.Inlines.Add(new Run(" [BOUGHT ON HAND]")
                {
                    FontSize   = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                });
            }
            else if (isReorder)
            {
                pItm.Inlines.Add(new Run(" [REORDER]")
                {
                    FontSize   = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                });
            }

            if (item.CostPrice.HasValue && item.CostPrice.Value > price)
            {
                var unitSavings = item.CostPrice.Value - price;
                pItm.Inlines.Add(new LineBreak());
                pItm.Inlines.Add(new Run($"Orig: ₱{item.CostPrice.Value:N2} | Saved: ₱{unitSavings:N2}")
                {
                    FontSize   = 8.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                });
            }
            if (!string.IsNullOrWhiteSpace(item.PurchaseNotes))
            {
                pItm.Inlines.Add(new LineBreak());
                pItm.Inlines.Add(new Run($"* {item.PurchaseNotes}")
                {
                    FontSize   = 8.5,
                    FontStyle  = FontStyles.Italic,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black
                });
            }
            tr.Cells.Add(new TableCell(pItm) { Padding = new Thickness(6, 5, 6, 5), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 0.5) });

            // Price
            tr.Cells.Add(new TableCell(new Paragraph(new Run(isUnavailable ? "—" : $"₱{price:N2}"))
            {
                FontSize      = 10,
                FontWeight    = FontWeights.Bold,
                Foreground    = itemBrush,
                TextAlignment = TextAlignment.Right,
                Margin        = new Thickness(0)
            }) { Padding = new Thickness(6, 5, 6, 5), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 0.5) });

            // Total
            tr.Cells.Add(new TableCell(new Paragraph(new Run(isUnavailable ? "—" : $"₱{total:N2}"))
            {
                FontSize      = 10,
                FontWeight    = FontWeights.Bold,
                Foreground    = itemBrush,
                TextAlignment = TextAlignment.Right,
                Margin        = new Thickness(0)
            }) { Padding = new Thickness(6, 5, 6, 5), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 0.5) });

            bg.Rows.Add(tr);
        }

        if (consolidatedItems.Count == 0)
        {
            var empRow = new TableRow();
            empRow.Cells.Add(new TableCell(new Paragraph(new Run("(No items recorded)"))
            {
                FontSize = 10, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, Foreground = Brushes.Black
            }) { ColumnSpan = 4, Padding = new Thickness(6, 10, 6, 10) });
            bg.Rows.Add(empRow);
        }

        table.RowGroups.Add(bg);
        doc.Blocks.Add(table);

        // Summary Calculations (Delivery fee removed)
        decimal purchaseBudget = order.ItemsSubtotal;
        decimal pasabuyFee = order.PasabuyFee;
        decimal discount = order.Discount;
        decimal estimatedTotal = Math.Max(0m, purchaseBudget + pasabuyFee - discount);

        // Summary Block (Right-Aligned, all black & bold)
        var pFin = new Paragraph { Margin = new Thickness(0, 4, 0, 12), TextAlignment = TextAlignment.Right, LineHeight = 20 };
        pFin.Inlines.Add(new Run($"PURCHASE BUDGET ({consolidatedItems.Count} items): ₱{purchaseBudget:N2}\n") { FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pFin.Inlines.Add(new Run($"PASABUY FEE: ₱{pasabuyFee:N2}\n") { FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        if (discount > 0)
        {
            pFin.Inlines.Add(new Run($"DISCOUNT: -₱{discount:N2}\n") { FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        }
        var totalSavingsA4 = consolidatedItems
            .Where(i => i.CostPrice.HasValue && i.CostPrice.Value > i.Price)
            .Sum(i => (i.CostPrice!.Value - i.Price) * (decimal)i.Qty);
        if (totalSavingsA4 > 0)
        {
            pFin.Inlines.Add(new Run($"TOTAL SAVINGS: ₱{totalSavingsA4:N2}\n") { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        }
        pFin.Inlines.Add(new Run("────────────────────────────────────────\n") { FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pFin.Inlines.Add(new Run($"ESTIMATED TOTAL: ₱{estimatedTotal:N2}\n") { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        doc.Blocks.Add(pFin);

        // Status, Payment, Fulfillment 3-Box Table (All black & bold)
        var gridTable = new Table { CellSpacing = 6, Margin = new Thickness(0, 0, 0, 12) };
        double colW = Math.Round((cw - 12) / 3);
        gridTable.Columns.Add(new TableColumn { Width = new GridLength(colW) });
        gridTable.Columns.Add(new TableColumn { Width = new GridLength(colW) });
        gridTable.Columns.Add(new TableColumn { Width = new GridLength(colW) });
        var gg = new TableRowGroup();
        var gr = new TableRow();

        TableCell MakeBox(string title, List<(string, string, bool)> lines)
        {
            var pBox = new Paragraph { Margin = new Thickness(0), LineHeight = 16 };
            pBox.Inlines.Add(new Run($"{title.ToUpper()}\n") { FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
            foreach (var (lbl, val, bold) in lines)
            {
                pBox.Inlines.Add(new Run($"{lbl} ") { FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black });
                pBox.Inlines.Add(new Run($"{val}\n") { FontSize = 9.5, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = Brushes.Black });
            }
            return new TableCell(pBox)
            {
                Padding         = new Thickness(10, 8, 10, 8),
                Background      = Hex("#F8FAFC"),
                BorderBrush     = Brushes.Black,
                BorderThickness = new Thickness(1)
            };
        }

        // Box 1: PO Status
        gr.Cells.Add(MakeBox("PO Status", new List<(string, string, bool)>
        {
            ("Status:", FormatOrderStatus(order.Status), true),
            ("Workflow:", "Store purchasing queue", false)
        }));

        // Box 2: Payment Section
        string payStatus = FormatPaymentStatus(order, estimatedTotal);
        var lastPayment = order.Payments?.OrderByDescending(p => p.PaymentDate ?? p.CreatedAt).FirstOrDefault();
        var payLines = new List<(string, string, bool)>
        {
            ("Status:", payStatus, true)
        };
        if (payStatus != "UNPAID")
        {
            if (lastPayment != null) payLines.Add(("Method:", FormatPaymentMethod(lastPayment.Method), true));
            payLines.Add(("Paid:", $"₱{order.TotalPaid:N2}", true));
            var payDate = lastPayment?.PaymentDate ?? lastPayment?.CreatedAt;
            if (payDate.HasValue) payLines.Add(("Date:", payDate.Value.ToString("MMM dd, yyyy"), false));
        }
        else
        {
            payLines.Add(("Balance Due:", $"₱{estimatedTotal:N2}", true));
        }
        gr.Cells.Add(MakeBox("Payment", payLines));

        // Box 3: Fulfillment
        var fulLines = new List<(string, string, bool)>
        {
            ("Status:", FormatFulfillmentStatus(order), true)
        };
        if (order.DeliveryType == DeliveryType.DELIVERY && !string.IsNullOrWhiteSpace(order.Address))
        {
            fulLines.Add(("Address:", order.Address, false));
        }
        gr.Cells.Add(MakeBox("Fulfillment", fulLines));

        gg.Rows.Add(gr);
        gridTable.RowGroups.Add(gg);
        doc.Blocks.Add(gridTable);

        // Footer
        string footer = string.IsNullOrWhiteSpace(footerMsg) || footerMsg.Contains("Thank you for using")
            ? "Thank you for trusting DIONIX POS!"
            : footerMsg;

        var pFoot = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        pFoot.Inlines.Add(new Run($"{footer}\n") { FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pFoot.Inlines.Add(new Run($"Printed: {DateTime.Now:MMM dd, yyyy  h:mm tt}  •  DIONIX POS Antique Pasabuy") { FontSize = 8.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black });
        doc.Blocks.Add(pFoot);

        return doc;
    }

    public static FlowDocument CreateRichBuyingListDocument(
        List<OrderItem> items,
        string storeName = "All Stores",
        string buyingDay = "All Days",
        DateTime? printDate = null,
        double pageWidth = 793.7) // Standard A4 width
    {
        double padH = 36;
        double padV = 30;
        var font = new FontFamily("Segoe UI, Arial, sans-serif");
        var monoFont = new FontFamily("Consolas, 'Courier New', monospace");

        var doc = new FlowDocument
        {
            PageWidth   = pageWidth,
            PagePadding = new Thickness(padH, padV, padH, padV),
            ColumnWidth = double.PositiveInfinity,
            Background  = Brushes.White,
            Foreground  = Brushes.Black,
            FontFamily  = font
        };

        double cw = Math.Max(400, pageWidth - padH * 2);

        // ─── HEADER — Logo + Title ──────────────────────────────────────────
        var logoBlockList = BuildLogoBlock(cw, maxHeight: 60);
        if (logoBlockList != null) doc.Blocks.Add(logoBlockList);

        var pHeader = new Paragraph { Margin = new Thickness(0, 2, 0, 2), TextAlignment = TextAlignment.Center };
        pHeader.Inlines.Add(new Run("DIONIX POS\n") { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pHeader.Inlines.Add(new Run("ANTIQUE PASABUY\n") { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Hex("#452B86") });
        pHeader.Inlines.Add(new Run("SHOPPING CHECKLIST\n") { FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Hex("#0F172A") });
        doc.Blocks.Add(pHeader);

        // ─── META SUMMARY ───────────────────────────────────────────────────
        int totalItemsA4   = items.Count;
        double totalUnitsA4 = items.Sum(i => i.Qty);
        decimal estBudgetA4 = items.Sum(i => (decimal)i.Qty * i.EstimatedPrice);
        var targetDate = printDate ?? DateTime.Today;
        var pMeta = new Paragraph { FontSize = 9.5, Foreground = Hex("#334155"), Margin = new Thickness(0, 4, 0, 10), TextAlignment = TextAlignment.Center };
        pMeta.Inlines.Add(new Run($"Date: {targetDate:MMM dd, yyyy} ({targetDate:dddd})    |    Store: {storeName}    |    Printed: {DateTime.Now:h:mm tt}\n"));
        pMeta.Inlines.Add(new Run($"Total Items: {totalItemsA4}    |    Total Qty: {totalUnitsA4:0.##}    |    Estimated Budget: \u20b1{estBudgetA4:N2}"));
        doc.Blocks.Add(pMeta);

        // Thin divider line
        var divLine = new Paragraph
        {
            BorderBrush = Hex("#000000"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 0, 0, 12)
        };
        doc.Blocks.Add(divLine);

        // ─── STORES & TABLES ──────────────────────────────────────────────────
        var storeGroups = items
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Store) ? "General Store" : i.Store)
            .OrderBy(g => g.Key);

        // Column widths for A4: CHECK (44), PRODUCT (*), QTY (65), CUSTOMER (130), ORDER # (95), EST. PRICE (85)
        double wCheck = 44;
        double wQty   = 65;
        double wCust  = 130;
        double wOrd   = 95;
        double wPrice = 85;
        double wProd  = Math.Max(120, cw - wCheck - wQty - wCust - wOrd - wPrice);

        foreach (var group in storeGroups)
        {
            // Simple, obvious store heading
            var pStore = new Paragraph(new Run($"STORE: {group.Key.ToUpper()}"))
            {
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                Margin = new Thickness(0, 8, 0, 4)
            };
            doc.Blocks.Add(pStore);

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 8) };
            table.Columns.Add(new TableColumn { Width = new GridLength(wCheck) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wProd) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wQty) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wCust) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wOrd) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wPrice) });

            // Table Header Row
            var hg = new TableRowGroup();
            var hr = new TableRow();

            void AddTh(string txt, TextAlignment align)
            {
                hr.Cells.Add(new TableCell(new Paragraph(new Run(txt))
                {
                    FontSize   = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Hex("#1E293B"),
                    TextAlignment = align,
                    Margin     = new Thickness(0)
                })
                {
                    Padding = new Thickness(4, 5, 4, 5),
                    BorderBrush = Hex("#94A3B8"),
                    BorderThickness = new Thickness(0, 1, 0, 1)
                });
            }

            AddTh("CHECK", TextAlignment.Center);
            AddTh("ITEM", TextAlignment.Left);
            AddTh("QTY", TextAlignment.Center);
            AddTh("ORDERED BY", TextAlignment.Left);
            AddTh("PO #", TextAlignment.Left);
            AddTh("SUBTOTAL", TextAlignment.Right);
            hg.Rows.Add(hr);
            table.RowGroups.Add(hg);

            // Consolidate same product within this store group
            var consolidated = group
                .GroupBy(i => (
                    Name: i.ProductName.Trim(),
                    Unit: (i.Unit ?? "").Trim(),
                    EstPrice: i.EstimatedPrice
                ))
                .Select(g2 => new {
                    ProductName = g2.Key.Name,
                    Unit = g2.Key.Unit,
                    EstimatedPrice = g2.Key.EstPrice,
                    Qty = g2.Sum(i => i.Qty),
                    Customers = g2.Select(i => i.Order?.Customer?.Name ?? "Walk-in").Distinct().ToList(),
                    Orders = g2.Select(i => i.Order?.OrderNo ?? "—").Distinct().ToList(),
                    Notes = g2.Select(i => i.PurchaseNotes).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList()
                })
                .OrderBy(x => x.ProductName)
                .ToList();

            // Table Data Rows
            var dg = new TableRowGroup();
            double storeQty = 0;
            decimal storeSubtotal = 0;

            foreach (var item in consolidated)
            {
                storeQty += item.Qty;
                var sub = (decimal)item.Qty * item.EstimatedPrice;
                storeSubtotal += sub;

                var row = new TableRow();

                // Physical Checkbox cell
                var checkPara = new Paragraph(new Run("☐"))
                {
                    FontFamily = new FontFamily("Segoe UI Symbol, Arial, sans-serif"),
                    FontSize = 10.5,
                    Foreground = Brushes.Black,
                    TextAlignment = TextAlignment.Center,
                    LineHeight = 15,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    Margin = new Thickness(0)
                };
                row.Cells.Add(new TableCell(checkPara)
                {
                    Padding = new Thickness(2, 5, 2, 5),
                    BorderBrush = Hex("#E2E8F0"),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                });

                // Product Name + Notes
                var prodPara = new Paragraph
                {
                    Margin = new Thickness(0),
                    LineHeight = 15,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight
                };
                prodPara.Inlines.Add(new Run(item.ProductName)
                {
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black
                });
                foreach (var note in item.Notes)
                {
                    prodPara.Inlines.Add(new LineBreak());
                    prodPara.Inlines.Add(new Run($"Note: {note}")
                    {
                        FontSize = 8.5,
                        FontStyle = FontStyles.Italic,
                        Foreground = Hex("#475569")
                    });
                }
                row.Cells.Add(new TableCell(prodPara)
                {
                    Padding = new Thickness(4, 5, 4, 5),
                    BorderBrush = Hex("#E2E8F0"),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                });

                // Qty & Unit
                var qtyStr = item.Qty % 1 == 0 ? $"{item.Qty:0}" : $"{item.Qty:0.##}";
                var qtyPara = new Paragraph(new Run($"{qtyStr} {item.Unit}".Trim()))
                {
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0)
                };
                row.Cells.Add(new TableCell(qtyPara)
                {
                    Padding = new Thickness(2, 5, 2, 5),
                    BorderBrush = Hex("#E2E8F0"),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                });

                // Customer (Ordered By) — joined if multiple
                var custText = string.Join(", ", item.Customers);
                var custPara = new Paragraph(new Run(custText))
                {
                    FontSize = 9.5,
                    Foreground = Hex("#1E293B"),
                    Margin = new Thickness(0)
                };
                row.Cells.Add(new TableCell(custPara)
                {
                    Padding = new Thickness(4, 5, 4, 5),
                    BorderBrush = Hex("#E2E8F0"),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                });

                // Order # — joined if multiple
                var ordText = string.Join(", ", item.Orders);
                var ordPara = new Paragraph(new Run(ordText))
                {
                    FontFamily = monoFont,
                    FontSize = 9,
                    Foreground = Hex("#334155"),
                    Margin = new Thickness(0)
                };
                row.Cells.Add(new TableCell(ordPara)
                {
                    Padding = new Thickness(4, 5, 4, 5),
                    BorderBrush = Hex("#E2E8F0"),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                });

                // Subtotal
                var pricePara = new Paragraph(new Run($"₱{sub:N2}"))
                {
                    FontSize = 9.5,
                    TextAlignment = TextAlignment.Right,
                    Margin = new Thickness(0)
                };
                row.Cells.Add(new TableCell(pricePara)
                {
                    Padding = new Thickness(4, 5, 4, 5),
                    BorderBrush = Hex("#E2E8F0"),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                });

                dg.Rows.Add(row);
            }

            table.RowGroups.Add(dg);
            doc.Blocks.Add(table);

            // Store summary
            var pStoreTotal = new Paragraph
            {
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = Hex("#1E293B"),
                Margin = new Thickness(0, 0, 0, 12)
            };
            pStoreTotal.Inlines.Add(new Run($"STORE TOTAL: {storeQty:0.##} units across {consolidated.Count} line item{(consolidated.Count != 1 ? "s" : "")}   |   Est. Budget: ₱{storeSubtotal:N2}"));
            doc.Blocks.Add(pStoreTotal);
        }

        // ─── OVERALL GRAND TOTAL ──────────────────────────────────────────────
        var grandDiv = new Paragraph
        {
            BorderBrush = Hex("#000000"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 6, 0, 6)
        };
        doc.Blocks.Add(grandDiv);

        var pGrand = new Paragraph
        {
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 2, 0, 4)
        };
        var totalQty = items.Sum(i => i.Qty);
        var grandEst = items.Sum(i => (decimal)i.Qty * i.EstimatedPrice);
        pGrand.Inlines.Add(new Run($"ALL STORES TOTAL: {totalQty:0.##} units across {items.Count} line items    |    TOTAL ESTIMATED BUDGET: ₱{grandEst:N2}"));
        doc.Blocks.Add(pGrand);

        var pFoot = new Paragraph(new Run("DIONIX POS • Antique Pasabuy Checklist • Shopper Copy"))
        {
            FontSize = 8.5,
            Foreground = Hex("#64748B"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        doc.Blocks.Add(pFoot);

        return doc;
    }

    /// <summary>
    /// Builds a compact, single-column Thermal FlowDocument for a Purchase Order (58mm or 80mm).
    /// Prevents horizontal table truncation and formats for thermal paper rolls.
    /// </summary>
    /// <summary>
    /// Builds the primary professional 80mm thermal receipt FlowDocument for a Purchase Order.
    /// Clean monochrome design, thin dividers, perfect column alignments, prominent totals, grouped by store.
    /// </summary>
    /// <summary>
    /// Builds the primary professional 80mm thermal receipt FlowDocument for a Purchase Order.
    /// Clean monochrome design, thin dividers, perfect column alignments, prominent totals, grouped by store.
    /// </summary>
    public static FlowDocument Create80mmThermalReceiptDocument(
        Order order,
        string businessName = "DIONIX POS",
        string footerMsg = "Thank you for trusting DIONIX POS!",
        double pageWidth = 302,
        Thickness? padding = null)
    {
        var pad = padding ?? new Thickness(8, 10, 8, 10);
        var font = new FontFamily("Segoe UI, Arial, sans-serif");
        var doc = new FlowDocument
        {
            PageWidth   = pageWidth,
            PagePadding = pad,
            ColumnWidth = double.PositiveInfinity,
            Background  = Brushes.White,
            Foreground  = Brushes.Black,
            FontFamily  = font
        };

        double cw = Math.Max(220, pageWidth - pad.Left - pad.Right);

        Paragraph ThinDivider(double vMargin = 4, double thickness = 0.75, Brush? brush = null)
        {
            return new Paragraph
            {
                BorderBrush     = brush ?? Brushes.Black,
                BorderThickness = new Thickness(0, thickness, 0, 0),
                Margin          = new Thickness(0, vMargin, 0, vMargin)
            };
        }

        TableRow TwoColRow(string label, string value, bool bold = false, double fontSize = 9.5, Brush? fg = null)
        {
            var r = new TableRow();
            var fw = bold ? FontWeights.Bold : FontWeights.Normal;
            r.Cells.Add(new TableCell(new Paragraph(new Run(label))
            {
                FontSize   = fontSize,
                FontWeight = fw,
                Foreground = fg ?? Brushes.Black,
                Margin     = new Thickness(0)
            }) { Padding = new Thickness(0, 1.5, 4, 1.5) });

            r.Cells.Add(new TableCell(new Paragraph(new Run(value))
            {
                FontSize      = fontSize,
                FontWeight    = fw,
                Foreground    = fg ?? Brushes.Black,
                TextAlignment = TextAlignment.Right,
                Margin        = new Thickness(0)
            }) { Padding = new Thickness(0, 1.5, 0, 1.5) });

            return r;
        }

        // ─── 1. HEADER ────────────────────────────────────────────────────────
        var profile = BusinessProfileService.GetProfileById(order.BusinessId) ?? BusinessProfileService.ActiveProfile;
        // Logo — centered above title text
        var logoBlock80 = BuildLogoBlock(cw, maxHeight: 48, profile.LogoResource);
        if (logoBlock80 != null) doc.Blocks.Add(logoBlock80);

        var pHeader = new Paragraph { Margin = new Thickness(0, 1, 0, 3), TextAlignment = TextAlignment.Center };
        pHeader.Inlines.Add(new Run($"{profile.SystemName}\n") { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pHeader.Inlines.Add(new Run($"{profile.Subtitle}\n") { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pHeader.Inlines.Add(new Run("PURCHASE ORDER\n") { FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black });
        doc.Blocks.Add(pHeader);

        doc.Blocks.Add(ThinDivider(vMargin: 3, thickness: 1.0));

        // ─── 2. ORDER METADATA ────────────────────────────────────────────────
        var metaTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 1, 0, 1) };
        double metaLblW = Math.Round(cw * 0.38);
        metaTable.Columns.Add(new TableColumn { Width = new GridLength(metaLblW) });
        metaTable.Columns.Add(new TableColumn { Width = new GridLength(cw - metaLblW) });

        var metaGroup = new TableRowGroup();
        metaGroup.Rows.Add(TwoColRow("PO #:", order.OrderNo, bold: true, fontSize: 10.5));
        metaGroup.Rows.Add(TwoColRow("Date:", order.CreatedAt.ToString("MMM dd, yyyy  h:mm tt")));
        metaGroup.Rows.Add(TwoColRow("Customer:", order.Customer?.Name ?? "Walk-in", bold: true, fontSize: 9.5));
        // Buying Day kept in DB — not printed on customer-facing receipt.

        metaTable.RowGroups.Add(metaGroup);
        doc.Blocks.Add(metaTable);

        doc.Blocks.Add(ThinDivider(vMargin: 3));

        // ─── 3. ITEMS SECTION (QTY | ITEM | PRICE | TOTAL) ────────────────────
        double colQty = 28;
        double colPrice = 56;
        double colTotal = 62;
        double colItem = Math.Max(70, cw - colQty - colPrice - colTotal);

        var itemsTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 1, 0, 1) };
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(colQty) });
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(colItem) });
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(colPrice) });
        itemsTable.Columns.Add(new TableColumn { Width = new GridLength(colTotal) });

        var headerGroup = new TableRowGroup();
        var headerRow = new TableRow();
        void AddHdrCell(string text, TextAlignment align)
        {
            headerRow.Cells.Add(new TableCell(new Paragraph(new Run(text))
            {
                FontSize      = 8.5,
                FontWeight    = FontWeights.Bold,
                Foreground    = Brushes.Black,
                TextAlignment = align,
                Margin        = new Thickness(0)
            })
            {
                Padding         = new Thickness(0, 2, 2, 2),
                BorderBrush     = Brushes.Black,
                BorderThickness = new Thickness(0, 0, 0, 0.75)
            });
        }
        AddHdrCell("QTY", TextAlignment.Center);
        AddHdrCell("ITEM", TextAlignment.Left);
        AddHdrCell("PRICE", TextAlignment.Right);
        AddHdrCell("TOTAL", TextAlignment.Right);
        headerGroup.Rows.Add(headerRow);
        itemsTable.RowGroups.Add(headerGroup);

        var dataGroup = new TableRowGroup();
        var allItems80 = order.Items?.Where(i => !string.IsNullOrWhiteSpace(i.ProductName)).ToList() ?? new List<OrderItem>();
        // Consolidate identical items: same name+price+status+source → sum qty
        var consolidatedItems80 = allItems80
            .GroupBy(i => (
                Name: i.ProductName.Trim(),
                Unit: (i.Unit ?? "").Trim(),
                Price: i.ActualPrice ?? i.EstimatedPrice,
                Status: i.Status,
                Source: i.Source
            ))
            .Select(g => new {
                ProductName = g.Key.Name,
                Unit = g.Key.Unit,
                Price = g.Key.Price,
                Status = g.Key.Status,
                Source = g.Key.Source,
                Qty = g.Sum(i => i.Qty),
                CostPrice = g.First().CostPrice,
                PurchaseNotes = g.Select(i => i.PurchaseNotes ?? i.Notes).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            })
            .OrderBy(i => i.ProductName)
            .ToList();
        // validItems used for summary calculations
        var validItems = allItems80;

        foreach (var item in consolidatedItems80)
        {
            var total = item.Price * (decimal)item.Qty;
            var qtyStr = item.Qty % 1 == 0 ? $"{item.Qty:0}" : $"{item.Qty:0.##}";
            var isUnavailable = item.Status == ItemStatus.UNAVAILABLE;
            var isBoughtOnHand = item.Source == ItemSource.STOCK || item.Status == ItemStatus.BOUGHT_FROM_STOCK;
            var isReorder = item.Status == ItemStatus.REORDER;

            var row = new TableRow();

            // QTY — red if unavailable
            row.Cells.Add(new TableCell(new Paragraph(new Run(qtyStr))
            {
                FontSize      = 9.5,
                FontWeight    = FontWeights.Bold,
                Foreground    = isUnavailable ? Brushes.Red : Brushes.Black,
                TextAlignment = TextAlignment.Center,
                Margin        = new Thickness(0)
            }) { Padding = new Thickness(0, 2, 2, 2) });

            // ITEM NAME — red + status tag if unavailable/bought-on-hand/reorder
            var pName = new Paragraph()
            {
                FontSize   = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = isUnavailable ? Brushes.Red : Brushes.Black,
                Margin     = new Thickness(0)
            };
            pName.Inlines.Add(new Run(item.ProductName));
            if (isUnavailable)
            {
                pName.Inlines.Add(new Run(" [NOT AVAILABLE]")
                {
                    FontSize   = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Red
                });
            }
            else if (isBoughtOnHand)
            {
                pName.Inlines.Add(new Run(" [BOUGHT ON HAND]")
                {
                    FontSize   = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                });
            }
            else if (isReorder)
            {
                pName.Inlines.Add(new Run(" [REORDER]")
                {
                    FontSize   = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                });
            }
            if (item.CostPrice.HasValue && item.CostPrice.Value > item.Price)
            {
                var unitSavings = item.CostPrice.Value - item.Price;
                pName.Inlines.Add(new LineBreak());
                pName.Inlines.Add(new Run($"Orig: ₱{item.CostPrice.Value:N2} | Saved: ₱{unitSavings:N2}")
                {
                    FontSize   = 8,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black
                });
            }
            if (!string.IsNullOrWhiteSpace(item.PurchaseNotes))
            {
                pName.Inlines.Add(new LineBreak());
                pName.Inlines.Add(new Run($"* {item.PurchaseNotes}")
                {
                    FontSize   = 8,
                    FontStyle  = FontStyles.Italic,
                    Foreground = Brushes.Black
                });
            }
            row.Cells.Add(new TableCell(pName) { Padding = new Thickness(2, 2, 2, 2) });

            // PRICE — N/A if unavailable
            row.Cells.Add(new TableCell(new Paragraph(new Run(isUnavailable ? "—" : $"₱{item.Price:N2}"))
            {
                FontSize      = 9,
                FontWeight    = FontWeights.Bold,
                Foreground    = isUnavailable ? Brushes.Red : Brushes.Black,
                TextAlignment = TextAlignment.Right,
                Margin        = new Thickness(0)
            }) { Padding = new Thickness(0, 2, 2, 2) });

            // TOTAL — N/A if unavailable
            row.Cells.Add(new TableCell(new Paragraph(new Run(isUnavailable ? "—" : $"₱{total:N2}"))
            {
                FontSize      = 9,
                FontWeight    = FontWeights.Bold,
                Foreground    = isUnavailable ? Brushes.Red : Brushes.Black,
                TextAlignment = TextAlignment.Right,
                Margin        = new Thickness(0)
            }) { Padding = new Thickness(0, 2, 0, 2) });

            dataGroup.Rows.Add(row);
        }

        if (validItems.Count == 0)
        {
            var emptyRow = new TableRow();
            emptyRow.Cells.Add(new TableCell(new Paragraph(new Run("(No items recorded)"))
            {
                FontSize = 9, Foreground = Brushes.Black
            }) { ColumnSpan = 4, Padding = new Thickness(0, 4, 0, 4) });
            dataGroup.Rows.Add(emptyRow);
        }

        itemsTable.RowGroups.Add(dataGroup);
        doc.Blocks.Add(itemsTable);

        // ─── 4. SUMMARY (PURCHASE BUDGET, PASABUY FEE, ESTIMATED TOTAL) ───────
        doc.Blocks.Add(ThinDivider(vMargin: 3));

        decimal purchaseBudget = order.ItemsSubtotal;
        decimal pasabuyFee = order.PasabuyFee;
        decimal discount = order.Discount;
        decimal estimatedTotal = Math.Max(0m, purchaseBudget + pasabuyFee - discount);

        var sumTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 1, 0, 1) };
        double sumLblW = Math.Round(cw * 0.55);
        sumTable.Columns.Add(new TableColumn { Width = new GridLength(sumLblW) });
        sumTable.Columns.Add(new TableColumn { Width = new GridLength(cw - sumLblW) });

        var sumGroup = new TableRowGroup();
        sumGroup.Rows.Add(TwoColRow("PURCHASE BUDGET:", $"₱{purchaseBudget:N2}", bold: true, fontSize: 9.5));
        sumGroup.Rows.Add(TwoColRow("PASABUY FEE:", $"₱{pasabuyFee:N2}", bold: true, fontSize: 9.5));
        if (discount > 0)
        {
            sumGroup.Rows.Add(TwoColRow("DISCOUNT:", $"-₱{discount:N2}", bold: true, fontSize: 9.5, fg: Brushes.Black));
        }
        var totalSavings80 = validItems
            .Where(i => i.CostPrice.HasValue && i.CostPrice.Value > (i.ActualPrice ?? i.EstimatedPrice))
            .Sum(i => (i.CostPrice!.Value - (i.ActualPrice ?? i.EstimatedPrice)) * (decimal)i.Qty);
        if (totalSavings80 > 0)
        {
            sumGroup.Rows.Add(TwoColRow("TOTAL SAVINGS:", $"₱{totalSavings80:N2}", bold: true, fontSize: 9.5, fg: Brushes.Black));
        }
        sumTable.RowGroups.Add(sumGroup);
        doc.Blocks.Add(sumTable);

        // Prominent ESTIMATED TOTAL
        doc.Blocks.Add(ThinDivider(vMargin: 3, thickness: 1.5));

        var estTable = new Table { CellSpacing = 0 };
        estTable.Columns.Add(new TableColumn { Width = new GridLength(sumLblW) });
        estTable.Columns.Add(new TableColumn { Width = new GridLength(cw - sumLblW) });
        var estGroup = new TableRowGroup();
        var estRow = new TableRow();

        estRow.Cells.Add(new TableCell(new Paragraph(new Run("ESTIMATED TOTAL:"))
        {
            FontSize   = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            Margin     = new Thickness(0)
        }) { Padding = new Thickness(0, 2, 0, 2) });

        estRow.Cells.Add(new TableCell(new Paragraph(new Run($"₱{estimatedTotal:N2}"))
        {
            FontSize      = 13.5,
            FontWeight    = FontWeights.Bold,
            Foreground    = Brushes.Black,
            TextAlignment = TextAlignment.Right,
            Margin        = new Thickness(0)
        }) { Padding = new Thickness(0, 1, 0, 1) });

        estGroup.Rows.Add(estRow);
        estTable.RowGroups.Add(estGroup);
        doc.Blocks.Add(estTable);

        doc.Blocks.Add(ThinDivider(vMargin: 3, thickness: 1.0));

        // ─── 5. PO STATUS ─────────────────────────────────────────────────────
        var statusTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 1, 0, 1) };
        statusTable.Columns.Add(new TableColumn { Width = new GridLength(sumLblW) });
        statusTable.Columns.Add(new TableColumn { Width = new GridLength(cw - sumLblW) });
        var statusGroup = new TableRowGroup();
        statusGroup.Rows.Add(TwoColRow("PO STATUS:", FormatOrderStatus(order.Status), bold: true, fontSize: 10));
        statusTable.RowGroups.Add(statusGroup);
        doc.Blocks.Add(statusTable);

        doc.Blocks.Add(ThinDivider(vMargin: 3));

        // ─── 6. PAYMENT SECTION (PAID / UNPAID, Method, Date, Reference) ──────
        string payStatus = FormatPaymentStatus(order, estimatedTotal);
        var payTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 1, 0, 1) };
        payTable.Columns.Add(new TableColumn { Width = new GridLength(sumLblW) });
        payTable.Columns.Add(new TableColumn { Width = new GridLength(cw - sumLblW) });
        var payGroup = new TableRowGroup();

        payGroup.Rows.Add(TwoColRow("PAYMENT STATUS:", payStatus, bold: true, fontSize: 10,
            fg: Brushes.Black));

        var lastPayment = order.Payments?.OrderByDescending(p => p.PaymentDate ?? p.CreatedAt).FirstOrDefault();
        if (payStatus != "UNPAID")
        {
            if (lastPayment != null)
            {
                payGroup.Rows.Add(TwoColRow("PAYMENT METHOD:", FormatPaymentMethod(lastPayment.Method), bold: true, fontSize: 9.5));
            }
            payGroup.Rows.Add(TwoColRow("AMOUNT PAID:", $"₱{order.TotalPaid:N2}", bold: true, fontSize: 9.5));

            if (payStatus == "PARTIAL")
            {
                var bal = Math.Max(0m, estimatedTotal - order.TotalPaid);
                payGroup.Rows.Add(TwoColRow("BALANCE DUE:", $"₱{bal:N2}", bold: true, fontSize: 9.5, fg: Brushes.Black));
            }

            var payDate = lastPayment?.PaymentDate ?? lastPayment?.CreatedAt;
            if (payDate.HasValue)
            {
                payGroup.Rows.Add(TwoColRow("PAYMENT DATE:", payDate.Value.ToString("MMM dd, yyyy"), fontSize: 9));
            }
            // REF # kept in DB — not printed on customer-facing receipt.
        }
        else
        {
            payGroup.Rows.Add(TwoColRow("BALANCE DUE:", $"₱{estimatedTotal:N2}", bold: true, fontSize: 9.5, fg: Brushes.Black));
        }

        payTable.RowGroups.Add(payGroup);
        doc.Blocks.Add(payTable);

        doc.Blocks.Add(ThinDivider(vMargin: 3));

        // ─── 7. FULFILLMENT SECTION ───────────────────────────────────────────
        var fulTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 1, 0, 1) };
        fulTable.Columns.Add(new TableColumn { Width = new GridLength(sumLblW) });
        fulTable.Columns.Add(new TableColumn { Width = new GridLength(cw - sumLblW) });
        var fulGroup = new TableRowGroup();
        fulGroup.Rows.Add(TwoColRow("FULFILLMENT:", FormatFulfillmentStatus(order), bold: true, fontSize: 9.5));
        if (order.DeliveryType == DeliveryType.DELIVERY && !string.IsNullOrWhiteSpace(order.Address))
        {
            fulGroup.Rows.Add(TwoColRow("ADDRESS:", order.Address, fontSize: 8.5));
        }
        fulTable.RowGroups.Add(fulGroup);
        doc.Blocks.Add(fulTable);

        doc.Blocks.Add(ThinDivider(vMargin: 3));

        // ─── 8. FOOTER ────────────────────────────────────────────────────────
        string footer = string.IsNullOrWhiteSpace(footerMsg) || footerMsg.Contains("Thank you for using")
            ? "Thank you for trusting DIONIX POS!"
            : footerMsg;

        var pFooter = new Paragraph(new Run(footer))
        {
            FontSize      = 9.5,
            FontWeight    = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Foreground    = Brushes.Black,
            Margin        = new Thickness(0, 3, 0, 1)
        };
        doc.Blocks.Add(pFooter);

        var pPrinted = new Paragraph(new Run($"Printed: {DateTime.Now:MMM dd, yyyy  h:mm tt}"))
        {
            FontSize      = 8,
            TextAlignment = TextAlignment.Center,
            Foreground    = Brushes.Black,
            Margin        = new Thickness(0, 0, 0, 4)
        };
        doc.Blocks.Add(pPrinted);

        return doc;
    }

    /// <summary>
    /// Builds a dedicated compact 58mm thermal receipt FlowDocument.
    /// Utilizes a dedicated 2-line item structure (Product on line 1, Qty @ Price and Total on line 2)
    /// to avoid cramped columns or truncated text on narrow 58mm paper rolls.
    /// </summary>
    public static FlowDocument Create58mmThermalReceiptDocument(
        Order order,
        string businessName = "DIONIX POS",
        string footerMsg = "Thank you for trusting DIONIX POS!",
        double pageWidth = 219,
        Thickness? padding = null)
    {
        var pad = padding ?? new Thickness(4, 8, 4, 8);
        var font = new FontFamily("Segoe UI, Arial, sans-serif");
        var doc = new FlowDocument
        {
            PageWidth   = pageWidth,
            PagePadding = pad,
            ColumnWidth = double.PositiveInfinity,
            Background  = Brushes.White,
            Foreground  = Brushes.Black,
            FontFamily  = font
        };

        double cw = Math.Max(170, pageWidth - pad.Left - pad.Right);

        Paragraph ThinDivider(double vMargin = 3, double thickness = 0.75)
        {
            return new Paragraph
            {
                BorderBrush     = Brushes.Black,
                BorderThickness = new Thickness(0, thickness, 0, 0),
                Margin          = new Thickness(0, vMargin, 0, vMargin)
            };
        }

        TableRow TwoColRow(string label, string value, bool bold = false, double fontSize = 9, Brush? fg = null)
        {
            var r = new TableRow();
            var fw = bold ? FontWeights.Bold : FontWeights.Normal;
            r.Cells.Add(new TableCell(new Paragraph(new Run(label))
            {
                FontSize   = fontSize,
                FontWeight = fw,
                Foreground = fg ?? Brushes.Black,
                Margin     = new Thickness(0)
            }) { Padding = new Thickness(0, 1, 2, 1) });

            r.Cells.Add(new TableCell(new Paragraph(new Run(value))
            {
                FontSize      = fontSize,
                FontWeight    = fw,
                Foreground    = fg ?? Brushes.Black,
                TextAlignment = TextAlignment.Right,
                Margin        = new Thickness(0)
            }) { Padding = new Thickness(0, 1, 0, 1) });

            return r;
        }

        // 1. HEADER
        var profile = BusinessProfileService.GetProfileById(order.BusinessId) ?? BusinessProfileService.ActiveProfile;
        // Logo — smaller for 58mm paper
        var logoBlock58 = BuildLogoBlock(cw, maxHeight: 38, profile.LogoResource);
        if (logoBlock58 != null) doc.Blocks.Add(logoBlock58);

        var pHeader = new Paragraph { Margin = new Thickness(0, 1, 0, 2), TextAlignment = TextAlignment.Center };
        pHeader.Inlines.Add(new Run($"{profile.SystemName}\n") { FontSize = 13.5, FontWeight = FontWeights.Bold });
        pHeader.Inlines.Add(new Run($"{profile.Subtitle}\n") { FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pHeader.Inlines.Add(new Run("PURCHASE ORDER\n") { FontSize = 9, FontWeight = FontWeights.SemiBold });
        doc.Blocks.Add(pHeader);

        doc.Blocks.Add(ThinDivider(vMargin: 2, thickness: 1.0));

        // 2. METADATA
        var metaTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 1, 0, 1) };
        double metaLblW = Math.Round(cw * 0.42);
        metaTable.Columns.Add(new TableColumn { Width = new GridLength(metaLblW) });
        metaTable.Columns.Add(new TableColumn { Width = new GridLength(cw - metaLblW) });
        var metaGroup = new TableRowGroup();
        metaGroup.Rows.Add(TwoColRow("PO #:", order.OrderNo, bold: true, fontSize: 9.5));
        metaGroup.Rows.Add(TwoColRow("Date:", order.CreatedAt.ToString("MM/dd/yy  h:mm tt")));
        metaGroup.Rows.Add(TwoColRow("Customer:", order.Customer?.Name ?? "Walk-in", bold: true));
        // Buying Day kept in DB — not printed on customer-facing receipt.
        metaTable.RowGroups.Add(metaGroup);
        doc.Blocks.Add(metaTable);

        doc.Blocks.Add(ThinDivider(vMargin: 2));

        // 3. ITEMS (Dedicated 2-Line Structure) — consolidated by product name+price
        var allItems58 = order.Items?.Where(i => !string.IsNullOrWhiteSpace(i.ProductName)).ToList() ?? new List<OrderItem>();
        var consolidatedItems58 = allItems58
            .GroupBy(i => (
                Name: i.ProductName.Trim(),
                Unit: (i.Unit ?? "").Trim(),
                Price: i.ActualPrice ?? i.EstimatedPrice,
                Status: i.Status,
                Source: i.Source
            ))
            .Select(g => new {
                ProductName = g.Key.Name,
                Unit = g.Key.Unit,
                Price = g.Key.Price,
                Status = g.Key.Status,
                Source = g.Key.Source,
                Qty = g.Sum(i => i.Qty),
                CostPrice = g.First().CostPrice,
                PurchaseNotes = g.Select(i => i.PurchaseNotes ?? i.Notes).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            })
            .OrderBy(i => i.ProductName)
            .ToList();
        // validItems for summary section below
        var validItems = allItems58;

        foreach (var item in consolidatedItems58)
        {
            var total = item.Price * (decimal)item.Qty;
            var qtyStr = item.Qty % 1 == 0 ? $"{item.Qty:0}" : $"{item.Qty:0.##}";
            var isUnavailable = item.Status == ItemStatus.UNAVAILABLE;
            var isBoughtOnHand = item.Source == ItemSource.STOCK || item.Status == ItemStatus.BOUGHT_FROM_STOCK;
            var isReorder = item.Status == ItemStatus.REORDER;

            // Line 1: Item Name
            var pItem = new Paragraph()
            {
                FontSize   = 9,
                FontWeight = FontWeights.Bold,
                Foreground = isUnavailable ? Brushes.Red : Brushes.Black,
                Margin     = new Thickness(0, 2, 0, 0)
            };
            pItem.Inlines.Add(new Run(item.ProductName));
            if (isUnavailable)
            {
                pItem.Inlines.Add(new Run(" [NOT AVAILABLE]")
                {
                    FontSize   = 7.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Red
                });
            }
            else if (isBoughtOnHand)
            {
                pItem.Inlines.Add(new Run(" [BOUGHT ON HAND]")
                {
                    FontSize   = 7.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                });
            }
            else if (isReorder)
            {
                pItem.Inlines.Add(new Run(" [REORDER]")
                {
                    FontSize   = 7.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                });
            }
            if (item.CostPrice.HasValue && item.CostPrice.Value > item.Price)
            {
                var unitSavings = item.CostPrice.Value - item.Price;
                pItem.Inlines.Add(new LineBreak());
                pItem.Inlines.Add(new Run($"Orig: ₱{item.CostPrice.Value:N2} | Saved: ₱{unitSavings:N2}")
                {
                    FontSize   = 7.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black
                });
            }
            doc.Blocks.Add(pItem);

            // Line 2: Qty x Price and Total
            var lineTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 1) };
            lineTable.Columns.Add(new TableColumn { Width = new GridLength(cw * 0.60) });
            lineTable.Columns.Add(new TableColumn { Width = new GridLength(cw * 0.40) });
            var lg = new TableRowGroup();
            var lr = new TableRow();

            var leftText = $"  {qtyStr} x ₱{item.Price:N2}";
            var rightText = isUnavailable ? "[N/A]" : $"₱{total:N2}";

            lr.Cells.Add(new TableCell(new Paragraph(new Run(leftText))
            {
                FontSize   = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = isUnavailable ? Brushes.Red : Brushes.Black,
                Margin     = new Thickness(0)
            }) { Padding = new Thickness(0) });

            lr.Cells.Add(new TableCell(new Paragraph(new Run(rightText))
            {
                FontSize      = 8.5,
                FontWeight    = FontWeights.Bold,
                Foreground    = isUnavailable ? Brushes.Red : Brushes.Black,
                TextAlignment = TextAlignment.Right,
                Margin        = new Thickness(0)
            }) { Padding = new Thickness(0) });

            lg.Rows.Add(lr);
            lineTable.RowGroups.Add(lg);
            doc.Blocks.Add(lineTable);

            if (!string.IsNullOrWhiteSpace(item.PurchaseNotes))
            {
                var pNote = new Paragraph(new Run($"  * {item.PurchaseNotes}"))
                {
                    FontSize   = 7.5,
                    FontStyle  = FontStyles.Italic,
                    Foreground = Brushes.Black,
                    Margin     = new Thickness(0, 0, 0, 1)
                };
                doc.Blocks.Add(pNote);
            }
        }

        if (validItems.Count == 0)
        {
            doc.Blocks.Add(new Paragraph(new Run("(No items recorded)")) { FontSize = 8.5, Margin = new Thickness(0, 2, 0, 2) });
        }


        // 4. SUMMARY
        doc.Blocks.Add(ThinDivider(vMargin: 2));

        decimal purchaseBudget = order.ItemsSubtotal;
        decimal pasabuyFee = order.PasabuyFee;
        decimal discount = order.Discount;
        decimal estimatedTotal = Math.Max(0m, purchaseBudget + pasabuyFee - discount);

        var sumTable = new Table { CellSpacing = 0, Margin = new Thickness(0, 1, 0, 1) };
        double sumLblW = Math.Round(cw * 0.58);
        sumTable.Columns.Add(new TableColumn { Width = new GridLength(sumLblW) });
        sumTable.Columns.Add(new TableColumn { Width = new GridLength(cw - sumLblW) });
        var sumGroup = new TableRowGroup();
        sumGroup.Rows.Add(TwoColRow("PURCHASE BUDGET:", $"₱{purchaseBudget:N2}", bold: true, fontSize: 8.5));
        sumGroup.Rows.Add(TwoColRow("PASABUY FEE:", $"₱{pasabuyFee:N2}", bold: true, fontSize: 8.5));
        if (discount > 0)
        {
            sumGroup.Rows.Add(TwoColRow("DISCOUNT:", $"-₱{discount:N2}", bold: true, fontSize: 8.5, fg: Brushes.Black));
        }
        var totalSavings58 = validItems
            .Where(i => i.CostPrice.HasValue && i.CostPrice.Value > (i.ActualPrice ?? i.EstimatedPrice))
            .Sum(i => (i.CostPrice!.Value - (i.ActualPrice ?? i.EstimatedPrice)) * (decimal)i.Qty);
        if (totalSavings58 > 0)
        {
            sumGroup.Rows.Add(TwoColRow("TOTAL SAVINGS:", $"₱{totalSavings58:N2}", bold: true, fontSize: 8.5, fg: Brushes.Black));
        }
        sumTable.RowGroups.Add(sumGroup);
        doc.Blocks.Add(sumTable);

        doc.Blocks.Add(ThinDivider(vMargin: 2, thickness: 1.2));

        var estTable = new Table { CellSpacing = 0 };
        estTable.Columns.Add(new TableColumn { Width = new GridLength(sumLblW) });
        estTable.Columns.Add(new TableColumn { Width = new GridLength(cw - sumLblW) });
        var estGroup = new TableRowGroup();
        var estRow = new TableRow();
        estRow.Cells.Add(new TableCell(new Paragraph(new Run("ESTIMATED TOTAL:"))
        {
            FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(0)
        }) { Padding = new Thickness(0, 1, 0, 1) });
        estRow.Cells.Add(new TableCell(new Paragraph(new Run($"₱{estimatedTotal:N2}"))
        {
            FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, TextAlignment = TextAlignment.Right, Margin = new Thickness(0)
        }) { Padding = new Thickness(0, 1, 0, 1) });
        estGroup.Rows.Add(estRow);
        estTable.RowGroups.Add(estGroup);
        doc.Blocks.Add(estTable);

        doc.Blocks.Add(ThinDivider(vMargin: 2));

        // 5. PO STATUS
        var statTable = new Table { CellSpacing = 0 };
        statTable.Columns.Add(new TableColumn { Width = new GridLength(sumLblW) });
        statTable.Columns.Add(new TableColumn { Width = new GridLength(cw - sumLblW) });
        var statGroup = new TableRowGroup();
        statGroup.Rows.Add(TwoColRow("PO STATUS:", FormatOrderStatus(order.Status), bold: true, fontSize: 9));
        statTable.RowGroups.Add(statGroup);
        doc.Blocks.Add(statTable);

        doc.Blocks.Add(ThinDivider(vMargin: 2));

        // 6. PAYMENT SECTION
        string payStatus = FormatPaymentStatus(order, estimatedTotal);
        var payTable = new Table { CellSpacing = 0 };
        payTable.Columns.Add(new TableColumn { Width = new GridLength(sumLblW) });
        payTable.Columns.Add(new TableColumn { Width = new GridLength(cw - sumLblW) });
        var payGroup = new TableRowGroup();
        payGroup.Rows.Add(TwoColRow("PAYMENT STATUS:", payStatus, bold: true, fontSize: 9,
            fg: Brushes.Black));

        var lastPayment = order.Payments?.OrderByDescending(p => p.PaymentDate ?? p.CreatedAt).FirstOrDefault();
        if (payStatus != "UNPAID")
        {
            if (lastPayment != null)
                payGroup.Rows.Add(TwoColRow("METHOD:", FormatPaymentMethod(lastPayment.Method), fontSize: 8.5));
            payGroup.Rows.Add(TwoColRow("AMOUNT PAID:", $"₱{order.TotalPaid:N2}", bold: true, fontSize: 8.5));
            if (payStatus == "PARTIAL")
            {
                var bal = Math.Max(0m, estimatedTotal - order.TotalPaid);
                payGroup.Rows.Add(TwoColRow("BALANCE DUE:", $"₱{bal:N2}", bold: true, fontSize: 8.5, fg: Brushes.Black));
            }
            var payDate = lastPayment?.PaymentDate ?? lastPayment?.CreatedAt;
            if (payDate.HasValue)
                payGroup.Rows.Add(TwoColRow("DATE:", payDate.Value.ToString("MM/dd/yy"), fontSize: 8));
        }
        else
        {
            payGroup.Rows.Add(TwoColRow("BALANCE DUE:", $"₱{estimatedTotal:N2}", bold: true, fontSize: 8.5, fg: Brushes.Black));
        }
        payTable.RowGroups.Add(payGroup);
        doc.Blocks.Add(payTable);

        doc.Blocks.Add(ThinDivider(vMargin: 2));

        // 7. FULFILLMENT
        var fulTable = new Table { CellSpacing = 0 };
        fulTable.Columns.Add(new TableColumn { Width = new GridLength(sumLblW) });
        fulTable.Columns.Add(new TableColumn { Width = new GridLength(cw - sumLblW) });
        var fulGroup = new TableRowGroup();
        fulGroup.Rows.Add(TwoColRow("FULFILLMENT:", FormatFulfillmentStatus(order), bold: true, fontSize: 8.5));
        fulTable.RowGroups.Add(fulGroup);
        doc.Blocks.Add(fulTable);

        doc.Blocks.Add(ThinDivider(vMargin: 2));

        // 8. FOOTER
        string footer = string.IsNullOrWhiteSpace(footerMsg) || footerMsg.Contains("Thank you for using")
            ? "Thank you for trusting DIONIX POS!"
            : footerMsg;
        doc.Blocks.Add(new Paragraph(new Run(footer))
        {
            FontSize = 8.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 1)
        });
        doc.Blocks.Add(new Paragraph(new Run($"Printed: {DateTime.Now:MM/dd/yy h:mm tt}"))
        {
            FontSize = 7.5, TextAlignment = TextAlignment.Center, Foreground = Brushes.Black, Margin = new Thickness(0)
        });

        return doc;
    }

    /// <summary>
    /// Router method for Thermal Purchase Order printing and preview.
    /// Automatically selects 58mm dedicated compact layout or 80mm standard receipt layout.
    /// </summary>
    public static FlowDocument CreateThermalPurchaseOrderFlowDocument(
        Order order,
        string businessName = "DIONIX POS",
        string footerMsg = "Thank you for trusting DIONIX POS!",
        double pageWidth = 280,
        Thickness? padding = null)
    {
        if (pageWidth <= 240)
        {
            return Create58mmThermalReceiptDocument(order, businessName, footerMsg, pageWidth, padding);
        }
        return Create80mmThermalReceiptDocument(order, businessName, footerMsg, pageWidth, padding);
    }

    public static FlowDocument CreateThermalBuyingChecklistDocument(
        List<OrderItem> items,
        string storeName = "All Stores",
        string buyingDay = "All Days",
        DateTime? printDate = null,
        double pageWidth = 280,
        Thickness? padding = null)
    {
        var pad = padding ?? new Thickness(10, 10, 10, 10);
        var font = new FontFamily("Segoe UI, Segoe UI Symbol, Consolas, sans-serif");
        var doc = new FlowDocument
        {
            PageWidth   = pageWidth,
            PagePadding = pad,
            ColumnWidth = double.PositiveInfinity,
            Background  = Brushes.White,
            Foreground  = Brushes.Black,
            FontFamily  = font
        };

        // Logo — compact for thermal paper
        double cwTherm = Math.Max(170, pageWidth - pad.Left - pad.Right);
        var logoBlockTherm = BuildLogoBlock(cwTherm, maxHeight: 40);
        if (logoBlockTherm != null) doc.Blocks.Add(logoBlockTherm);

        var pHeader = new Paragraph { Margin = new Thickness(0, 1, 0, 4), TextAlignment = TextAlignment.Center };
        pHeader.Inlines.Add(new Run("DIONIX POS\n") { FontSize = 13, FontWeight = FontWeights.Black });
        pHeader.Inlines.Add(new Run("ANTIQUE PASABUY\n") { FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Hex("#452B86") });
        pHeader.Inlines.Add(new Run("SHOPPING CHECKLIST\n") { FontSize = 11, FontWeight = FontWeights.Bold });
        doc.Blocks.Add(pHeader);

        doc.Blocks.Add(new Paragraph { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 6) });

        // Meta + Summary Stats
        int totalItemsTh    = items.Count;
        double totalUnitsTh = items.Sum(i => i.Qty);
        decimal estBudgetTh = items.Sum(i => (decimal)i.Qty * i.EstimatedPrice);
        var targetDate = printDate ?? DateTime.Today;
        var pMeta = new Paragraph { Margin = new Thickness(0, 0, 0, 6), LineHeight = 15, FontSize = 9.5, TextAlignment = TextAlignment.Center };
        pMeta.Inlines.Add(new Run($"Date: {targetDate:MMM dd, yyyy}  |  Store: {storeName}\n"));
        pMeta.Inlines.Add(new Run($"Printed: {DateTime.Now:h:mm tt}\n"));
        pMeta.Inlines.Add(new Run($"Items: {totalItemsTh}  |  Qty: {totalUnitsTh:0.##}  |  Est: \u20b1{estBudgetTh:N2}\n") { FontWeight = FontWeights.SemiBold });
        doc.Blocks.Add(pMeta);

        doc.Blocks.Add(new Paragraph { BorderBrush = Hex("#94A3B8"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 2, 0, 6) });

        var storeGroups = items
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Store) ? "General Store" : i.Store)
            .OrderBy(g => g.Key);

        foreach (var group in storeGroups)
        {
            var pStore = new Paragraph(new Run($"STORE: {group.Key.ToUpper()}"))
            {
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Hex("#452B86"),
                Margin = new Thickness(0, 6, 0, 2)
            };
            doc.Blocks.Add(pStore);

            // Consolidate same product within the same store
            var consolidated = group
                .GroupBy(i => (
                    Name: i.ProductName.Trim(),
                    Unit: (i.Unit ?? "").Trim(),
                    EstPrice: i.EstimatedPrice
                ))
                .Select(g2 => new {
                    ProductName = g2.Key.Name,
                    Unit = g2.Key.Unit,
                    EstimatedPrice = g2.Key.EstPrice,
                    Qty = g2.Sum(i => i.Qty),
                    Customers = g2.Select(i => i.Order?.Customer?.Name ?? "Walk-in").Distinct().ToList(),
                    Orders = g2.Select(i => i.Order?.OrderNo ?? "N/A").Distinct().ToList(),
                    Notes = g2.Select(i => i.Notes).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList()
                })
                .OrderBy(x => x.ProductName)
                .ToList();

            foreach (var item in consolidated)
            {
                var pItem = new Paragraph { Margin = new Thickness(4, 2, 0, 4), LineHeight = 15, FontSize = 9.5 };
                var qtyStr = item.Qty % 1 == 0 ? $"{item.Qty:0}" : $"{item.Qty:0.##}";
                pItem.Inlines.Add(new Run($"☐ {item.ProductName}\n") { FontWeight = FontWeights.SemiBold, FontSize = 10 });
                pItem.Inlines.Add(new Run($"  Qty: {qtyStr} {item.Unit}  |  Est: ₱{item.EstimatedPrice:N2}  (×{item.Orders.Count} order{(item.Orders.Count != 1 ? "s" : "")})\n"));
                var custLine = string.Join(", ", item.Customers);
                var poLine = string.Join(", ", item.Orders);
                pItem.Inlines.Add(new Run($"  Customers: {custLine}  |  PO: {poLine}\n") { Foreground = Hex("#475569") });
                foreach (var note in item.Notes)
                {
                    pItem.Inlines.Add(new Run($"  Note: {note}\n") { FontStyle = FontStyles.Italic, Foreground = Hex("#64748B") });
                }
                doc.Blocks.Add(pItem);
            }
        }

        doc.Blocks.Add(new Paragraph { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 6, 0, 4) });

        var totalBudget = items.Sum(i => (decimal)i.Qty * i.EstimatedPrice);
        var totalChecklistUnits = items.Sum(i => i.Qty);
        var pSummary = new Paragraph { Margin = new Thickness(0, 2, 0, 4), LineHeight = 16, FontSize = 10.5, TextAlignment = TextAlignment.Right };
        pSummary.Inlines.Add(new Run($"Total: {totalChecklistUnits:0.##} units across {items.Count} line items\n") { FontWeight = FontWeights.SemiBold });
        pSummary.Inlines.Add(new Run($"ESTIMATED BUDGET: ₱{totalBudget:N2}\n") { FontWeight = FontWeights.Bold, FontSize = 11.5, Foreground = Hex("#452B86") });
        doc.Blocks.Add(pSummary);

        return doc;
    }

    /// <summary>
    /// Builds a Purchase Orders summary FlowDocument for Print All POs or Print Filtered POs.
    /// </summary>
    public static FlowDocument CreateOrdersSummaryFlowDocument(
        List<Order> orders,
        string title = "Purchase Orders Summary",
        double pageWidth = 793.7,
        Thickness? padding = null,
        bool isThermal = false)
    {
        var pad = padding ?? (isThermal ? new Thickness(10) : new Thickness(36, 30, 36, 30));
        var doc = new FlowDocument
        {
            PageWidth   = pageWidth,
            PagePadding = pad,
            ColumnWidth = double.PositiveInfinity,
            Background  = Brushes.White,
            Foreground  = Brushes.Black,
            FontFamily  = new FontFamily("Segoe UI, Arial, sans-serif")
        };

        if (isThermal)
        {
            var pHead = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            pHead.Inlines.Add(new Run("DIONIX POS\n") { FontSize = 13, FontWeight = FontWeights.Black });
            pHead.Inlines.Add(new Run("ANTIQUE PASABUY\n") { FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Hex("#452B86") });
            pHead.Inlines.Add(new Run($"{title.ToUpper()}\n") { FontSize = 11, FontWeight = FontWeights.Bold });
            pHead.Inlines.Add(new Run($"Date: {DateTime.Now:MMM dd, yyyy  h:mm tt}  |  Total: {orders.Count} POs\n") { FontSize = 9, Foreground = Hex("#475569") });
            doc.Blocks.Add(pHead);

            doc.Blocks.Add(new Paragraph { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 6) });

            foreach (var o in orders)
            {
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 4), LineHeight = 15, FontSize = 9.5 };
                p.Inlines.Add(new Run($"PO: {o.OrderNo}  [{o.Status}]\n") { FontWeight = FontWeights.Bold });
                p.Inlines.Add(new Run($"  Customer: {o.Customer?.Name ?? "Walk-in"}\n"));
                p.Inlines.Add(new Run($"  Day: {o.BuyingDay}  |  Items: {o.Items?.Count ?? 0}  |  Est: ₱{o.GrandTotal:N2}\n") { Foreground = Hex("#334155") });
                doc.Blocks.Add(p);
            }

            doc.Blocks.Add(new Paragraph { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 4) });
            var pSum = new Paragraph { TextAlignment = TextAlignment.Right, FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0) };
            pSum.Inlines.Add(new Run($"TOTAL POs: {orders.Count}  |  SUM: ₱{orders.Sum(o => o.GrandTotal):N2}") { FontWeight = FontWeights.Bold, Foreground = Hex("#452B86") });
            doc.Blocks.Add(pSum);
            return doc;
        }

        // Standard Wide / A4 / Letter table
        double cw = Math.Max(400, pageWidth - pad.Left - pad.Right);
        var pHeader = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
        pHeader.Inlines.Add(new Run("DIONIX POS\n") { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.Black });
        pHeader.Inlines.Add(new Run("ANTIQUE PASABUY & RETAIL\n") { FontSize = 10, Foreground = Hex("#334155") });
        pHeader.Inlines.Add(new Run($"{title.ToUpper()}\n") { FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Hex("#0F172A") });
        pHeader.Inlines.Add(new Run($"Printed: {DateTime.Now:yyyy-MM-dd h:mm tt}    |    Total Orders: {orders.Count}    |    Grand Total: ₱{orders.Sum(o => o.GrandTotal):N2}\n") { FontSize = 9.5, Foreground = Hex("#475569") });
        doc.Blocks.Add(pHeader);

        doc.Blocks.Add(new Paragraph { BorderBrush = Hex("#000000"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 10) });

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 8) };
        double wPO = 100;
        double wCust = 160;
        double wItems = 60;
        double wDay = 100;
        double wTotal = 110;
        double wStatus = 130;
        double wDate = Math.Max(90, cw - wPO - wCust - wItems - wDay - wTotal - wStatus);

        table.Columns.Add(new TableColumn { Width = new GridLength(wPO) });
        table.Columns.Add(new TableColumn { Width = new GridLength(wCust) });
        table.Columns.Add(new TableColumn { Width = new GridLength(wItems) });
        table.Columns.Add(new TableColumn { Width = new GridLength(wDay) });
        table.Columns.Add(new TableColumn { Width = new GridLength(wTotal) });
        table.Columns.Add(new TableColumn { Width = new GridLength(wStatus) });
        table.Columns.Add(new TableColumn { Width = new GridLength(wDate) });

        var thg = new TableRowGroup();
        var thr = new TableRow();
        void AddTh(string txt, TextAlignment align)
        {
            thr.Cells.Add(new TableCell(new Paragraph(new Run(txt))
            {
                FontSize = 9.5, FontWeight = FontWeights.Bold, TextAlignment = align, Margin = new Thickness(0)
            })
            {
                Padding = new Thickness(4, 5, 4, 5),
                BorderBrush = Hex("#94A3B8"),
                BorderThickness = new Thickness(0, 1, 0, 1)
            });
        }
        AddTh("PO #", TextAlignment.Left);
        AddTh("CUSTOMER", TextAlignment.Left);
        AddTh("ITEMS", TextAlignment.Center);
        AddTh("SHOPPING DAY", TextAlignment.Center);
        AddTh("EST. TOTAL", TextAlignment.Right);
        AddTh("STATUS", TextAlignment.Center);
        AddTh("DATE", TextAlignment.Center);
        thg.Rows.Add(thr);
        table.RowGroups.Add(thg);

        var bg = new TableRowGroup();
        foreach (var o in orders)
        {
            var tr = new TableRow();
            void AddTd(string txt, TextAlignment align, FontWeight? fw = null, Brush? fg = null)
            {
                tr.Cells.Add(new TableCell(new Paragraph(new Run(txt))
                {
                    FontSize = 9.5, FontWeight = fw ?? FontWeights.Normal, Foreground = fg ?? Brushes.Black, TextAlignment = align, Margin = new Thickness(0)
                })
                {
                    Padding = new Thickness(4, 4, 4, 4),
                    BorderBrush = Hex("#E2E8F0"),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                });
            }
            AddTd(o.OrderNo, TextAlignment.Left, fw: FontWeights.SemiBold);
            AddTd(o.Customer?.Name ?? "Walk-in", TextAlignment.Left);
            AddTd((o.Items?.Count ?? 0).ToString(), TextAlignment.Center);
            AddTd(o.BuyingDay, TextAlignment.Center);
            AddTd($"₱{o.GrandTotal:N2}", TextAlignment.Right, fw: FontWeights.SemiBold);
            AddTd(o.Status.ToString(), TextAlignment.Center, fw: FontWeights.Bold, fg: Hex("#452B86"));
            AddTd(o.CreatedAt.ToString("MMM dd, yyyy"), TextAlignment.Center);
            bg.Rows.Add(tr);
        }
        table.RowGroups.Add(bg);
        doc.Blocks.Add(table);

        var pSummary = new Paragraph { TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        pSummary.Inlines.Add(new Run($"TOTAL ORDERS: {orders.Count}    |    TOTAL ESTIMATED AMOUNT: ₱{orders.Sum(o => o.GrandTotal):N2}") { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Hex("#452B86") });
        doc.Blocks.Add(pSummary);

        return doc;
    }

    /// <summary>
    /// Builds a traditional, professional A4 business report FlowDocument for printing or PDF export.
    /// Features clean standard header (Business, Report Name, Date Range, Store), thin borders,
    /// subtle blue headers, black text, repeating table headers, and clean summary rows.
    /// </summary>
    public static FlowDocument CreateRichReportDocument(
        string reportType,
        string reportTitle,
        string dateRange,
        string storeFilter,
        List<dynamic> rows,
        Dictionary<string, string> summaryTotals,
        double pageWidth = 1080) // Landscape width so wide columns breathe without truncation
    {
        double padH = 36;
        double padV = 30;
        var sansFont = new FontFamily("Segoe UI, Arial, sans-serif");
        var monoFont = new FontFamily("Consolas, 'Courier New', monospace");

        var doc = new FlowDocument
        {
            PageWidth   = pageWidth,
            PagePadding = new Thickness(padH, padV, padH, padV),
            ColumnWidth = double.PositiveInfinity,
            Background  = Brushes.White,
            Foreground  = Brushes.Black,
            FontFamily  = sansFont
        };

        double cw = Math.Max(500, pageWidth - padH * 2);

        // ─── 1. HEADER (Traditional Business Document) ────────────────────────
        var pHeader = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
        pHeader.Inlines.Add(new Run("DIONIX POS\n")
        {
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black
        });
        pHeader.Inlines.Add(new Run("Antique Pasabuy & Retail\n")
        {
            FontSize = 10,
            Foreground = Hex("#334155")
        });
        pHeader.Inlines.Add(new Run($"{reportTitle.ToUpper()}\n")
        {
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Hex("#0F172A")
        });
        doc.Blocks.Add(pHeader);

        var pMeta = new Paragraph
        {
            FontSize = 9,
            Foreground = Hex("#475569"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        pMeta.Inlines.Add(new Run($"Date/Period: {dateRange}    |    Store: {storeFilter}    |    Printed: {DateTime.Now:yyyy-MM-dd h:mm tt}    |    Records: {rows.Count}"));
        doc.Blocks.Add(pMeta);

        // Divider
        var divLine = new Paragraph
        {
            BorderBrush = Hex("#CBD5E1"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 0, 0, 10)
        };
        doc.Blocks.Add(divLine);

        // ─── 2. TABLE ─────────────────────────────────────────────────────────
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 12) };

        // Define columns based on report type
        List<(string Header, TextAlignment Align, double Proportion)> colDefs = reportType switch
        {
            "sales" =>
            [
                ("PO #", TextAlignment.Left, 110),
                ("Date", TextAlignment.Center, 125),
                ("Customer", TextAlignment.Left, 200),
                ("Shopping Day", TextAlignment.Center, 100),
                ("Items Subtotal", TextAlignment.Right, 110),
                ("Pasabuy Fee", TextAlignment.Right, 105),
                ("Delivery Fee", TextAlignment.Right, 100),
                ("Grand Total", TextAlignment.Right, 115),
                ("Status", TextAlignment.Center, 95)
            ],
            "customers" =>
            [
                ("Customer Name", TextAlignment.Left, 220),
                ("Phone", TextAlignment.Left, 130),
                ("Address", TextAlignment.Left, 250),
                ("Total Orders", TextAlignment.Center, 105),
                ("Total Spent", TextAlignment.Right, 130),
                ("Pasabuy Fees Paid", TextAlignment.Right, 140)
            ],
            "cancelled" =>
            [
                ("PO #", TextAlignment.Left, 110),
                ("Order Date", TextAlignment.Center, 125),
                ("Customer", TextAlignment.Left, 180),
                ("Items", TextAlignment.Center, 80),
                ("Cancelled Value", TextAlignment.Right, 120),
                ("Delivery Type", TextAlignment.Center, 105),
                ("Cancellation Reason / Notes", TextAlignment.Left, 250)
            ],
            _ => // topstores
            [
                ("Type", TextAlignment.Left, 90),
                ("Store / Product Name", TextAlignment.Left, 220),
                ("Total Orders", TextAlignment.Center, 110),
                ("Total Units", TextAlignment.Center, 110),
                ("Estimated Volume", TextAlignment.Right, 130),
                ("Share / Note", TextAlignment.Left, 200)
            ]
        };

        double totalProp = colDefs.Sum(c => c.Proportion);
        foreach (var c in colDefs)
        {
            var colW = Math.Round((c.Proportion / totalProp) * cw);
            table.Columns.Add(new TableColumn { Width = new GridLength(colW) });
        }

        // Table Header
        var hg = new TableRowGroup();
        var hr = new TableRow { Background = Hex("#F1F5F9") };

        foreach (var col in colDefs)
        {
            hr.Cells.Add(new TableCell(new Paragraph(new Run(col.Header))
            {
                FontSize      = 9,
                FontWeight    = FontWeights.Bold,
                Foreground    = Hex("#1E293B"),
                TextAlignment = col.Align,
                Margin        = new Thickness(0)
            })
            {
                Padding         = new Thickness(5, 6, 5, 6),
                BorderBrush     = Hex("#CBD5E1"),
                BorderThickness = new Thickness(0, 1, 0, 1)
            });
        }
        hg.Rows.Add(hr);
        table.RowGroups.Add(hg);

        // Table Data Rows
        var dg = new TableRowGroup();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var row = new TableRow();

            List<string> cellValues = reportType switch
            {
                "sales" =>
                [
                    $"{r.OrderNo}",
                    $"{r.CreatedAt:yyyy-MM-dd HH:mm}",
                    $"{r.CustomerName}",
                    $"{r.BuyingDay}",
                    $"₱{Convert.ToDecimal(r.ItemsSubtotal):N2}",
                    $"₱{Convert.ToDecimal(r.PasabuyFee):N2}",
                    $"₱{Convert.ToDecimal(r.DeliveryFee):N2}",
                    $"₱{Convert.ToDecimal(r.GrandTotal):N2}",
                    $"{r.Status}"
                ],
                "customers" =>
                [
                    $"{r.Name}",
                    $"{r.Phone}",
                    $"{r.Address}",
                    $"{r.OrdersCount}",
                    $"₱{Convert.ToDecimal(r.TotalSpent):N2}",
                    $"₱{Convert.ToDecimal(r.PasabuyFeesPaid):N2}"
                ],
                "cancelled" =>
                [
                    $"{r.OrderNo}",
                    $"{r.CreatedAt:yyyy-MM-dd HH:mm}",
                    $"{r.CustomerName}",
                    $"{r.ItemsCountDisplay}",
                    $"₱{Convert.ToDecimal(r.GrandTotal):N2}",
                    $"{r.DeliveryTypeDisplay}",
                    $"{r.Notes}"
                ],
                _ => // topstores
                [
                    $"{r.CategoryType}",
                    $"{r.Name}",
                    $"{r.TotalOrders}",
                    $"{r.TotalQtyDisplay}",
                    $"₱{Convert.ToDecimal(r.EstimatedVolume):N2}",
                    $"{r.ShareNote}"
                ]
            };

            for (int j = 0; j < colDefs.Count; j++)
            {
                var val = j < cellValues.Count ? cellValues[j] : "";
                var align = colDefs[j].Align;
                var isMonospace = colDefs[j].Header.Contains("PO #") || colDefs[j].Header.Contains("Phone");

                row.Cells.Add(new TableCell(new Paragraph(new Run(val))
                {
                    FontFamily    = isMonospace ? monoFont : sansFont,
                    FontSize      = 9,
                    Foreground    = Hex("#0F172A"),
                    TextAlignment = align,
                    Margin        = new Thickness(0)
                })
                {
                    Padding         = new Thickness(5, 5, 5, 5),
                    BorderBrush     = Hex("#E2E8F0"),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                });
            }

            dg.Rows.Add(row);
        }

        if (rows.Count == 0)
        {
            var emptyRow = new TableRow();
            emptyRow.Cells.Add(new TableCell(new Paragraph(new Run("(No records found for the selected period)"))
            {
                FontSize = 9.5, Foreground = Hex("#64748B"), TextAlignment = TextAlignment.Center
            })
            { ColumnSpan = colDefs.Count, Padding = new Thickness(0, 10, 0, 10) });
            dg.Rows.Add(emptyRow);
        }

        // Summary Row if applicable
        if (summaryTotals != null && summaryTotals.Count > 0)
        {
            var sumRow = new TableRow { Background = Hex("#F8FAFC") };
            var sumText = string.Join("   |   ", summaryTotals.Select(kv => $"{kv.Key}: {kv.Value}"));
            var pSum = new Paragraph(new Run(sumText))
            {
                FontSize      = 9.5,
                FontWeight    = FontWeights.Bold,
                Foreground    = Hex("#0F172A"),
                TextAlignment = TextAlignment.Right,
                Margin        = new Thickness(0)
            };
            sumRow.Cells.Add(new TableCell(pSum)
            {
                ColumnSpan      = colDefs.Count,
                Padding         = new Thickness(6, 7, 6, 7),
                BorderBrush     = Hex("#CBD5E1"),
                BorderThickness = new Thickness(0, 2, 0, 1)
            });
            dg.Rows.Add(sumRow);
        }

        table.RowGroups.Add(dg);
        doc.Blocks.Add(table);

        // ─── 3. FOOTER ────────────────────────────────────────────────────────
        var pFoot = new Paragraph(new Run($"DIONIX POS • Antique Pasabuy & Retail • Management Report • Generated {DateTime.Now:yyyy-MM-dd HH:mm}"))
        {
            FontSize = 8.5,
            Foreground = Hex("#94A3B8"),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        doc.Blocks.Add(pFoot);

        return doc;
    }

    /// <summary>
    /// Formats and prints a clean, store-grouped buying checklist for the shopping run.
    /// Supports thermal roll printers (58/80mm) and standard A4 desktop printers (e.g. Epson L120).
    /// </summary>
    public static (bool success, string message) PrintBuyingList(
        string storeName,
        string buyingDay,
        List<OrderItem> items,
        string? targetPrinter = null,
        bool showDialog = true)
    {
        try
        {
            var pd = new PrintDialog();
            var queue = ResolvePrintQueue(targetPrinter);
            if (queue != null) pd.PrintQueue = queue;

            if (showDialog || pd.PrintQueue == null)
            {
                var result = pd.ShowDialog();
                if (result != true)
                {
                    return (false, "Print cancelled by user.");
                }
            }

            var printerName = pd.PrintQueue?.FullName ?? pd.PrintQueue?.Name ?? targetPrinter ?? "Printer";
            bool isThermal = IsThermalPrinter(printerName);

            FlowDocument doc;
            if (isThermal)
            {
                var printableWidth = pd.PrintableAreaWidth > 50 ? pd.PrintableAreaWidth : 302;
                var width = Math.Min(printableWidth, 380);
                doc = CreateThermalBuyingChecklistDocument(items, storeName, buyingDay, DateTime.Today, pageWidth: width);
                doc.PageHeight = 1500;
            }
            else
            {
                var printableWidth = pd.PrintableAreaWidth > 100 ? pd.PrintableAreaWidth : 793.7;
                var printableHeight = pd.PrintableAreaHeight > 100 ? pd.PrintableAreaHeight : 1122.5;
                doc = CreateRichBuyingListDocument(items, storeName, buyingDay, DateTime.Today, pageWidth: printableWidth);
                doc.PageHeight = printableHeight;
            }

            doc.ColumnWidth = double.PositiveInfinity;
            IDocumentPaginatorSource dps = doc;
            pd.PrintDocument(dps.DocumentPaginator, $"Buying List - {storeName}");

            return (true, $"Printed successfully to {printerName}.");
        }
        catch (Exception ex)
        {
            // Fallback to text mode
            try
            {
                var sb = new StringBuilder();
                const int W = 48;
                string Line(char c = '-') => new string(c, W);
                string Center(string s) => s.Length >= W ? s : s.PadLeft((W + s.Length) / 2).PadRight(W);
                string TwoCol(string left, string right)
                {
                    var spaces = W - left.Length - right.Length;
                    return spaces > 0 ? left + new string(' ', spaces) + right : $"{left} {right}";
                }

                sb.AppendLine(Center("DIONIX POS"));
                sb.AppendLine(Center("ANTIQUE PASABUY — SHOPPING LIST"));
                sb.AppendLine(Line('-'));
                sb.AppendLine(TwoCol("Shopping Day:", buyingDay));
                sb.AppendLine(TwoCol("Date:", DateTime.Now.ToString("MMM dd, yyyy")));
                sb.AppendLine(Line('-'));

                double totalQty = 0;
                var storeGroups = items
                    .GroupBy(i => string.IsNullOrWhiteSpace(i.Store) ? "General Store" : i.Store)
                    .OrderBy(g => g.Key);

                foreach (var group in storeGroups)
                {
                    sb.AppendLine();
                    sb.AppendLine($"STORE: {group.Key.ToUpper()}");
                    sb.AppendLine(Line('-'));
                    double grpQty = 0;

                    foreach (var item in group)
                    {
                        grpQty += item.Qty;
                        totalQty += item.Qty;
                        var orderNo = item.Order?.OrderNo ?? "—";
                        var cust = item.Order?.Customer?.Name ?? "Walk-in";
                        sb.AppendLine($"[ ] {item.ProductName}");
                        var subLine = $"    Qty: {item.Qty:0.##} {item.Unit}  |  {cust}  |  {orderNo}";
                        sb.AppendLine(subLine);
                        if (!string.IsNullOrWhiteSpace(item.PurchaseNotes))
                        {
                            sb.AppendLine($"    Note: {item.PurchaseNotes}");
                        }
                    }
                    sb.AppendLine(Line('-'));
                    sb.AppendLine(TwoCol($"TOTAL QUANTITY ({group.Key}):", $"{grpQty:0.##}"));
                }

                sb.AppendLine(Line('='));
                sb.AppendLine(TwoCol("ALL STORES TOTAL QUANTITY:", $"{totalQty:0.##}"));
                sb.AppendLine(TwoCol("TOTAL ITEMS:", $"{items.Count}"));
                sb.AppendLine(Line('='));
                sb.AppendLine(Center("Shopper Copy • Antique Pasabuy"));
                sb.AppendLine();
                sb.AppendLine();

                return PrintContent(sb.ToString(), $"Buying List - {storeName}", targetPrinter, showDialog);
            }
            catch
            {
                return (false, $"Printing error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Legacy compatibility wrapper for PrintText.
    /// </summary>
    public static bool PrintText(string textFilePath, string printerName)
    {
        try
        {
            var text = File.Exists(textFilePath) ? File.ReadAllText(textFilePath) : textFilePath;
            var (ok, _) = PrintContent(text, "Document Print", printerName, showDialog: string.IsNullOrEmpty(printerName));
            return ok;
        }
        catch { return false; }
    }
}
