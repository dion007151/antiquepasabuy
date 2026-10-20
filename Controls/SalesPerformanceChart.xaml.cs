using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AntiquePasabuyPOS.Models;

namespace AntiquePasabuyPOS.Controls;

public partial class SalesPerformanceChart : UserControl
{
    private sealed class ChartBucket
    {
        public string Label { get; set; } = "";
        public string FullHeader { get; set; } = "";
        public decimal SalesRevenue { get; set; }
        public decimal PasabuyFees { get; set; }
        public decimal DeliveryFees { get; set; }
        public int OrderCount { get; set; }
    }

    private DateTime _from;
    private DateTime _to;
    private List<Order> _orders = [];
    private List<ChartBucket> _buckets = [];

    // Series visibility toggles
    private bool _showSales = true;
    private bool _showPasabuy = true;
    private bool _showDelivery = true;
    private string _groupingPeriodText = "Daily";
    private int _groupingPeriodCount = 7;

    // Brushes matching modern fintech POS palette
    private static readonly Brush SalesBrush = new SolidColorBrush(Color.FromRgb(69, 43, 134));    // #452B86 Dionix Purple
    private static readonly Brush PasabuyBrush = new SolidColorBrush(Color.FromRgb(5, 150, 105));   // #059669 Emerald
    private static readonly Brush DeliveryBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));  // #D97706 Amber
    private static readonly Brush GridLineBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249)); // #F1F5F9
    private static readonly Brush BaseLineBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)); // #CBD5E1
    private static readonly Brush TextMutedBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // #64748B
    private static readonly Brush HoverHighlightBrush = new SolidColorBrush(Color.FromArgb(20, 69, 43, 134)); // Subtle column highlight

    public SalesPerformanceChart()
    {
        InitializeComponent();
    }

    public void RenderChart(DateTime from, DateTime to, List<Order> orders)
    {
        _from = from;
        _to = to;
        _orders = orders ?? [];

        BuildBuckets();
        UpdateHeaderAndControls();
        Redraw();
    }

    private void OnPlotCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PlotCanvas.ActualWidth > 10 && PlotCanvas.ActualHeight > 10)
        {
            Redraw();
        }
    }

    private void BuildBuckets()
    {
        _buckets.Clear();

        var endInclusive = _to > _from ? _to.AddSeconds(-1).Date : _from.Date;
        var start = _from.Date;
        var totalDays = (int)(endInclusive - start).TotalDays + 1;

        if (totalDays <= 0) totalDays = 1;

        // SMART GROUPING:
        // Short range (<= 14 days) -> Group by Day
        // Longer range (15 - 90 days) -> Group by Week
        // Very large range (> 90 days) -> Group by Month
        if (totalDays <= 14)
        {
            _groupingPeriodText = "Daily";
            _groupingPeriodCount = totalDays;
            for (var d = start; d <= endInclusive; d = d.AddDays(1))
            {
                var dayOrders = _orders.Where(o => o.CreatedAt.Date == d).ToList();
                var label = totalDays <= 7 ? d.ToString("ddd MM/dd") : d.ToString("MMM dd");

                _buckets.Add(new ChartBucket
                {
                    Label = label,
                    FullHeader = d.ToString("dddd, MMMM dd, yyyy"),
                    SalesRevenue = dayOrders.Sum(o => o.GrandTotal),
                    PasabuyFees = dayOrders.Sum(o => o.PasabuyFee),
                    DeliveryFees = dayOrders.Sum(o => o.DeliveryFee),
                    OrderCount = dayOrders.Count
                });
            }
        }
        else if (totalDays <= 90)
        {
            var weekCount = (int)Math.Ceiling(totalDays / 7.0);
            _groupingPeriodText = "Weekly";
            _groupingPeriodCount = weekCount;

            var curStart = start;
            while (curStart <= endInclusive)
            {
                var curEnd = curStart.AddDays(6);
                if (curEnd > endInclusive) curEnd = endInclusive;

                var weekOrders = _orders.Where(o => o.CreatedAt.Date >= curStart && o.CreatedAt.Date <= curEnd).ToList();
                var label = $"{curStart:MM/dd}-{curEnd:MM/dd}";

                _buckets.Add(new ChartBucket
                {
                    Label = label,
                    FullHeader = $"Week of {curStart:MMM dd} – {curEnd:MMM dd, yyyy}",
                    SalesRevenue = weekOrders.Sum(o => o.GrandTotal),
                    PasabuyFees = weekOrders.Sum(o => o.PasabuyFee),
                    DeliveryFees = weekOrders.Sum(o => o.DeliveryFee),
                    OrderCount = weekOrders.Count
                });

                curStart = curStart.AddDays(7);
            }
        }
        else
        {
            var startMonth = new DateTime(start.Year, start.Month, 1);
            var endMonth = new DateTime(endInclusive.Year, endInclusive.Month, 1);
            var curMonth = startMonth;
            var monthCount = 0;

            while (curMonth <= endMonth)
            {
                monthCount++;
                var nextMonth = curMonth.AddMonths(1);
                var monthOrders = _orders.Where(o => o.CreatedAt >= curMonth && o.CreatedAt < nextMonth).ToList();

                _buckets.Add(new ChartBucket
                {
                    Label = curMonth.ToString("MMM yyyy"),
                    FullHeader = curMonth.ToString("MMMM yyyy"),
                    SalesRevenue = monthOrders.Sum(o => o.GrandTotal),
                    PasabuyFees = monthOrders.Sum(o => o.PasabuyFee),
                    DeliveryFees = monthOrders.Sum(o => o.DeliveryFee),
                    OrderCount = monthOrders.Count
                });

                curMonth = nextMonth;
            }

            _groupingPeriodText = "Monthly";
            _groupingPeriodCount = monthCount;
        }
    }

    #region Interactive Mode Switcher & Series Toggles

    private void OnModeAllClicked(object sender, MouseButtonEventArgs e)
    {
        _showSales = true;
        _showPasabuy = true;
        _showDelivery = true;
        UpdateHeaderAndControls();
        Redraw();
    }

    private void OnModeFeesClicked(object sender, MouseButtonEventArgs e)
    {
        _showSales = false;
        _showPasabuy = true;
        _showDelivery = true;
        UpdateHeaderAndControls();
        Redraw();
    }

    private void OnModeRevenueClicked(object sender, MouseButtonEventArgs e)
    {
        _showSales = true;
        _showPasabuy = false;
        _showDelivery = false;
        UpdateHeaderAndControls();
        Redraw();
    }

    private void OnToggleSalesSeries(object sender, MouseButtonEventArgs e)
    {
        // Don't allow turning off all series
        if (_showSales && !_showPasabuy && !_showDelivery) return;
        _showSales = !_showSales;
        UpdateHeaderAndControls();
        Redraw();
    }

    private void OnTogglePasabuySeries(object sender, MouseButtonEventArgs e)
    {
        if (_showPasabuy && !_showSales && !_showDelivery) return;
        _showPasabuy = !_showPasabuy;
        UpdateHeaderAndControls();
        Redraw();
    }

    private void OnToggleDeliverySeries(object sender, MouseButtonEventArgs e)
    {
        if (_showDelivery && !_showSales && !_showPasabuy) return;
        _showDelivery = !_showDelivery;
        UpdateHeaderAndControls();
        Redraw();
    }

    private void UpdateHeaderAndControls()
    {
        if (SubtitleLabel == null || ModeAllPill == null) return;

        bool isAll = _showSales && _showPasabuy && _showDelivery;
        bool isFeesOnly = !_showSales && (_showPasabuy || _showDelivery);
        bool isRevenueOnly = _showSales && !_showPasabuy && !_showDelivery;

        var unit = _groupingPeriodText == "Daily" ? $"({_groupingPeriodCount} day{(_groupingPeriodCount > 1 ? "s" : "")})" :
                   _groupingPeriodText == "Weekly" ? $"({_groupingPeriodCount} week{(_groupingPeriodCount > 1 ? "s" : "")})" :
                   $"({_groupingPeriodCount} month{(_groupingPeriodCount > 1 ? "s" : "")})";

        if (isFeesOnly)
        {
            SubtitleLabel.Text = $"{_groupingPeriodText} net fee earnings {unit} — fee scale";
        }
        else if (isRevenueOnly)
        {
            SubtitleLabel.Text = $"{_groupingPeriodText} gross sales volume {unit}";
        }
        else
        {
            SubtitleLabel.Text = $"{_groupingPeriodText} revenue & fee breakdown {unit}";
        }

        var primaryBrush = TryFindResource("PosPrimary") as Brush ?? SalesBrush;
        var secondaryTextBrush = TryFindResource("PosSecondaryText") as Brush ?? TextMutedBrush;

        // Update segmented pill highlights
        ModeAllPill.Background = isAll ? primaryBrush : Brushes.Transparent;
        ModeAllText.Foreground = isAll ? Brushes.White : secondaryTextBrush;
        ModeAllText.FontWeight = isAll ? FontWeights.SemiBold : FontWeights.Medium;

        ModeFeesPill.Background = isFeesOnly ? primaryBrush : Brushes.Transparent;
        ModeFeesText.Foreground = isFeesOnly ? Brushes.White : secondaryTextBrush;
        ModeFeesText.FontWeight = isFeesOnly ? FontWeights.SemiBold : FontWeights.Medium;

        ModeRevenuePill.Background = isRevenueOnly ? primaryBrush : Brushes.Transparent;
        ModeRevenueText.Foreground = isRevenueOnly ? Brushes.White : secondaryTextBrush;
        ModeRevenueText.FontWeight = isRevenueOnly ? FontWeights.SemiBold : FontWeights.Medium;

        // Update interactive legend opacity & decorations
        LegendSalesDot.Opacity = _showSales ? 1.0 : 0.25;
        LegendSalesLabel.Opacity = _showSales ? 1.0 : 0.4;
        LegendSalesLabel.TextDecorations = _showSales ? null : TextDecorations.Strikethrough;

        LegendPasabuyDot.Opacity = _showPasabuy ? 1.0 : 0.25;
        LegendPasabuyLabel.Opacity = _showPasabuy ? 1.0 : 0.4;
        LegendPasabuyLabel.TextDecorations = _showPasabuy ? null : TextDecorations.Strikethrough;

        LegendDeliveryDot.Opacity = _showDelivery ? 1.0 : 0.25;
        LegendDeliveryLabel.Opacity = _showDelivery ? 1.0 : 0.4;
        LegendDeliveryLabel.TextDecorations = _showDelivery ? null : TextDecorations.Strikethrough;
    }

    #endregion

    private void Redraw()
    {
        YAxisCanvas.Children.Clear();
        PlotCanvas.Children.Clear();
        XAxisCanvas.Children.Clear();

        var activeCount = (_showSales ? 1 : 0) + (_showPasabuy ? 1 : 0) + (_showDelivery ? 1 : 0);
        if (activeCount == 0) activeCount = 1;

        if (_buckets.Count == 0 || _buckets.All(b =>
            (!_showSales || b.SalesRevenue == 0) &&
            (!_showPasabuy || b.PasabuyFees == 0) &&
            (!_showDelivery || b.DeliveryFees == 0)))
        {
            EmptyStateOverlay.Visibility = Visibility.Visible;
            return;
        }

        EmptyStateOverlay.Visibility = Visibility.Collapsed;

        var plotWidth = PlotCanvas.ActualWidth;
        var plotHeight = PlotCanvas.ActualHeight;

        if (plotWidth < 50 || plotHeight < 50) return;

        // Reserve top margin for nice spacing
        const double topMargin = 12.0;
        const double bottomMargin = 4.0;
        var usableHeight = plotHeight - topMargin - bottomMargin;

        // Calculate maximum value across visible metrics ONLY
        decimal maxVal = 0m;
        foreach (var b in _buckets)
        {
            if (_showSales && b.SalesRevenue > maxVal) maxVal = b.SalesRevenue;
            if (_showPasabuy && b.PasabuyFees > maxVal) maxVal = b.PasabuyFees;
            if (_showDelivery && b.DeliveryFees > maxVal) maxVal = b.DeliveryFees;
        }
        var niceMax = CalculateNiceMax(maxVal);

        // 1. Draw 4 Horizontal Gridlines & Y-Axis Labels
        const int steps = 4;
        for (var i = 0; i <= steps; i++)
        {
            var frac = (double)i / steps;
            var yPos = (plotHeight - bottomMargin) - (frac * usableHeight);
            var tickVal = (decimal)frac * niceMax;

            // Subtle Gridline across plot
            var line = new Line
            {
                X1 = 0,
                Y1 = yPos,
                X2 = plotWidth,
                Y2 = yPos,
                Stroke = i == 0 ? BaseLineBrush : GridLineBrush,
                StrokeThickness = i == 0 ? 1.5 : 1.0,
                SnapsToDevicePixels = true
            };
            PlotCanvas.Children.Add(line);

            // Y-Axis Label
            var labelText = FormatCurrencyAxis(tickVal);
            var yLabel = new TextBlock
            {
                Text = labelText,
                FontSize = 10.5,
                FontFamily = new FontFamily("Segoe UI"),
                Foreground = TextMutedBrush,
                TextAlignment = TextAlignment.Right,
                Width = 58
            };
            Canvas.SetLeft(yLabel, 2);
            Canvas.SetTop(yLabel, yPos - 8);
            YAxisCanvas.Children.Add(yLabel);
        }

        // 2. Draw Grouped Bars for each Bucket
        var bucketCount = _buckets.Count;
        var clusterSlotWidth = plotWidth / bucketCount;

        // Compute bar width adaptively based on active series count
        var maxClusterWidth = clusterSlotWidth - 10;
        const double barGap = 3.0;
        var barWidth = Math.Clamp((maxClusterWidth - ((activeCount - 1) * barGap)) / activeCount, 5.0, 24.0);
        var totalClusterWidth = (barWidth * activeCount) + (barGap * (activeCount - 1));

        for (var i = 0; i < bucketCount; i++)
        {
            var b = _buckets[i];
            var clusterCenter = (i + 0.5) * clusterSlotWidth;
            var clusterLeft = clusterCenter - (totalClusterWidth / 2.0);

            // Invisible full-height hover column hit area
            var hoverColumn = new Border
            {
                Width = clusterSlotWidth,
                Height = plotHeight,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = CreateBucketToolTip(b)
            };
            Canvas.SetLeft(hoverColumn, i * clusterSlotWidth);
            Canvas.SetTop(hoverColumn, 0);

            // Subtle hover overlay inside the column
            var highlightOverlay = new Border
            {
                Width = clusterSlotWidth - 4,
                Height = plotHeight,
                Background = HoverHighlightBrush,
                CornerRadius = new CornerRadius(6),
                Opacity = 0.0,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(highlightOverlay, (i * clusterSlotWidth) + 2);
            Canvas.SetTop(highlightOverlay, 0);
            PlotCanvas.Children.Add(highlightOverlay);

            var currentBarLeft = clusterLeft;
            var barList = new List<Border>();

            // Bar 1: Sales Revenue (Blue)
            if (_showSales)
            {
                var salesHeight = (double)(b.SalesRevenue / niceMax) * usableHeight;
                if (b.SalesRevenue > 0 && salesHeight < 3) salesHeight = 3;
                var salesBar = CreateBar(barWidth, salesHeight, SalesBrush);
                Canvas.SetLeft(salesBar, currentBarLeft);
                Canvas.SetTop(salesBar, (plotHeight - bottomMargin) - salesHeight);
                PlotCanvas.Children.Add(salesBar);
                barList.Add(salesBar);
                currentBarLeft += barWidth + barGap;
            }

            // Bar 2: Pasabuy Fees (Green)
            if (_showPasabuy)
            {
                var pasabuyHeight = (double)(b.PasabuyFees / niceMax) * usableHeight;
                if (b.PasabuyFees > 0 && pasabuyHeight < 3) pasabuyHeight = 3;
                var pasabuyBar = CreateBar(barWidth, pasabuyHeight, PasabuyBrush);
                Canvas.SetLeft(pasabuyBar, currentBarLeft);
                Canvas.SetTop(pasabuyBar, (plotHeight - bottomMargin) - pasabuyHeight);
                PlotCanvas.Children.Add(pasabuyBar);
                barList.Add(pasabuyBar);
                currentBarLeft += barWidth + barGap;
            }

            // Bar 3: Delivery Fees (Orange)
            if (_showDelivery)
            {
                var deliveryHeight = (double)(b.DeliveryFees / niceMax) * usableHeight;
                if (b.DeliveryFees > 0 && deliveryHeight < 3) deliveryHeight = 3;
                var deliveryBar = CreateBar(barWidth, deliveryHeight, DeliveryBrush);
                Canvas.SetLeft(deliveryBar, currentBarLeft);
                Canvas.SetTop(deliveryBar, (plotHeight - bottomMargin) - deliveryHeight);
                PlotCanvas.Children.Add(deliveryBar);
                barList.Add(deliveryBar);
            }

            // Column hover interaction
            hoverColumn.MouseEnter += (_, _) =>
            {
                highlightOverlay.Opacity = 1.0;
                foreach (var bar in barList) bar.Opacity = 0.88;
            };
            hoverColumn.MouseLeave += (_, _) =>
            {
                highlightOverlay.Opacity = 0.0;
                foreach (var bar in barList) bar.Opacity = 1.0;
            };

            PlotCanvas.Children.Add(hoverColumn);

            // 3. X-Axis Label
            var skipLabel = bucketCount > 10 && (i % 2 != 0 && i != bucketCount - 1);
            if (!skipLabel)
            {
                var xLabel = new TextBlock
                {
                    Text = b.Label,
                    FontSize = 10.5,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = TextMutedBrush,
                    TextAlignment = TextAlignment.Center,
                    Width = clusterSlotWidth + 10,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Canvas.SetLeft(xLabel, (i * clusterSlotWidth) - 5);
                Canvas.SetTop(xLabel, 3);
                XAxisCanvas.Children.Add(xLabel);
            }
        }
    }

    private static Border CreateBar(double width, double height, Brush fill)
    {
        return new Border
        {
            Width = width,
            Height = height,
            Background = fill,
            CornerRadius = new CornerRadius(3, 3, 0, 0),
            IsHitTestVisible = false
        };
    }

    private ToolTip CreateBucketToolTip(ChartBucket b)
    {
        var tt = new ToolTip
        {
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HasDropShadow = true
        };

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), // #0F172A Dark Slate
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)), // #334155
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            MinWidth = 210
        };

        var sp = new StackPanel();

        // Header (Period Title)
        var titleBlock = new TextBlock
        {
            Text = b.FullHeader,
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        sp.Children.Add(titleBlock);

        var orderCountBlock = new TextBlock
        {
            Text = $"{b.OrderCount} order{(b.OrderCount == 1 ? "" : "s")}",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), // #94A3B8
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        sp.Children.Add(orderCountBlock);

        // Divider
        sp.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Row helper
        UIElement MakeMetricRow(string name, decimal amount, Color dotColor, Color textColor, bool isDimmed = false)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2), Opacity = isDimmed ? 0.45 : 1.0 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(dotColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Grid.SetColumn(dot, 0);
            grid.Children.Add(dot);

            var label = new TextBlock
            {
                Text = name,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            var val = new TextBlock
            {
                Text = $"₱{amount:N2}",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textColor),
                FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            Grid.SetColumn(val, 2);
            grid.Children.Add(val);

            return grid;
        }

        sp.Children.Add(MakeMetricRow("Sales Revenue", b.SalesRevenue, Color.FromRgb(11, 94, 215), Colors.White, !_showSales));
        sp.Children.Add(MakeMetricRow("Pasabuy Fees", b.PasabuyFees, Color.FromRgb(16, 185, 129), Color.FromRgb(52, 211, 153), !_showPasabuy));
        sp.Children.Add(MakeMetricRow("Delivery Fees", b.DeliveryFees, Color.FromRgb(245, 158, 11), Color.FromRgb(251, 191, 36), !_showDelivery));

        // If in Net Fees mode, show total net earnings row
        if (!_showSales && (_showPasabuy || _showDelivery))
        {
            var netTotal = (_showPasabuy ? b.PasabuyFees : 0) + (_showDelivery ? b.DeliveryFees : 0);
            sp.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Margin = new Thickness(0, 5, 0, 5)
            });
            sp.Children.Add(MakeMetricRow("Net Total Earned", netTotal, Color.FromRgb(34, 197, 94), Color.FromRgb(74, 222, 128)));
        }

        card.Child = sp;
        tt.Content = card;
        return tt;
    }

    private static decimal CalculateNiceMax(decimal maxValue)
    {
        if (maxValue <= 0) return 500m;

        // Add 15% headroom so bars don't touch the top ceiling
        var val = (double)maxValue * 1.15;
        var exponent = Math.Floor(Math.Log10(val));
        var fraction = val / Math.Pow(10, exponent);
        double niceFraction;

        if (fraction <= 1.2) niceFraction = 1.2;
        else if (fraction <= 2.0) niceFraction = 2.0;
        else if (fraction <= 2.5) niceFraction = 2.5;
        else if (fraction <= 5.0) niceFraction = 5.0;
        else if (fraction <= 7.5) niceFraction = 7.5;
        else niceFraction = 10.0;

        return (decimal)(niceFraction * Math.Pow(10, exponent));
    }

    private static string FormatCurrencyAxis(decimal val)
    {
        if (val >= 1_000_000)
            return $"₱{val / 1_000_000:0.#}M";
        if (val >= 1_000)
            return $"₱{val / 1_000:0.#}k";
        return $"₱{val:N0}";
    }
}
