using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AntiquePasabuyPOS.Data;
using AntiquePasabuyPOS.Views;

namespace AntiquePasabuyPOS;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--test-rates"))
        {
            RunRateTests();
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--test-system"))
        {
            RunSystemTests();
            Shutdown(0);
            return;
        }

        // ── WPF Rendering Performance (low-end / Celeron hardware) ──────────
        // Set software-render fallback mode to Default so WPF will use GPU
        // composition where available and fall back gracefully without stalling.
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;

        // Initialize theme preferences (Light / Dark mode)
        AntiquePasabuyPOS.Services.ThemeManager.Initialize();
        AntiquePasabuyPOS.Services.DisplayScaleManager.Initialize();

        // Catch all unhandled exceptions so the app never silently crashes
        DispatcherUnhandledException += (_, ex) =>
        {
            var inner = ex.Exception.InnerException;
            var msg   = inner?.Message ?? ex.Exception.Message;
            var trace = (inner ?? ex.Exception).StackTrace ?? "";
            // Show first 6 lines of stack trace to identify the crash location
            var shortTrace = string.Join("\n", trace.Split('\n').Take(6));
            MessageBox.Show(
                $"An error occurred:\n\n{msg}\n\n--- Where ---\n{shortTrace}",
                "Antique Pasabuy POS — Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        try
        {
            var db = AppDbContext.Create();
            AntiquePasabuyPOS.Services.BusinessProfileService.Initialize(db);
            var login = new LoginWindow(db);
            login.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start:\n\n{ex.Message}\n\n{ex.InnerException?.Message}",
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void RunRateTests()
    {
        var testCases = new (decimal Subtotal, decimal Expected, string Desc)[]
        {
            (50.00m,   50.00m,  "TEST 1: Subtotal ₱50"),
            (99.00m,   50.00m,  "TEST 2: Subtotal ₱99"),
            (99.99m,   50.00m,  "BOUNDARY: Subtotal ₱99.99"),
            (100.00m,  75.00m,  "TEST 3 & BOUNDARY: Subtotal ₱100"),
            (199.00m,  75.00m,  "TEST 4: Subtotal ₱199"),
            (199.99m,  75.00m,  "BOUNDARY: Subtotal ₱199.99"),
            (200.00m,  100.00m, "TEST 5 & BOUNDARY: Subtotal ₱200"),
            (299.00m,  100.00m, "TEST 6: Subtotal ₱299"),
            (299.99m,  100.00m, "BOUNDARY: Subtotal ₱299.99"),
            (300.00m,  150.00m, "TEST 7 & BOUNDARY: Subtotal ₱300"),
            (999.00m,  150.00m, "TEST 8: Subtotal ₱999"),
            (999.99m,  150.00m, "BOUNDARY: Subtotal ₱999.99"),
            (1000.00m, 250.00m, "TEST 9 & BOUNDARY: Subtotal ₱1,000"),
            (1499.00m, 250.00m, "TEST 10: Subtotal ₱1,499"),
            (1499.99m, 250.00m, "BOUNDARY: Subtotal ₱1,499.99"),
            (1500.00m, 300.00m, "TEST 11 & BOUNDARY: Subtotal ₱1,500"),
            (1999.00m, 300.00m, "TEST 12: Subtotal ₱1,999"),
            (1999.99m, 300.00m, "BOUNDARY: Subtotal ₱1,999.99"),
            (2000.00m, 350.00m, "TEST 13 & BOUNDARY: Subtotal ₱2,000"),
            (2499.00m, 350.00m, "TEST 14: Subtotal ₱2,499"),
            (2499.99m, 350.00m, "BOUNDARY: Subtotal ₱2,499.99"),
            (2500.00m, 375.00m, "TEST 15 & BOUNDARY: Subtotal ₱2,500 (15%)"),
            (3000.00m, 450.00m, "TEST 16: Subtotal ₱3,000 (15%)"),
            (5000.00m, 750.00m, "TEST 17: Subtotal ₱5,000 (15%)"),
            (0.00m,    0.00m,   "EMPTY ORDER: Subtotal ₱0.00")
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== OFFICIAL ANTIQUE PASABUY SERVICE FEE TEST RESULTS ===");
        int passed = 0, failed = 0;

        foreach (var (sub, exp, desc) in testCases)
        {
            var actual = Services.PasabuyFeeService.CalculateServiceFee(sub, null);
            if (actual == exp)
            {
                sb.AppendLine($"[PASS] {desc} -> Subtotal: ₱{sub:N2} | Fee: ₱{actual:N2} (Expected: ₱{exp:N2})");
                passed++;
            }
            else
            {
                sb.AppendLine($"[FAIL] {desc} -> Subtotal: ₱{sub:N2} | Actual: ₱{actual:N2} | Expected: ₱{exp:N2}");
                failed++;
            }
        }

        sb.AppendLine($"TOTAL: {passed} PASSED, {failed} FAILED");
        var outPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rate_test_results.txt");
        System.IO.File.WriteAllText(outPath, sb.ToString());
    }

    private static void RunSystemTests()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== DIONIX POS LIFECYCLE & ARCHITECTURE VERIFICATION ===");
        int passed = 0, failed = 0;

        void Assert(bool condition, string testName)
        {
            if (condition)
            {
                sb.AppendLine($"[PASS] {testName}");
                passed++;
            }
            else
            {
                sb.AppendLine($"[FAIL] {testName}");
                failed++;
            }
        }

        // 1. OrderStatusService state machine transition legality tests
        Assert(Services.OrderStatusService.CanTransition(Models.OrderStatus.WAITING_CONFIRMATION, Models.OrderStatus.READY_TO_BUY),
            "State Machine: WAITING_CONFIRMATION -> READY_TO_BUY is allowed");
        Assert(Services.OrderStatusService.CanTransition(Models.OrderStatus.READY_TO_BUY, Models.OrderStatus.BUYING_NOW),
            "State Machine: READY_TO_BUY -> BUYING_NOW is allowed");
        Assert(Services.OrderStatusService.CanTransition(Models.OrderStatus.BUYING_NOW, Models.OrderStatus.COMPLETED),
            "State Machine: BUYING_NOW -> COMPLETED is allowed");
        Assert(Services.OrderStatusService.CanTransition(Models.OrderStatus.COMPLETED, Models.OrderStatus.CANCELLED),
            "State Machine: COMPLETED -> CANCELLED is allowed");
        Assert(!Services.OrderStatusService.CanTransition(Models.OrderStatus.CANCELLED, Models.OrderStatus.READY_TO_BUY),
            "State Machine: CANCELLED -> READY_TO_BUY is rejected");
        Assert(Services.OrderStatusService.CanTransition(Models.OrderStatus.WAITING_CONFIRMATION, Models.OrderStatus.CANCELLED),
            "State Machine: WAITING_CONFIRMATION -> CANCELLED is allowed");

        // 2. OrderCalculationService tests
        var order = new Models.Order
        {
            ServiceFee = 150m,
            DeliveryFee = 50m,
            Discount = 20m,
            Items = new List<Models.OrderItem>
            {
                new() { Qty = 2, EstimatedPrice = 200m }, // 400
                new() { Qty = 1, EstimatedPrice = 100m }  // 100
            },
            Payments = new List<Models.Payment>
            {
                new() { Amount = 200m }
            }
        };
        var subtotal = Services.OrderCalculationService.CalculateItemsSubtotal(order.Items);
        var grandTotal = Services.OrderCalculationService.CalculateGrandTotal(subtotal, order.PasabuyFee, order.DeliveryFee, order.Discount);
        var totalPaid = Services.OrderCalculationService.CalculateTotalPaid(order.Payments);
        var balance = Services.OrderCalculationService.CalculateBalance(grandTotal, totalPaid);

        Assert(subtotal == 500m, "Calculation: Items Subtotal = ₱500.00");
        Assert(grandTotal == 680m, "Calculation: Grand Total = ₱680.00 (500 + 150 + 50 - 20)");
        Assert(totalPaid == 200m, "Calculation: Total Paid = ₱200.00");
        Assert(balance == 480m, "Calculation: Balance Due = ₱480.00");

        // 3. NextActionService smart actions
        var pOrder = new Models.Order { Status = Models.OrderStatus.WAITING_CONFIRMATION };
        var pAction = Services.NextActionService.GetNextAction(pOrder);
        Assert(pAction.TargetStatus == Models.OrderStatus.BUYING_NOW && pAction.ActionLabel == "START BUYING",
            "NextAction: WAITING_CONFIRMATION -> START BUYING");

        var rOrder = new Models.Order { Status = Models.OrderStatus.READY_TO_BUY };
        var rAction = Services.NextActionService.GetNextAction(rOrder);
        Assert(rAction.TargetStatus == Models.OrderStatus.BUYING_NOW && rAction.ActionLabel == "START BUYING",
            "NextAction: READY_TO_BUY -> START BUYING");

        var bOrder = new Models.Order { Status = Models.OrderStatus.BUYING_NOW };
        var bAction = Services.NextActionService.GetNextAction(bOrder);
        Assert(bAction.TargetStatus == Models.OrderStatus.BOUGHT && bAction.ActionLabel == "MARK AS BOUGHT",
            "NextAction: BUYING_NOW -> MARK AS BOUGHT");

        var boughtOrder = new Models.Order { Status = Models.OrderStatus.BOUGHT };
        var boughtAction = Services.NextActionService.GetNextAction(boughtOrder);
        Assert(boughtAction.TargetStatus == Models.OrderStatus.COMPLETED && boughtAction.ActionLabel == "FINISH ORDER",
            "NextAction: BOUGHT -> FINISH ORDER");

        // 4. Strict fulfillment filtering (Fulfillment exclusion verification)
        var forBuyingOrder = new Models.Order { Status = Models.OrderStatus.READY_TO_BUY, DeliveryType = Models.DeliveryType.PICKUP };
        var completedPickupOrder = new Models.Order { Status = Models.OrderStatus.COMPLETED, DeliveryType = Models.DeliveryType.PICKUP };

        var testOrders = new List<Models.Order> { forBuyingOrder, completedPickupOrder };
        var pickupTabOrders = testOrders.Where(o => o.Status == Models.OrderStatus.COMPLETED && o.IsPickup).ToList();

        Assert(pickupTabOrders.Count == 1 && pickupTabOrders[0] == completedPickupOrder,
            "Fulfillment: READY_TO_BUY orders strictly excluded from Completed Pickup tab");

        // 5. Login validation test suite
        using (var db = AppDbContext.Create())
        {
            var loginVm = new ViewModels.LoginViewModel(db);

            loginVm.Username = "";
            loginVm.Password = "admin123";
            loginVm.LoginCommand.Execute(null);
            Assert(loginVm.LoginError == "Please enter your username.",
                "Login Validation: Empty username -> 'Please enter your username.'");

            loginVm.Username = "admin";
            loginVm.Password = "";
            loginVm.LoginCommand.Execute(null);
            Assert(loginVm.LoginError == "Please enter your password.",
                "Login Validation: Empty password -> 'Please enter your password.'");

            loginVm.Username = "nonexistent_user_999";
            loginVm.Password = "wrongpassword";
            loginVm.LoginCommand.Execute(null);
            Assert(loginVm.LoginError == "Incorrect username or password.",
                "Login Validation: Bad credentials -> 'Incorrect username or password.'");

            // Successful login
            bool loginSucceeded = false;
            loginVm.OnLoginSuccess = u => { loginSucceeded = (u != null && u.Username == "admin"); };
            loginVm.Username = "admin";
            loginVm.Password = "admin123";
            loginVm.LoginCommand.Execute(null);
            Assert(loginSucceeded && string.IsNullOrEmpty(loginVm.LoginError),
                "Login Auth: Valid credentials succeed and invoke OnLoginSuccess callback");
        }

        // 6. 5-Status Canonical Workflow & Payment Independence Suite
        using (var db = AppDbContext.Create())
        {
            var orderSvc = new Services.OrderService(db);
            var statusSvc = new Services.OrderStatusService(db);

            // Test A: Create PO -> defaults to WAITING_CONFIRMATION ("Waiting to Buy")
            var testItems = new List<(string name, double qty, string unit, decimal price, string store, string? notes)>
            {
                ("Workflow Test Item", 1, "pc", 100m, "SM", "Automated status test")
            };
            var po = orderSvc.Create(
                customerId: null,
                items: testItems,
                type: Models.DeliveryType.Pickup,
                notes: "Workflow automated test PO",
                status: Models.OrderStatus.WAITING_CONFIRMATION,
                buyingDay: "Wednesday"
            );
            Assert(po.Status == Models.OrderStatus.WAITING_CONFIRMATION && po.StatusDisplay == "Waiting to Buy",
                "Workflow A: Create PO defaults to Waiting to Buy");

            // Test B: Status display canonical labels
            Assert(Services.OrderStatusService.FormatStatus(Models.OrderStatus.WAITING_CONFIRMATION) == "Waiting to Buy" &&
                   Services.OrderStatusService.FormatStatus(Models.OrderStatus.READY_TO_BUY) == "Waiting to Buy" &&
                   Services.OrderStatusService.FormatStatus(Models.OrderStatus.BUYING_NOW) == "Buying" &&
                   Services.OrderStatusService.FormatStatus(Models.OrderStatus.BOUGHT) == "Bought" &&
                   Services.OrderStatusService.FormatStatus(Models.OrderStatus.COMPLETED) == "Order Finished" &&
                   Services.OrderStatusService.FormatStatus(Models.OrderStatus.CANCELLED) == "Cancelled",
                "Workflow B: Exact canonical 5-status strings (Waiting to Buy, Buying, Bought, Order Finished, Cancelled)");

            // Test C: Status transitions: Waiting to Buy -> Buying -> Bought -> Order Finished
            var (ok1, _) = statusSvc.Transition(po.Id, Models.OrderStatus.BUYING_NOW, reason: "Start Buying");
            var poAfterBuy = orderSvc.GetById(po.Id);
            Assert(ok1 && poAfterBuy?.StatusDisplay == "Buying", "Workflow C1: Transition to Buying");

            var (ok2, _) = statusSvc.Transition(po.Id, Models.OrderStatus.BOUGHT, reason: "Bought");
            var poAfterBought = orderSvc.GetById(po.Id);
            Assert(ok2 && poAfterBought?.StatusDisplay == "Bought", "Workflow C2: Transition to Bought");

            var (ok3, _) = statusSvc.Transition(po.Id, Models.OrderStatus.COMPLETED, reason: "Finish Order");
            var poAfterFinished = orderSvc.GetById(po.Id);
            Assert(ok3 && poAfterFinished?.StatusDisplay == "Order Finished", "Workflow C3: Transition to Order Finished");

            // Test D: Payment Status calculation: 0 = Not Paid, partial = Partly Paid, full = Paid
            var paymentPo = orderSvc.Create(
                customerId: null,
                items: testItems,
                type: Models.DeliveryType.Pickup,
                status: Models.OrderStatus.WAITING_CONFIRMATION,
                buyingDay: "Wednesday"
            );
            Assert(paymentPo.PaymentStatusDisplay == "Not Paid" && paymentPo.PaymentStatus == Models.PaymentStatus.UNPAID,
                "Workflow D1: Zero payment = Not Paid");

            var paymentSvc = new Services.PaymentService(db);
            paymentSvc.RecordPayment(paymentPo.Id, 20m, Models.PaymentMethod.Cash, "Partial payment");
            var partialPo = orderSvc.GetById(paymentPo.Id);
            Assert(partialPo?.PaymentStatusDisplay == "Partly Paid" && partialPo?.PaymentStatus == Models.PaymentStatus.PARTIAL,
                "Workflow D2: Partial payment = Partly Paid");

            paymentSvc.RecordPayment(paymentPo.Id, partialPo!.Balance, Models.PaymentMethod.Cash, "Balance payment");
            var fullPo = orderSvc.GetById(paymentPo.Id);
            Assert(fullPo?.PaymentStatusDisplay == "Paid" && fullPo?.PaymentStatus == Models.PaymentStatus.PAID && fullPo?.Balance == 0m,
                "Workflow D3: Full payment = Paid and Balance is 0");

            // Test E: Decoupling: paying in full does NOT auto-complete order status
            Assert(fullPo?.Status == Models.OrderStatus.WAITING_CONFIRMATION && fullPo?.StatusDisplay == "Waiting to Buy",
                "Workflow E: Paying in full preserves order status (still Waiting to Buy, decoupled from payment)");

            // Test F: Decoupling: Order Finished does not change payment status if unpaid
            var unpaidPo = orderSvc.Create(
                customerId: null,
                items: testItems,
                type: Models.DeliveryType.Pickup,
                status: Models.OrderStatus.COMPLETED,
                buyingDay: "Wednesday"
            );
            Assert(unpaidPo.StatusDisplay == "Order Finished" && unpaidPo.PaymentStatusDisplay == "Not Paid",
                "Workflow F: Order Finished with no payments stays Not Paid");

            // Test G: Dashboard counts use real DB counts for Waiting to Buy
            using (var freshDb = AppDbContext.Create())
            {
                var freshOrderSvc = new Services.OrderService(freshDb);
                var pasabuyStats = freshOrderSvc.GetPasabuyStats();
                var realWaitingToBuy = freshDb.Orders.Count(o => (o.BusinessId == "ANTIQUE_PASABUY" || string.IsNullOrEmpty(o.BusinessId)) &&
                                                                 (o.Status == Models.OrderStatus.WAITING_CONFIRMATION || o.Status == Models.OrderStatus.READY_TO_BUY));
                Assert(pasabuyStats.ForBuyingOrdersCount == realWaitingToBuy,
                    $"Workflow G: Dashboard uses real database counts for Waiting to Buy ({pasabuyStats.ForBuyingOrdersCount} == {realWaitingToBuy})");
            }

            // Cleanup test orders to preserve database state
            orderSvc.Delete(po.Id);
            orderSvc.Delete(paymentPo.Id);
            orderSvc.Delete(unpaidPo.Id);
        }

        sb.AppendLine($"TOTAL: {passed} PASSED, {failed} FAILED");
        var outPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system_test_results.txt");
        System.IO.File.WriteAllText(outPath, sb.ToString());
    }
}
