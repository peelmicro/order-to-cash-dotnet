'use client';

import Link from 'next/link';
import { useState, type FormEvent } from 'react';
import { DateTime } from '@/components/date-time';
import { ErrorMessage } from '@/components/error-message';
import { Pager } from '@/components/pager';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useCredits, useInvoices, useRegisterPayment, type CreditFilters, type InvoiceFilters, type RegisterPaymentResult } from '@/hooks/use-billing';
import { useRetailers } from '@/hooks/use-catalog';
import { useOrderIdByReference } from '@/hooks/use-orders';
import { INVOICE_STATUSES, type Invoice, type InvoiceStatus, type Party } from '@/lib/api-types';
import { formatMinorUnits, parseAmount, toDecimalString } from '@/lib/money';

const PAGE_SIZE = 20;

/**
 * A starting point for the remittance reference, shaped like openapi.yaml's
 * example (`PAY-2026-08-18-000019`). It is deterministic per invoice and day on
 * purpose: `paymentReference` IS the idempotency key (B10, R48), so submitting
 * the same suggestion twice demonstrates a replay rather than a second payment.
 */
export function suggestPaymentReference(invoice: Pick<Invoice, 'invoiceReference'>, today: Date = new Date()): string {
  return `PAY-${today.toISOString().slice(0, 10)}-${invoice.invoiceReference.replace(/^INV-/, '')}`;
}

export function BillingView() {
  const retailers = useRetailers();
  return (
    <div className="flex flex-col gap-8">
      <h1 className="text-xl font-semibold">Billing</h1>
      {retailers.isError ? <ErrorMessage error={retailers.error} prefix="Retailer filters unavailable" fallback="the retailer list could not be loaded" testId="billing-retailers-error" /> : null}
      <CreditsCard retailers={retailers.data ?? []} />
      <InvoicesSection retailers={retailers.data ?? []} />
    </div>
  );
}

function RetailerFilter({ id, value, onChange, retailers }: { id: string; value: string | undefined; onChange: (value: string | undefined) => void; retailers: Party[] }) {
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>Retailer</Label>
      <NativeSelect id={id} className="w-64" value={value ?? ''} onChange={(event) => onChange(event.target.value || undefined)}>
        <NativeSelectOption value="">All retailers</NativeSelectOption>
        {retailers.map((retailer) => (
          <NativeSelectOption key={retailer.code} value={retailer.code}>
            {retailer.name} ({retailer.code})
          </NativeSelectOption>
        ))}
      </NativeSelect>
    </div>
  );
}

function CreditsCard({ retailers }: { retailers: Party[] }) {
  const [filters, setFilters] = useState<CreditFilters>({ page: 1, pageSize: PAGE_SIZE });
  const credits = useCredits(filters);
  return (
    <Card>
      <CardHeader>
        <CardTitle>Credit limits</CardTitle>
        <CardDescription>Available = limit − held − open exposure (invariant B1). A line at zero is what makes an order&apos;s credit check fail.</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <RetailerFilter id="credit-retailer-filter" value={filters.retailerCode} retailers={retailers} onChange={(retailerCode) => setFilters((current) => ({ ...current, retailerCode, page: 1 }))} />
        {credits.isError ? (
          <ErrorMessage error={credits.error} prefix="Could not load credit limits" fallback="the request failed" testId="credits-error" />
        ) : credits.isPending ? (
          <p className="text-sm text-muted-foreground" data-testid="credits-loading">
            Loading credit limits…
          </p>
        ) : credits.data.items.length === 0 ? (
          <p className="rounded-md border border-dashed p-6 text-sm text-muted-foreground" data-testid="credits-empty">
            No credit lines match this filter.
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead scope="col">Retailer</TableHead>
                <TableHead scope="col">Company</TableHead>
                <TableHead scope="col" className="text-right">
                  Limit
                </TableHead>
                <TableHead scope="col" className="text-right">
                  Held
                </TableHead>
                <TableHead scope="col" className="text-right">
                  Open exposure
                </TableHead>
                <TableHead scope="col" className="text-right">
                  Available
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {credits.data.items.map((credit) => (
                <TableRow key={credit.creditCode} data-testid="credit-row">
                  <TableCell>{credit.retailerCode}</TableCell>
                  <TableCell>{credit.companyCode}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatMinorUnits(credit.creditLimit, credit.currency)}</TableCell>
                  <TableCell className="text-right tabular-nums" data-testid="credit-held">
                    {formatMinorUnits(credit.activeHolds, credit.currency)}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{formatMinorUnits(credit.openExposure, credit.currency)}</TableCell>
                  <TableCell className={`text-right tabular-nums ${credit.availableCredit === 0 ? 'font-medium text-destructive' : ''}`} data-testid="credit-available">
                    {formatMinorUnits(credit.availableCredit, credit.currency)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        {credits.isError ? null : <Pager page={credits.data?.page} current={filters.page} noun="credit lines" onChange={(page) => setFilters((current) => ({ ...current, page }))} testId="credits-pager" />}
      </CardContent>
    </Card>
  );
}

function InvoicesSection({ retailers }: { retailers: Party[] }) {
  const [filters, setFilters] = useState<InvoiceFilters>({ page: 1, pageSize: PAGE_SIZE });
  const invoices = useInvoices(filters);
  // The invoice being paid is held here, not looked up in the list: once paid
  // it may leave an "issued" list on the next poll, and the outcome (accepted
  // or duplicate) must stay on screen when it does — found in a real browser.
  const [active, setActive] = useState<Invoice | null>(null);

  return (
    <section className="flex flex-col gap-4" aria-labelledby="invoices-heading">
      <h2 id="invoices-heading" className="text-lg font-semibold">
        Invoices
      </h2>
      <div className="flex flex-wrap items-end gap-4">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="invoice-status-filter">Status</Label>
          <NativeSelect id="invoice-status-filter" className="w-48" value={filters.status ?? ''} onChange={(event) => setFilters((current) => ({ ...current, status: (event.target.value || undefined) as InvoiceStatus | undefined, page: 1 }))}>
            <NativeSelectOption value="">All statuses</NativeSelectOption>
            {INVOICE_STATUSES.map((status) => (
              <NativeSelectOption key={status} value={status}>
                {status}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
        <RetailerFilter id="invoice-retailer-filter" value={filters.retailerCode} retailers={retailers} onChange={(retailerCode) => setFilters((current) => ({ ...current, retailerCode, page: 1 }))} />
        {invoices.isFetching && !invoices.isPending ? <span className="text-xs text-muted-foreground">refreshing…</span> : null}
      </div>

      {active ? <PaymentForm key={active.invoiceId} invoice={active} onClose={() => setActive(null)} /> : null}

      {invoices.isError ? (
        <ErrorMessage error={invoices.error} prefix="Could not load invoices" fallback="the request failed" testId="invoices-error" />
      ) : invoices.isPending ? (
        <p className="text-sm text-muted-foreground" data-testid="invoices-loading">
          Loading invoices…
        </p>
      ) : invoices.data.items.length === 0 ? (
        <p className="rounded-md border border-dashed p-6 text-sm text-muted-foreground" data-testid="invoices-empty">
          No invoices match these filters.
        </p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead scope="col">Invoice</TableHead>
              <TableHead scope="col">Order</TableHead>
              <TableHead scope="col">Retailer</TableHead>
              <TableHead scope="col">Company</TableHead>
              <TableHead scope="col">Issued</TableHead>
              <TableHead scope="col" className="text-right">
                Total
              </TableHead>
              <TableHead scope="col">Status</TableHead>
              <TableHead scope="col">
                <span className="sr-only">Actions</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {invoices.data.items.map((invoice) => (
                <TableRow key={invoice.invoiceId} data-testid="invoice-row">
                  <TableCell className="font-medium">{invoice.invoiceReference}</TableCell>
                  <TableCell>{invoice.orderReference}</TableCell>
                  <TableCell>{invoice.retailerCode}</TableCell>
                  <TableCell>{invoice.companyCode}</TableCell>
                  <TableCell>
                    <DateTime value={invoice.invoiceDate} />
                  </TableCell>
                  <TableCell className="text-right tabular-nums" data-testid="invoice-total">
                    {formatMinorUnits(invoice.totalAmount, invoice.currency)}
                  </TableCell>
                  <TableCell>
                    <Badge variant={invoice.status === 'paid' ? 'secondary' : 'outline'} data-testid="invoice-status">
                      {invoice.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right">
                    {invoice.status === 'issued' && active?.invoiceId !== invoice.invoiceId ? (
                      <Button type="button" size="sm" variant="outline" onClick={() => setActive(invoice)} data-testid="register-payment-button">
                        Register payment
                      </Button>
                    ) : null}
                  </TableCell>
                </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {invoices.isError ? null : <Pager page={invoices.data?.page} current={filters.page} noun="invoices" onChange={(page) => setFilters((current) => ({ ...current, page }))} testId="invoices-pager" />}
    </section>
  );
}

/**
 * Register payment (R47/R48). The amount is typed as a decimal and reaches the
 * wire as exact integer minor units, parsed digit-wise for the invoice's own
 * currency. The answer is rendered by what the server says happened: a NEW
 * payment (`201`, `accepted`) and an idempotent replay (`200`, `duplicate`)
 * look different, so an operator can tell a second click changed nothing.
 */
function PaymentForm({ invoice, onClose }: { invoice: Invoice; onClose: () => void }) {
  const registerPayment = useRegisterPayment();
  const [paymentReference, setPaymentReference] = useState(() => suggestPaymentReference(invoice));
  const [amountInput, setAmountInput] = useState(() => toDecimalString(invoice.totalAmount, invoice.currency));
  const [problem, setProblem] = useState<string | null>(null);
  const [result, setResult] = useState<RegisterPaymentResult | null>(null);
  const linkedOrderId = useOrderIdByReference(result?.response.orderReference ?? undefined);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const amount = parseAmount(amountInput, invoice.currency);
    if (!paymentReference.trim()) {
      setProblem('Enter the remittance reference.');
      return;
    }
    if (amount === undefined) {
      setProblem(`Enter an amount in ${invoice.currency} (for example ${toDecimalString(invoice.totalAmount, invoice.currency)}).`);
      return;
    }
    setProblem(null);
    registerPayment.mutate(
      { invoiceId: invoice.invoiceId, request: { paymentReference: paymentReference.trim(), amount: { amount, currency: invoice.currency }, valueDate: new Date().toISOString(), source: 'operator' } },
      { onSuccess: setResult },
    );
  }

  return (
    <div className="flex flex-col gap-3 rounded-md border p-4" data-testid="payment-form">
      <p className="text-sm font-medium">
        Register payment for {invoice.invoiceReference} ({invoice.orderReference}) — {formatMinorUnits(invoice.totalAmount, invoice.currency)}
      </p>
      <form method="post" onSubmit={submit} noValidate className="flex flex-col gap-3">
        <div className="grid grid-cols-1 items-end gap-3 sm:grid-cols-[minmax(0,1fr)_12rem_auto]">
          <div className="flex min-w-0 flex-col gap-1.5">
            <Label htmlFor={`payment-reference-${invoice.invoiceId}`}>Payment reference</Label>
            <Input id={`payment-reference-${invoice.invoiceId}`} value={paymentReference} onChange={(event) => setPaymentReference(event.target.value)} maxLength={30} data-testid="payment-reference-input" />
          </div>
          <div className="flex min-w-0 flex-col gap-1.5">
            <Label htmlFor={`payment-amount-${invoice.invoiceId}`}>Amount ({invoice.currency})</Label>
            <Input id={`payment-amount-${invoice.invoiceId}`} inputMode="decimal" value={amountInput} onChange={(event) => setAmountInput(event.target.value)} data-testid="payment-amount-input" />
          </div>
          <div className="flex gap-2">
            <Button type="submit" disabled={registerPayment.isPending} data-testid="submit-payment-button">
              {registerPayment.isPending ? 'Registering…' : 'Submit payment'}
            </Button>
            <Button type="button" variant="ghost" onClick={onClose}>
              {result ? 'Close' : 'Cancel'}
            </Button>
          </div>
        </div>
      </form>
      {problem ? <p className="text-sm text-destructive">{problem}</p> : null}
      {registerPayment.isError ? <ErrorMessage error={registerPayment.error} fallback="Registering the payment failed." testId="payment-error" /> : null}
      {result && !registerPayment.isError ? (
        result.response.outcome === 'accepted' ? (
          <p className="text-sm text-emerald-700" data-testid="payment-outcome-accepted" data-http-status={result.status}>
            Payment {result.response.paymentReference} recorded (HTTP {result.status}) — invoice {result.response.invoiceReference} is now {result.response.invoiceStatus}.
          </p>
        ) : (
          <p className="text-sm text-amber-700" data-testid="payment-outcome-duplicate" data-http-status={result.status}>
            Payment {result.response.paymentReference} was already recorded (HTTP {result.status}) — nothing new was created; this was an idempotent replay.
          </p>
        )
      ) : null}
      {result ? (
        <div className="text-sm text-muted-foreground">
          The order completes once the saga catches up —{' '}
          {linkedOrderId.isError ? (
            <ErrorMessage error={linkedOrderId.error} prefix="the order link could not be resolved" fallback="the request failed" testId="view-order-link-error" />
          ) : linkedOrderId.isPending ? (
            <span data-testid="view-order-link-resolving">resolving the order link…</span>
          ) : linkedOrderId.data ? (
            <Link href={`/orders/${linkedOrderId.data}`} className="underline" data-testid="view-order-link">
              watch it on the order&apos;s live timeline
            </Link>
          ) : (
            // The lookup ANSWERED and found nothing: say so, never keep "resolving" on screen for a question already answered.
            <span data-testid="view-order-link-not-found">no order was found for {result.response.orderReference ?? 'this invoice'}, so there is no timeline to link to.</span>
          )}
        </div>
      ) : null}
    </div>
  );
}
