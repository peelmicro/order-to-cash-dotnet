using OrderToCash.Seed.Presentation;

// apps/Seed — the deterministic, idempotent one-shot seed job
// (feature_list.json #12), ported from #7's apps/seed/src/index.ts. Usage:
// `dotnet run --project src/Seed` against the composed stack (localhost:1433
// MS-SQL, localhost:27017 MongoDB — .env's MSSQL_*/MONGO_* variables).
try
{
    Console.WriteLine("[seed] starting...");
    var summary = await SeedRunner.RunAsync().ConfigureAwait(false);
    Console.WriteLine("[seed] OK — self-verification summary:");
    Console.WriteLine($"  orders:       currencies={summary.Orders.Currencies} products={summary.Orders.Products} " +
        $"retailers={summary.Orders.Retailers} companies={summary.Orders.Companies} orders={summary.Orders.Orders} " +
        $"orderItems={summary.Orders.OrderItems} outbox={summary.Orders.Outbox}");
    Console.WriteLine($"  fulfillment:  stock={summary.Fulfillment.Stock} reservations={summary.Fulfillment.Reservations} " +
        $"despatches={summary.Fulfillment.Despatches} despatchItems={summary.Fulfillment.DespatchItems} " +
        $"outbox={summary.Fulfillment.Outbox}");
    Console.WriteLine($"  billing:      credits={summary.Billing.Credits} creditItems={summary.Billing.CreditItems} " +
        $"invoices={summary.Billing.Invoices} invoiceItems={summary.Billing.InvoiceItems} " +
        $"payments={summary.Billing.Payments} outbox={summary.Billing.Outbox}");
    Console.WriteLine($"  mongo:        order_timeline={summary.OrderTimelines}");
    Console.WriteLine("[seed] done.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[seed] FAILED: {ex}");
    return 1;
}
