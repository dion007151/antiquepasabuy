using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AntiquePasabuyPOS.Models;
using AntiquePasabuyPOS.Services;

namespace AntiquePasabuyPOS.Views.Dialogs;

public partial class PrintPreviewDialog : Window
{
    private enum DocSourceKind { Order, Checklist, OrdersSummary, CustomDoc }

    private readonly DocSourceKind _kind;
    private readonly Order? _order;
    private readonly List<OrderItem>? _checklistItems;
    private readonly string _storeName = "All Stores";
    private readonly string _buyingDay = "All Days";
    private readonly DateTime? _printDate;
    private readonly List<Order>? _summaryOrders;
    private readonly FlowDocument? _rawDoc;

    private readonly string _businessName;
    private readonly string _footerMsg;
    private readonly string _docTitle;

    public PrintPreviewDialog(Order order, string? businessName = null, string? footerMsg = null, string? targetPrinter = null)
    {
        InitializeComponent();
        _kind = DocSourceKind.Order;
        _order = order;
        var profile = BusinessProfileService.GetProfileById(order.BusinessId) ?? BusinessProfileService.ActiveProfile;
        _businessName = string.IsNullOrWhiteSpace(businessName) || businessName == "DIONIX POS" ? profile.SystemName : businessName;
        _footerMsg = string.IsNullOrWhiteSpace(footerMsg) || footerMsg.Contains("Thank you") ? profile.ReceiptFooter : footerMsg;
        _docTitle = $"PO-{order.OrderNo}";

        TitleLabel.Text = $"Purchase Order Preview — {order.OrderNo}";
        SubtitleLabel.Text = $"Customer: {order.Customer?.Name ?? "Walk-in"}  •  Shopping Day: {order.BuyingDay}  •  Status: {PrinterService.FormatOrderStatus(order.Status)}";

        InitSettings(targetPrinter);
    }

    public PrintPreviewDialog(List<OrderItem> checklistItems, string storeName, string buyingDay, string title, string? targetPrinter = null, DateTime? printDate = null)
    {
        InitializeComponent();
        _kind = DocSourceKind.Checklist;
        _checklistItems = checklistItems;
        _storeName = storeName;
        _buyingDay = buyingDay;
        _printDate = printDate ?? DateTime.Today;
        _businessName = "DIONIX POS";
        _footerMsg = "Antique Pasabuy Checklist";
        _docTitle = $"Checklist-{buyingDay}";

        TitleLabel.Text = title;
        var dateFormatted = _printDate.Value.ToString("MMM dd, yyyy");
        SubtitleLabel.Text = $"Date: {dateFormatted} ({buyingDay})  •  Store: {storeName}  •  Total Items: {checklistItems.Count}";

        InitSettings(targetPrinter);
    }

    public PrintPreviewDialog(List<Order> orders, string title, string? targetPrinter = null)
    {
        InitializeComponent();
        _kind = DocSourceKind.OrdersSummary;
        _summaryOrders = orders;
        _businessName = "DIONIX POS";
        _footerMsg = "Antique Pasabuy Summary";
        _docTitle = title;

        TitleLabel.Text = title;
        SubtitleLabel.Text = $"Total Purchase Orders: {orders.Count}  •  Estimated Total: ₱{orders.Sum(o => o.GrandTotal):N2}";

        InitSettings(targetPrinter);
    }

    public PrintPreviewDialog(FlowDocument customDoc, string title, string subtitle, string docTitle = "Print Document", string? targetPrinter = null)
    {
        InitializeComponent();
        _kind = DocSourceKind.CustomDoc;
        _rawDoc = customDoc;
        _businessName = "DIONIX POS";
        _footerMsg = "";
        _docTitle = docTitle;

        TitleLabel.Text = title;
        SubtitleLabel.Text = subtitle;

        InitSettings(targetPrinter);
    }

    private void SelectPaperTag(string tag)
    {
        if (PaperSizeCombo == null) return;
        foreach (ComboBoxItem item in PaperSizeCombo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                PaperSizeCombo.SelectedItem = item;
                break;
            }
        }
    }

    private void InitSettings(string? targetPrinter)
    {
        // 1. Populate Installed Printers
        try
        {
            var (printers, defaultPrinter) = PrinterService.GetInstalledPrinters();
            PrinterCombo.ItemsSource = printers;

            string? toSelect = null;
            if (!string.IsNullOrWhiteSpace(targetPrinter) && printers.Contains(targetPrinter) && !PrinterService.IsVirtualOrPdfPrinter(targetPrinter))
            {
                toSelect = targetPrinter;
            }
            else
            {
                var physical = printers.FirstOrDefault(p => !PrinterService.IsVirtualOrPdfPrinter(p));
                if (physical != null)
                {
                    toSelect = physical;
                }
                else if (!string.IsNullOrWhiteSpace(targetPrinter) && printers.Contains(targetPrinter))
                {
                    toSelect = targetPrinter;
                }
                else if (!string.IsNullOrWhiteSpace(defaultPrinter) && printers.Contains(defaultPrinter))
                {
                    toSelect = defaultPrinter;
                }
                else
                {
                    toSelect = printers.FirstOrDefault();
                }
            }
            PrinterCombo.SelectedItem = toSelect;

            // Intelligent paper tag default:
            // POS thermal printers -> Thermal 80mm
            // Standard desktop inkjets/lasers (Epson L120, HP, Canon, PDF) -> A4
            if (!string.IsNullOrWhiteSpace(toSelect))
            {
                bool isThermal = PrinterService.IsThermalPrinter(toSelect);
                SelectPaperTag(isThermal ? "Thermal80" : "A4");
            }
        }
        catch
        {
            // fallback
        }

        CheckSpoolerQueueStatus();
        Loaded += (_, _) => UpdatePreview();
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (sender == PrinterCombo)
        {
            var selected = PrinterCombo.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                bool isThermal = PrinterService.IsThermalPrinter(selected);
                var currentTag = (PaperSizeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                if (isThermal && currentTag is "A4" or "Letter" or "Legal" or "A5")
                {
                    SelectPaperTag("Thermal80");
                }
                else if (!isThermal && currentTag is "Thermal58" or "Thermal80")
                {
                    SelectPaperTag("A4");
                }
            }
            CheckSpoolerQueueStatus();
        }
        UpdatePreview();
    }

    private void CheckSpoolerQueueStatus()
    {
        try
        {
            var selected = PrinterCombo.SelectedItem as string;
            var (jobs, status) = PrinterService.GetPrinterQueueStatus(selected);
            if (QueueWarningPanel != null)
            {
                if (jobs > 0 || status.Contains("Error", StringComparison.OrdinalIgnoreCase) || status.Contains("Paper", StringComparison.OrdinalIgnoreCase))
                {
                    QueueWarningPanel.Visibility = Visibility.Visible;
                    if (QueueWarningText != null) QueueWarningText.Text = $"{jobs} pending job(s) in queue";
                }
                else
                {
                    QueueWarningPanel.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch { }
    }

    private void OnClearSpoolerQueue(object sender, RoutedEventArgs e)
    {
        var selected = PrinterCombo.SelectedItem as string;
        var (ok, msg) = PrinterService.ClearPrinterQueue(selected);
        MessageBox.Show(msg, ok ? "Print Queue Cleared" : "Queue Status", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        CheckSpoolerQueueStatus();
    }

    private (double width, double height, Thickness padding, bool isThermal, string paperTag, string marginTag) GetActiveLayoutSettings()
    {
        var paperTag = (PaperSizeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "A4";
        var orientTag = (OrientationCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Auto";
        var marginTag = (MarginsCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Normal";

        bool isThermal = paperTag is "Thermal58" or "Thermal80";
        if (CustomSizePanel != null)
        {
            CustomSizePanel.Visibility = paperTag == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        }

        // Standard dimensions (in 1/96 inch points)
        double width = 793.7;  // A4 default
        double height = 1122.5;

        switch (paperTag)
        {
            case "Letter":
                width = 816.0; height = 1056.0;
                break;
            case "Legal":
                width = 816.0; height = 1344.0;
                break;
            case "A5":
                width = 559.4; height = 793.7;
                break;
            case "Thermal58":
                width = 219.0; height = 1200.0;
                break;
            case "Thermal80":
                width = 302.0; height = 1500.0;
                break;
            case "Custom":
                if (double.TryParse(CustomWidthBox?.Text, out var cw) && cw > 100) width = cw;
                if (double.TryParse(CustomHeightBox?.Text, out var ch) && ch > 100) height = ch;
                if (width <= 400) isThermal = true;
                break;
            default: // A4
                width = 793.7; height = 1122.5;
                break;
        }

        // Orientation
        if (!isThermal)
        {
            if (orientTag == "Landscape" && width < height)
            {
                (width, height) = (height, width);
            }
            else if (orientTag == "Portrait" && width > height)
            {
                (width, height) = (height, width);
            }
        }

        // Margins
        Thickness padding;
        if (isThermal)
        {
            padding = marginTag switch
            {
                "None" => new Thickness(0),
                "Narrow" => new Thickness(6, 6, 6, 6),
                "Compact" => new Thickness(4, 4, 4, 4),
                _ => new Thickness(10, 8, 10, 8)
            };
        }
        else
        {
            padding = marginTag switch
            {
                "None" => new Thickness(0),
                "Narrow" => new Thickness(18, 15, 18, 15),
                "Compact" => new Thickness(10, 10, 10, 10),
                _ => new Thickness(36, 30, 36, 30) // Normal
            };
        }

        return (width, height, padding, isThermal, paperTag, marginTag);
    }

    private FlowDocument GenerateDocumentForSettings(double width, double height, Thickness padding, bool isThermal)
    {
        FlowDocument doc;
        switch (_kind)
        {
            case DocSourceKind.Order when _order != null:
                doc = isThermal
                    ? PrinterService.CreateThermalPurchaseOrderFlowDocument(_order, _businessName, _footerMsg, pageWidth: width, padding: padding)
                    : PrinterService.CreatePurchaseOrderFlowDocument(_order, _businessName, _footerMsg, pageWidth: width);
                break;

            case DocSourceKind.Checklist when _checklistItems != null:
                doc = isThermal
                    ? PrinterService.CreateThermalBuyingChecklistDocument(_checklistItems, _storeName, _buyingDay, printDate: _printDate, pageWidth: width, padding: padding)
                    : PrinterService.CreateRichBuyingListDocument(_checklistItems, _storeName, _buyingDay, printDate: _printDate, pageWidth: width);
                break;

            case DocSourceKind.OrdersSummary when _summaryOrders != null:
                doc = PrinterService.CreateOrdersSummaryFlowDocument(_summaryOrders, _docTitle, pageWidth: width, padding: padding, isThermal: isThermal);
                break;

            case DocSourceKind.CustomDoc when _rawDoc != null:
                doc = _rawDoc;
                break;

            default:
                doc = new FlowDocument(new Paragraph(new Run("No content available for printing.")));
                break;
        }

        doc.PageWidth = width;
        doc.PageHeight = height;
        doc.PagePadding = padding;
        doc.ColumnWidth = double.PositiveInfinity;
        return doc;
    }

    private void UpdatePreview()
    {
        var (width, height, padding, isThermal, paperTag, marginTag) = GetActiveLayoutSettings();

        var doc = GenerateDocumentForSettings(width, height, padding, isThermal);
        DocViewer.Document = doc;

        if (PaperContainer != null)
        {
            PaperContainer.Width = width;
        }

        if (PaperInfoLabel != null)
        {
            var layoutName = isThermal ? $"Thermal {width:0}pt Roll" : $"{paperTag} ({(width > height ? "Landscape" : "Portrait")})";
            PaperInfoLabel.Text = $"{layoutName}  •  Margin: {marginTag}  •  Width: {width:0}pt";
        }
    }

    private void OnSavePdf(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Title = "Save Document as PDF",
            Filter = "PDF Document (*.pdf)|*.pdf|XPS Document (*.xps)|*.xps",
            FileName = $"{_docTitle}_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
        };

        if (sfd.ShowDialog() != true) return;

        try
        {
            var pd = new PrintDialog();
            var pdfPrinter = "Microsoft Print to PDF";
            var server = new LocalPrintServer();
            PrintQueue? queue = null;
            try { queue = server.GetPrintQueue(pdfPrinter); } catch { }

            if (queue != null)
            {
                pd.PrintQueue = queue;
                var (width, height, padding, isThermal, _, _) = GetActiveLayoutSettings();
                var printDoc = GenerateDocumentForSettings(width, height, padding, isThermal);
                IDocumentPaginatorSource dps = printDoc;
                pd.PrintDocument(dps.DocumentPaginator, _docTitle);
                MessageBox.Show($"Document successfully saved as PDF:\n{sfd.FileName}", "PDF Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Fallback to standard print dialog so user can select their PDF virtual printer
                var res = pd.ShowDialog();
                if (res == true)
                {
                    var (width, height, padding, isThermal, _, _) = GetActiveLayoutSettings();
                    var printDoc = GenerateDocumentForSettings(width, height, padding, isThermal);
                    IDocumentPaginatorSource dps = printDoc;
                    pd.PrintDocument(dps.DocumentPaginator, _docTitle);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save PDF: {ex.Message}", "Export Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        var selectedPrinter = PrinterCombo.SelectedItem as string;
        int copies = 1;
        if (int.TryParse(CopiesBox.Text, out var c) && c > 1) copies = c;

        try
        {
            var pd = new PrintDialog();
            var resolvedQueue = PrinterService.ResolvePrintQueue(selectedPrinter);

            if (resolvedQueue != null)
            {
                // Printer found — send directly without showing dialog
                pd.PrintQueue = resolvedQueue;
                try
                {
                    if (pd.PrintTicket != null && copies > 1)
                        pd.PrintTicket.CopyCount = copies;
                }
                catch { }

                var (width, height, padding, isThermal, _, _) = GetActiveLayoutSettings();
                var printDoc = GenerateDocumentForSettings(width, height, padding, isThermal);
                IDocumentPaginatorSource dps = printDoc;
                pd.PrintDocument(dps.DocumentPaginator, _docTitle);

                var printerName = resolvedQueue.FullName ?? resolvedQueue.Name ?? selectedPrinter ?? "Printer";
                MessageBox.Show(
                    $"Successfully sent {copies} cop{(copies > 1 ? "ies" : "y")} to:\n{printerName}",
                    "Print Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            else
            {
                // Last resort: show the Windows print dialog
                // Pre-select the chosen printer name as a hint
                if (pd.ShowDialog() != true) return;

                try
                {
                    if (pd.PrintTicket != null && copies > 1)
                        pd.PrintTicket.CopyCount = copies;
                }
                catch { }

                var (width, height, padding, isThermal, _, _) = GetActiveLayoutSettings();
                var printDoc = GenerateDocumentForSettings(width, height, padding, isThermal);
                IDocumentPaginatorSource dps = printDoc;
                pd.PrintDocument(dps.DocumentPaginator, _docTitle);

                var printerName = pd.PrintQueue?.FullName ?? pd.PrintQueue?.Name ?? selectedPrinter ?? "Printer";
                MessageBox.Show(
                    $"Successfully sent {copies} cop{(copies > 1 ? "ies" : "y")} to:\n{printerName}",
                    "Print Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Printing error: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private void OnCopyAsImage(object sender, RoutedEventArgs e)
    {
        try
        {
            // Determine render dimensions from DocViewer or PaperContainer
            double w = DocViewer.ActualWidth > 0 ? DocViewer.ActualWidth : (PaperContainer?.Width ?? 794);
            double h = DocViewer.ActualHeight > 0 ? DocViewer.ActualHeight : 1123;

            int pixelW = (int)Math.Ceiling(w);
            int pixelH = (int)Math.Ceiling(h);
            if (pixelW <= 0) pixelW = 794;
            if (pixelH <= 0) pixelH = 1123;

            // Render with an explicit solid white background so pasted image is never transparent or dark in Messenger/browsers
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pixelW, pixelH));
                var vb = new VisualBrush(DocViewer) { Stretch = Stretch.None };
                dc.DrawRectangle(vb, null, new Rect(0, 0, pixelW, pixelH));
            }

            var rtb = new RenderTargetBitmap(pixelW, pixelH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();

            // Encode as PNG into memory stream for modern web apps (Messenger, Viber, Telegram, browsers)
            var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            encoder.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);

            var dataObj = new DataObject();
            // "PNG" format is recognized by Chrome, Edge, Facebook Messenger, Viber, Discord
            dataObj.SetData("PNG", ms, false);
            // BitmapSource is recognized by Windows Paint, Office, and desktop apps
            dataObj.SetImage(rtb);

            bool copied = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(dataObj, true);
                    copied = true;
                    break;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    System.Threading.Thread.Sleep(60);
                }
            }

            if (!copied)
            {
                Clipboard.SetImage(rtb);
            }

            // Visual feedback on button
            CopyImageBtn.IsEnabled = false;
            var origContent = CopyImageBtn.Content;
            var ok = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ok.Children.Add(new MaterialDesignThemes.Wpf.PackIcon
            {
                Kind = MaterialDesignThemes.Wpf.PackIconKind.Check,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74))
            });
            ok.Children.Add(new TextBlock
            {
                Text = "Copied to Clipboard!",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74))
            });
            CopyImageBtn.Content = ok;

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                CopyImageBtn.Content = origContent;
                CopyImageBtn.IsEnabled = true;
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not copy receipt as image:\n{ex.Message}",
                "Copy Image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
