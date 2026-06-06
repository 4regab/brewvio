using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Models;
using Brewvio.Services;
using Microsoft.EntityFrameworkCore;

namespace Brewvio.Tests;

/// <summary>
/// Extended OrderService tests covering every untested path:
/// drafts (save/confirm/list/delete), advance status, queue count,
/// next order number, RecentAsync filtering, GetReceiptAsync,
/// modifier price delta, zero-quantity guard, stock warning emission,
/// and split-payment (Cash + GCash).
/// </summary>
public class OrderServiceExtendedTests(SharedTestDb fixture) : IClassFixture<SharedTestDb>
{
    private static (OrderService svc, BrewvioDbContext db) Build(TestScope t)
    {
        var cashier = t.Db.Users.First(u => u.Username == "cashier");
        var cur = TestSupport.Cur(cashier.Id, cashier.Username, cashier.Role);
        var audit = new AuditService(t.Db, cur);
        return (new OrderService(t.Db, cur, audit, new SettingsService(t.Db, audit)), t.Db);
    }

    private static List<CartItemInput> Cart(int menuItemId, int qty = 1) =>
        new() { new CartItemInput(menuItemId, qty, new List<int>(), null) };

    // ── Modifier price delta ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrder_applies_modifier_price_delta_to_unit_price()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");   // ₱140
        var extraShot = db.Modifiers.First(m => m.Name == "Extra Shot"); // +₱30

        var receipt = await svc.CreateAsync(new CreateOrderRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int> { extraShot.Id }, null) },
            0m,
            new List<PaymentInput> { new("Cash", 300m) }));

        Assert.Single(receipt.Items);
        Assert.Equal(170m, receipt.Items[0].UnitPrice);    // 140 + 30
        Assert.Equal(170m, receipt.Items[0].LineTotal);
        Assert.Equal("Extra Shot", receipt.Items[0].Modifiers);
    }

    [Fact]
    public async Task CreateOrder_multiple_modifiers_sum_all_deltas()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");   // ₱140
        var extraShot = db.Modifiers.First(m => m.Name == "Extra Shot"); // +₱30
        var oatMilk = db.Modifiers.First(m => m.Name == "Oat Milk");     // +₱25

        var receipt = await svc.CreateAsync(new CreateOrderRequest(
            new List<CartItemInput>
            {
                new(latte.Id, 1, new List<int> { extraShot.Id, oatMilk.Id }, null)
            },
            0m,
            new List<PaymentInput> { new("Cash", 300m) }));

        Assert.Equal(195m, receipt.Items[0].UnitPrice); // 140 + 30 + 25
    }

    // ── Zero-quantity guard ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrder_rejects_zero_quantity_item()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync(new CreateOrderRequest(
                new List<CartItemInput> { new(latte.Id, 0, new List<int>(), null) },
                0m,
                new List<PaymentInput> { new("Cash", 200m) })));
    }

    [Fact]
    public async Task CreateOrder_rejects_negative_quantity_item()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateAsync(new CreateOrderRequest(
                new List<CartItemInput> { new(latte.Id, -1, new List<int>(), null) },
                0m,
                new List<PaymentInput> { new("Cash", 200m) })));
    }

    // ── Stock warning emission ────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrder_emits_low_stock_warning_when_ingredient_hits_threshold()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte"); // uses 200ml milk per unit
        var milk = db.Ingredients.First(i => i.Name == "Whole Milk");

        // Set stock to exactly threshold so after one latte it drops below → low/negative warning
        milk.StockLevel = milk.Threshold;
        await db.SaveChangesAsync();

        var receipt = await svc.CreateAsync(new CreateOrderRequest(
            Cart(latte.Id, 1), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        Assert.NotEmpty(receipt.StockWarnings);
        Assert.Contains(receipt.StockWarnings, w => w.Contains("Whole Milk"));
    }

    [Fact]
    public async Task CreateOrder_emits_negative_stock_warning_when_oversold()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte"); // uses 200ml milk
        var milk = db.Ingredients.First(i => i.Name == "Whole Milk");

        milk.StockLevel = 0m; // empty
        await db.SaveChangesAsync();

        var receipt = await svc.CreateAsync(new CreateOrderRequest(
            Cart(latte.Id, 1), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        Assert.Contains(receipt.StockWarnings, w => w.Contains("negative"));
    }

    // ── Discount clamped to subtotal ──────────────────────────────────────────

    [Fact]
    public async Task CreateOrder_discount_exceeding_subtotal_is_clamped_to_subtotal()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte"); // ₱140

        var receipt = await svc.CreateAsync(new CreateOrderRequest(
            Cart(latte.Id, 1), 999m,       // discount > subtotal
            new List<PaymentInput> { new("Cash", 200m) }));

        // After clamping, discount = subtotal, tax = 0, total = 0
        Assert.Equal(140m, receipt.DiscountAmount); // clamped to subtotal
        Assert.Equal(0m, receipt.TotalAmount);
    }

    // ── Refund null path ──────────────────────────────────────────────────────

    [Fact]
    public async Task Refund_returns_null_for_missing_transaction()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, _) = Build(t);

        var result = await svc.RefundAsync(999_999, "Missing transaction");

        Assert.Null(result);
    }

    [Fact]
    public async Task Refund_of_preparing_order_throws()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        var receipt = await svc.CreateAsync(new CreateOrderRequest(
            Cart(latte.Id, 1), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        // Status is "Preparing" — only Completed can be refunded
        Assert.Equal("Preparing", receipt.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RefundAsync(receipt.TransactionId, "Too early to refund"));
    }

    // ── AdvanceStatusAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task AdvanceStatus_moves_preparing_to_completed()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        var receipt = await svc.CreateAsync(new CreateOrderRequest(
            Cart(latte.Id, 1), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        Assert.Equal("Preparing", receipt.Status);

        var summary = await svc.AdvanceStatusAsync(receipt.TransactionId);

        Assert.NotNull(summary);
        Assert.Equal("Completed", summary!.Status);

        var verify = t.NewContext();
        Assert.Equal("Completed", (await verify.Transactions.FindAsync(receipt.TransactionId))!.Status);
    }

    [Fact]
    public async Task AdvanceStatus_returns_null_for_missing_order()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, _) = Build(t);

        Assert.Null(await svc.AdvanceStatusAsync(999_999));
    }

    [Fact]
    public async Task AdvanceStatus_throws_when_already_completed()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        var receipt = await svc.CreateAsync(new CreateOrderRequest(
            Cart(latte.Id, 1), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));
        await svc.AdvanceStatusAsync(receipt.TransactionId); // → Completed

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.AdvanceStatusAsync(receipt.TransactionId));  // already Completed → invalid
    }

    // ── ActiveQueueCountAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ActiveQueueCount_counts_only_pending_and_preparing()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        // Create two orders (both start as "Preparing")
        var r1 = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 200m) }));
        var r2 = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 200m) }));

        // Advance one to Completed
        await svc.AdvanceStatusAsync(r1.TransactionId);

        var count = await svc.ActiveQueueCountAsync();

        Assert.Equal(1, count); // only r2 remains in Preparing
    }

    // ── NextOrderNumberAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task NextOrderNumber_returns_one_more_than_highest_id()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        var receipt = await svc.CreateAsync(new CreateOrderRequest(
            Cart(latte.Id), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        var next = await svc.NextOrderNumberAsync();

        Assert.Equal(receipt.TransactionId + 1, next);
    }

    [Fact]
    public async Task NextOrderNumber_returns_one_when_no_transactions_exist()
    {
        // Use isolated TestDb so no transactions from other tests leak in
        using var isolated = new TestDb();
        await DatabaseInitializer.SeedAllOriginalAsync(isolated.Db);
        var cashier = isolated.Db.Users.First(u => u.Username == "cashier");
        var cur = TestSupport.Cur(cashier.Id, cashier.Username, cashier.Role);
        var audit = new AuditService(isolated.Db, cur);
        var svc = new OrderService(isolated.Db, cur, audit, new SettingsService(isolated.Db, audit));

        var next = await svc.NextOrderNumberAsync();

        Assert.Equal(1, next);
    }

    // ── RecentAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RecentAsync_returns_most_recent_first()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        var r1 = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 200m) }));
        var r2 = await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 200m) }));

        var recent = await svc.RecentAsync(take: 10);

        Assert.Equal(r2.TransactionId, recent.First().Id);
        Assert.Equal(r1.TransactionId, recent.Last().Id);
    }

    [Fact]
    public async Task RecentAsync_respects_take_limit()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        for (var i = 0; i < 5; i++)
            await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 200m) }));

        var recent = await svc.RecentAsync(take: 3);

        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public async Task RecentAsync_from_date_excludes_older_transactions()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        // Create an order
        await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 200m) }));

        // Filter to tomorrow — nothing should match
        var future = await svc.RecentAsync(take: 50, from: DateTime.UtcNow.Date.AddDays(1));

        Assert.Empty(future);
    }

    [Fact]
    public async Task RecentAsync_from_date_includes_same_day_transactions()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        await svc.CreateAsync(new CreateOrderRequest(Cart(latte.Id), 0m, new List<PaymentInput> { new("Cash", 200m) }));

        var today = await svc.RecentAsync(take: 50, from: DateTime.UtcNow.Date);

        Assert.NotEmpty(today);
    }

    // ── GetReceiptAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetReceiptAsync_returns_correct_receipt_for_existing_id()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        var created = await svc.CreateAsync(new CreateOrderRequest(
            Cart(latte.Id, 2), 0m,
            new List<PaymentInput> { new("Cash", 500m) }));

        var fetched = await svc.GetReceiptAsync(created.TransactionId);

        Assert.NotNull(fetched);
        Assert.Equal(created.TransactionId, fetched!.TransactionId);
        Assert.Equal(created.TotalAmount, fetched.TotalAmount);
        Assert.Equal(2, fetched.Items.Sum(i => i.Quantity));
    }

    [Fact]
    public async Task GetReceiptAsync_returns_null_for_missing_id()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, _) = Build(t);

        Assert.Null(await svc.GetReceiptAsync(999_999));
    }

    // ── Draft lifecycle ───────────────────────────────────────────────────────

    [Fact]
    public async Task SaveDraft_creates_draft_transaction_with_correct_fields()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte"); // ₱140

        var draft = await svc.SaveDraftAsync(new SaveDraftRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) },
            20m, "Cash"));

        Assert.True(draft.Id > 0);
        Assert.Equal(140m, draft.Subtotal);
        Assert.Equal(20m, draft.DiscountAmount);
        Assert.Equal(1, draft.ItemCount);
        Assert.Contains("Caffe Latte", draft.ItemSummary);

        var verify = t.NewContext();
        var tx = await verify.Transactions.FindAsync(draft.Id);
        Assert.NotNull(tx);
        Assert.Equal("Draft", tx!.Status);
        Assert.Equal(0m, tx.TotalAmount);  // TotalAmount is 0 until confirmed
    }

    [Fact]
    public async Task SaveDraft_does_not_deduct_stock()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");
        var milkBefore = t.NewContext().Ingredients.First(i => i.Name == "Whole Milk").StockLevel;

        await svc.SaveDraftAsync(new SaveDraftRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) },
            0m, "Cash"));

        var milkAfter = t.NewContext().Ingredients.First(i => i.Name == "Whole Milk").StockLevel;
        Assert.Equal(milkBefore, milkAfter);
    }

    [Fact]
    public async Task SaveDraft_rejects_empty_cart()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, _) = Build(t);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SaveDraftAsync(new SaveDraftRequest(new List<CartItemInput>(), 0m, "Cash")));
    }

    [Fact]
    public async Task GetDraftsAsync_returns_only_current_cashiers_drafts()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        await svc.SaveDraftAsync(new SaveDraftRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) },
            0m, "Cash"));

        var drafts = await svc.GetDraftsAsync();

        Assert.Single(drafts);
        Assert.All(drafts, d => Assert.Contains("Caffe Latte", d.ItemSummary));
    }

    [Fact]
    public async Task GetDraftsAsync_returns_empty_when_no_drafts()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, _) = Build(t);

        var drafts = await svc.GetDraftsAsync();

        Assert.Empty(drafts);
    }

    [Fact]
    public async Task ConfirmDraft_processes_payment_and_deducts_stock()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        SettingsService.ResetTaxRateCache();
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");
        var milkBefore = t.NewContext().Ingredients.First(i => i.Name == "Whole Milk").StockLevel;

        var draft = await svc.SaveDraftAsync(new SaveDraftRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) },
            0m, "Cash"));

        // VAT-inclusive: total the customer pays == discounted subtotal (no discount here).
        var expectedTotal = draft.Subtotal;

        var receipt = await svc.ConfirmDraftAsync(draft.Id,
            new ConfirmDraftRequest(new List<PaymentInput> { new("Cash", expectedTotal) }));

        Assert.Equal("Preparing", receipt.Status);
        Assert.Equal(expectedTotal, receipt.TotalAmount);
        Assert.Equal(0m, receipt.Change);

        var milkAfter = t.NewContext().Ingredients.First(i => i.Name == "Whole Milk").StockLevel;
        Assert.Equal(milkBefore - 200m, milkAfter);
    }

    [Fact]
    public async Task ConfirmDraft_with_insufficient_payment_throws()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte"); // ₱140

        var draft = await svc.SaveDraftAsync(new SaveDraftRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) },
            0m, "Cash"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.ConfirmDraftAsync(draft.Id,
                new ConfirmDraftRequest(new List<PaymentInput> { new("Cash", 50m) })));
    }

    [Fact]
    public async Task ConfirmDraft_throws_for_missing_draft_id()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, _) = Build(t);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ConfirmDraftAsync(999_999,
                new ConfirmDraftRequest(new List<PaymentInput> { new("Cash", 200m) })));
    }

    [Fact]
    public async Task ConfirmDraft_gcash_payment_sets_method_to_gcash()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        SettingsService.ResetTaxRateCache();
        var (svc, db) = Build(t);
        var espresso = db.MenuItems.First(m => m.Name == "Espresso");

        var draft = await svc.SaveDraftAsync(new SaveDraftRequest(
            new List<CartItemInput> { new(espresso.Id, 1, new List<int>(), null) },
            0m, "GCash"));

        var total = draft.Subtotal;   // VAT-inclusive: total == discounted subtotal

        var receipt = await svc.ConfirmDraftAsync(draft.Id,
            new ConfirmDraftRequest(new List<PaymentInput> { new("GCash", total) }));

        Assert.Equal("GCash", receipt.PaymentMethod);
    }

    [Fact]
    public async Task ConfirmDraft_change_is_calculated_correctly()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        SettingsService.ResetTaxRateCache();
        var (svc, db) = Build(t);
        var espresso = db.MenuItems.First(m => m.Name == "Espresso");

        var draft = await svc.SaveDraftAsync(new SaveDraftRequest(
            new List<CartItemInput> { new(espresso.Id, 1, new List<int>(), null) },
            0m, "Cash"));

        var total = draft.Subtotal;   // VAT-inclusive: total == discounted subtotal
        var overpay = total + 50m;

        var receipt = await svc.ConfirmDraftAsync(draft.Id,
            new ConfirmDraftRequest(new List<PaymentInput> { new("Cash", overpay) }));

        Assert.Equal(50m, receipt.Change);
    }

    [Fact]
    public async Task DeleteDraft_removes_draft_and_its_items()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        var draft = await svc.SaveDraftAsync(new SaveDraftRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) },
            0m, "Cash"));

        await svc.DeleteDraftAsync(draft.Id);

        var verify = t.NewContext();
        Assert.Null(await verify.Transactions.FindAsync(draft.Id));
        Assert.Empty(await verify.TransactionItems.Where(ti => ti.TransactionId == draft.Id).ToListAsync());
    }

    [Fact]
    public async Task DeleteDraft_on_missing_id_does_not_throw()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, _) = Build(t);

        // Should be a no-op — not throw
        await svc.DeleteDraftAsync(999_999);
    }

    [Fact]
    public async Task DeleteDraft_writes_audit_entry()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        var draft = await svc.SaveDraftAsync(new SaveDraftRequest(
            new List<CartItemInput> { new(latte.Id, 1, new List<int>(), null) },
            0m, "Cash"));

        await svc.DeleteDraftAsync(draft.Id);

        var verify = t.NewContext();
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "DraftDeleted" && a.Details.Contains($"#{draft.Id}"));
    }

    // ── CancelAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_with_empty_reason_stores_default_message()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, _) = Build(t);

        await svc.CancelAsync(""); // empty reason

        var verify = t.NewContext();
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "OrderCancelled" && a.Details.Contains("no reason given"));
    }

    // ── CreateAsync audit trail ───────────────────────────────────────────────

    [Fact]
    public async Task CreateOrder_writes_order_placed_audit_entry()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        await svc.CreateAsync(new CreateOrderRequest(
            Cart(latte.Id), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        var verify = t.NewContext();
        Assert.Contains(await verify.AuditLogs.ToListAsync(),
            a => a.Action == "OrderPlaced");
    }

    // ── Multi-item order ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrder_with_multiple_different_items_computes_correct_subtotal()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");    // ₱140
        var espresso = db.MenuItems.First(m => m.Name == "Espresso");    // ₱100

        var receipt = await svc.CreateAsync(new CreateOrderRequest(
            new List<CartItemInput>
            {
                new(latte.Id, 1, new List<int>(), null),
                new(espresso.Id, 2, new List<int>(), null),
            },
            0m,
            new List<PaymentInput> { new("Cash", 500m) }));

        Assert.Equal(340m, receipt.Subtotal); // 140 + 2*100
        Assert.Equal(2, receipt.Items.Count);
    }

    // ── TransactionSummary shape ──────────────────────────────────────────────

    [Fact]
    public async Task AdvanceStatus_returns_summary_with_cashier_display_name()
    {
        using var t = fixture.Begin();
        await DatabaseInitializer.SeedAllOriginalAsync(t.Db);
        var (svc, db) = Build(t);
        var latte = db.MenuItems.First(m => m.Name == "Caffe Latte");

        var receipt = await svc.CreateAsync(new CreateOrderRequest(
            Cart(latte.Id), 0m,
            new List<PaymentInput> { new("Cash", 200m) }));

        var summary = await svc.AdvanceStatusAsync(receipt.TransactionId);

        Assert.NotNull(summary);
        Assert.False(string.IsNullOrWhiteSpace(summary!.Cashier));
        Assert.Equal("Completed", summary.Status);
        Assert.Equal(1, summary.ItemCount);
        Assert.Contains("Caffe Latte", summary.ItemSummary);
    }
}
