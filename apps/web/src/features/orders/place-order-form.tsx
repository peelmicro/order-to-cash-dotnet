'use client';

import Link from 'next/link';
import { useId, useMemo, useRef, useState, type FormEvent } from 'react';
import { ErrorMessage } from '@/components/error-message';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select';
import { Separator } from '@/components/ui/separator';
import { useCompanies, useProducts, useRetailers } from '@/hooks/use-catalog';
import { usePlaceOrder } from '@/hooks/use-orders';
import type { PlaceOrderLine, PlaceOrderRequest, PlaceOrderResponse, Product } from '@/lib/api-types';
import { currencyExponent, draftLineTotal, formatMinorUnits, parseDecimalToMinorUnits, toDecimalString } from '@/lib/money';
import { describeError, stockShortages } from '@/lib/problem';

/**
 * A draft line keeps what the operator TYPED, as text. The minor-unit integers
 * the wire needs are derived only when needed (running total, submit), so an
 * in-progress keystroke like "19." is never reformatted under the cursor, and a
 * typed "19.99" can never pass through a float on its way to 1999.
 */
interface DraftLine {
  key: number;
  productCode: string;
  quantityInput: string;
  unitPriceInput: string;
  lineDiscountInput: string;
}

/** The R42 demo: one line whose total ends in .99, which the credit simulator refuses → the saga compensates. */
const COMPENSATION_DEMO_UNIT_PRICE = 24_999;

/** Shown only when a failed catalogue answer carried no problem text at all (an HTML error page, an empty body). */
const CATALOG_FALLBACK = 'The catalogue could not be loaded.';

function parseQuantity(input: string): number | undefined {
  const trimmed = input.trim();
  if (!/^\d+$/.test(trimmed)) return undefined;
  const value = Number(trimmed);
  return Number.isSafeInteger(value) && value >= 1 ? value : undefined;
}

interface LineProblems {
  quantity?: string;
  unitPrice?: string;
  lineDiscount?: string;
}

export function PlaceOrderForm() {
  const retailers = useRetailers();
  const companies = useCompanies();
  const products = useProducts();
  const placeOrder = usePlaceOrder();

  // id 106: `DraftLine.key` used to come from a module-scope `let`, free-running
  // across every SSR render a long-lived `next start` process ever serves, and
  // recomputed from 1 in the browser — the two could disagree on the very
  // first render after any prior pass. A ref is per COMPONENT INSTANCE: a
  // fresh one is created for every render tree (every request server-side,
  // every mount client-side), so it starts at 1 every time, never leaking
  // state across unrelated requests. Refs may not be READ during render
  // (react-hooks/refs), so this one is only ever touched from event handlers
  // — the initial line below is a plain literal, not `emptyLine()`. The
  // label/select id pair itself is derived separately, from React's own
  // request/render-scoped `useId()` in OrderLineRow below — this ref only
  // keeps line identity for reconciliation and for `updateLine`/remove.
  const nextLineKeyRef = useRef(1);
  function emptyLine(): DraftLine {
    nextLineKeyRef.current += 1;
    return { key: nextLineKeyRef.current, productCode: '', quantityInput: '1', unitPriceInput: '', lineDiscountInput: '' };
  }

  const [retailerCode, setRetailerCode] = useState('');
  const [companyCode, setCompanyCode] = useState('');
  const [currency, setCurrency] = useState('EUR');
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<DraftLine[]>(() => [{ key: 1, productCode: '', quantityInput: '1', unitPriceInput: '', lineDiscountInput: '' }]);
  const [showProblems, setShowProblems] = useState(false);
  const [accepted, setAccepted] = useState<PlaceOrderResponse | null>(null);

  const catalogFailed = retailers.isError || companies.isError || products.isError;
  // Each failing catalogue query's OWN words (problem `detail`, else `title`) —
  // one line per distinct message, so three identical 503s read once and two
  // different failures both reach the user (id 29 bullet 5; review B1).
  const catalogErrors = [...new Map([retailers, companies, products].filter((query) => query.isError).map((query) => [describeError(query.error, CATALOG_FALLBACK), query.error] as const)).values()];
  const retailersUsable = !retailers.isError && (retailers.data?.length ?? 0) > 0;
  const companiesUsable = !companies.isError && (companies.data?.length ?? 0) > 0;
  const productsUsable = !products.isError && (products.data?.length ?? 0) > 0;

  const exponent = currencyExponent(currency);
  const priceByCode = useMemo(() => new Map((products.data ?? []).map((product) => [product.code, product.price])), [products.data]);

  const currencies = useMemo(() => {
    const set = new Set<string>([currency]);
    for (const party of retailers.data ?? []) set.add(party.currency);
    for (const party of companies.data ?? []) set.add(party.currency);
    for (const product of products.data ?? []) set.add(product.currency);
    return [...set].sort();
  }, [currency, retailers.data, companies.data, products.data]);

  function lineProblems(line: DraftLine): LineProblems {
    const problems: LineProblems = {};
    if (parseQuantity(line.quantityInput) === undefined) problems.quantity = 'Enter a whole number of at least 1.';
    const decimals = exponent === 0 ? 'a whole amount' : `an amount with at most ${exponent} decimal${exponent === 1 ? '' : 's'}`;
    if (line.unitPriceInput.trim() && parseDecimalToMinorUnits(line.unitPriceInput, exponent) === undefined) problems.unitPrice = `Enter ${decimals} (${currency}).`;
    if (line.lineDiscountInput.trim() && parseDecimalToMinorUnits(line.lineDiscountInput, exponent) === undefined) problems.lineDiscount = `Enter ${decimals} (${currency}).`;
    return problems;
  }

  const runningTotal = lines
    .filter((line) => line.productCode)
    .reduce(
      (sum, line) =>
        sum +
        draftLineTotal(
          {
            quantity: parseQuantity(line.quantityInput) ?? 0,
            unitPrice: parseDecimalToMinorUnits(line.unitPriceInput, exponent),
            lineDiscount: parseDecimalToMinorUnits(line.lineDiscountInput, exponent),
          },
          priceByCode.get(line.productCode),
        ),
      0,
    );

  function selectRetailer(code: string) {
    setRetailerCode(code);
    // The retailer trades in one currency (Party.currency); follow it so the
    // order is never silently placed in the wrong one. The field stays editable.
    const selected = retailers.data?.find((retailer) => retailer.code === code);
    if (selected) setCurrency(selected.currency);
  }

  function updateLine(key: number, patch: Partial<DraftLine>) {
    setLines((current) => current.map((line) => (line.key === key ? { ...line, ...patch } : line)));
  }

  function fillCompensationDemo() {
    const retailer = retailers.data?.[0];
    const demoCurrency = retailer?.currency ?? currency;
    setRetailerCode(retailer?.code ?? 'CarrefourEs');
    setCompanyCode(companies.data?.[0]?.code ?? 'IBERFOODS');
    setCurrency(demoCurrency);
    setNotes('demo — compensation path (.99)');
    setLines([{ ...emptyLine(), productCode: products.data?.[0]?.code ?? 'PRD-0001', unitPriceInput: toDecimalString(COMPENSATION_DEMO_UNIT_PRICE, demoCurrency) }]);
  }

  function buildRequest(): PlaceOrderRequest | undefined {
    const chosen = lines.filter((line) => line.productCode.trim());
    if (!retailerCode.trim() || !companyCode.trim() || chosen.length === 0) return undefined;
    const wireLines: PlaceOrderLine[] = [];
    for (const line of chosen) {
      const quantity = parseQuantity(line.quantityInput);
      if (quantity === undefined) return undefined;
      const wire: PlaceOrderLine = { productCode: line.productCode.trim(), quantity };
      if (line.unitPriceInput.trim()) {
        const unitPrice = parseDecimalToMinorUnits(line.unitPriceInput, exponent);
        if (unitPrice === undefined) return undefined;
        wire.unitPrice = unitPrice;
      }
      if (line.lineDiscountInput.trim()) {
        const lineDiscount = parseDecimalToMinorUnits(line.lineDiscountInput, exponent);
        if (lineDiscount === undefined) return undefined;
        if (lineDiscount > 0) wire.lineDiscount = lineDiscount;
      }
      wireLines.push(wire);
    }
    const request: PlaceOrderRequest = { retailerCode: retailerCode.trim(), companyCode: companyCode.trim(), currency: currency.trim().toUpperCase(), lines: wireLines };
    if (notes.trim()) request.notes = notes.trim();
    return request;
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setAccepted(null);
    setShowProblems(true);
    const request = buildRequest();
    if (!request) return;
    placeOrder.mutate(
      { request, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (response) => {
          setAccepted(response);
          setLines([emptyLine()]);
          setShowProblems(false);
        },
      },
    );
  }

  const headerProblem = showProblems && (!retailerCode.trim() || !companyCode.trim() || !lines.some((line) => line.productCode.trim())) ? 'Choose a retailer, a company and at least one product.' : undefined;
  const shortages = placeOrder.isError ? stockShortages(placeOrder.error) : undefined;

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-semibold">Place order</h1>
        <Button type="button" variant="outline" onClick={fillCompensationDemo}>
          Fill demo order (.99 → compensation)
        </Button>
      </div>

      {catalogFailed ? (
        <div className="flex flex-col gap-1" data-testid="catalog-unavailable">
          {catalogErrors.map((error) => (
            <ErrorMessage key={describeError(error, CATALOG_FALLBACK)} error={error} fallback={CATALOG_FALLBACK} testId="catalog-error" />
          ))}
          <p className="text-sm text-muted-foreground" data-testid="catalog-manual-entry">
            Enter codes by hand meanwhile (e.g. retailer <code>CarrefourEs</code>, company <code>IBERFOODS</code>, product <code>PRD-0001</code>).
          </p>
        </div>
      ) : null}

      <Card>
        <CardHeader>
          <CardTitle>Order</CardTitle>
          <CardDescription>Placed with `POST /orders`: 201 means the order was accepted, not that the saga finished.</CardDescription>
        </CardHeader>
        <CardContent>
          <form method="post" className="flex flex-col gap-6" onSubmit={submit} noValidate data-testid="place-order-form">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_8rem]">
              <div className="flex min-w-0 flex-col gap-1.5">
                <Label htmlFor="retailer">Retailer</Label>
                {retailersUsable ? (
                  <NativeSelect id="retailer" wrapperClassName="w-full min-w-0" value={retailerCode} onChange={(event) => selectRetailer(event.target.value)} title={retailers.data?.find((r) => r.code === retailerCode)?.name}>
                    <NativeSelectOption value="">Select a retailer</NativeSelectOption>
                    {(retailers.data ?? []).map((retailer) => (
                      <NativeSelectOption key={retailer.code} value={retailer.code}>
                        {retailer.name} ({retailer.code})
                      </NativeSelectOption>
                    ))}
                  </NativeSelect>
                ) : (
                  <Input id="retailer" value={retailerCode} onChange={(event) => setRetailerCode(event.target.value)} placeholder="e.g. CarrefourEs" />
                )}
              </div>
              <div className="flex min-w-0 flex-col gap-1.5">
                <Label htmlFor="company">Company</Label>
                {companiesUsable ? (
                  <NativeSelect id="company" wrapperClassName="w-full min-w-0" value={companyCode} onChange={(event) => setCompanyCode(event.target.value)} title={companies.data?.find((c) => c.code === companyCode)?.name}>
                    <NativeSelectOption value="">Select a company</NativeSelectOption>
                    {(companies.data ?? []).map((company) => (
                      <NativeSelectOption key={company.code} value={company.code}>
                        {company.name} ({company.code})
                      </NativeSelectOption>
                    ))}
                  </NativeSelect>
                ) : (
                  <Input id="company" value={companyCode} onChange={(event) => setCompanyCode(event.target.value)} placeholder="e.g. IBERFOODS" />
                )}
              </div>
              <div className="flex min-w-0 flex-col gap-1.5">
                <Label htmlFor="currency">Currency</Label>
                <NativeSelect id="currency" wrapperClassName="w-full min-w-0" value={currency} onChange={(event) => setCurrency(event.target.value)}>
                  {currencies.map((code) => (
                    <NativeSelectOption key={code} value={code}>
                      {code}
                    </NativeSelectOption>
                  ))}
                </NativeSelect>
              </div>
            </div>

            <Separator />

            <div className="flex flex-col gap-3">
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium">Lines</span>
                <Button type="button" variant="outline" size="sm" onClick={() => setLines((current) => [...current, emptyLine()])}>
                  Add line
                </Button>
              </div>
              {lines.map((line, index) => (
                <OrderLineRow
                  key={line.key}
                  line={line}
                  index={index}
                  problems={showProblems ? lineProblems(line) : {}}
                  catalogPrice={priceByCode.get(line.productCode)}
                  currency={currency}
                  productsUsable={productsUsable}
                  products={products.data}
                  canRemove={lines.length > 1}
                  onUpdate={(patch) => updateLine(line.key, patch)}
                  onRemove={() => setLines((current) => current.filter((l) => l.key !== line.key))}
                />
              ))}
            </div>

            <Separator />

            <div className="flex items-center justify-between gap-4">
              <span className="text-sm text-muted-foreground">Running total (an estimate — the server computes the authoritative total)</span>
              <span className="text-lg font-semibold tabular-nums" data-testid="running-total">
                {formatMinorUnits(runningTotal, currency)}
              </span>
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="notes">Notes</Label>
              <Input id="notes" value={notes} onChange={(event) => setNotes(event.target.value)} />
            </div>

            {headerProblem ? (
              <p className="text-sm text-destructive" data-testid="place-order-incomplete">
                {headerProblem}
              </p>
            ) : null}

            {placeOrder.isError ? (
              <div className="flex flex-col gap-1" data-testid="place-order-failure">
                <ErrorMessage error={placeOrder.error} fallback="Placing the order failed." testId="place-order-error" />
                {shortages ? (
                  <ul className="list-disc pl-5 text-sm text-destructive" data-testid="place-order-shortages">
                    {shortages.map((shortage) => (
                      <li key={shortage.productCode}>
                        {shortage.productCode}: requested {shortage.requested}, only {shortage.available} available
                      </li>
                    ))}
                  </ul>
                ) : null}
              </div>
            ) : null}

            {accepted ? (
              <p className="text-sm text-emerald-700" data-testid="place-order-success">
                Order {accepted.orderReference} accepted.{' '}
                <Link href={`/orders/${accepted.orderId}`} className="underline" data-testid="accepted-order-link">
                  Follow it on its live timeline
                </Link>
                .
              </p>
            ) : null}

            <div>
              <Button type="submit" disabled={placeOrder.isPending}>
                {placeOrder.isPending ? 'Placing…' : 'Place order'}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}

interface OrderLineRowProps {
  line: DraftLine;
  index: number;
  problems: LineProblems;
  catalogPrice: number | undefined;
  currency: string;
  productsUsable: boolean;
  products: Product[] | undefined;
  canRemove: boolean;
  onUpdate: (patch: Partial<DraftLine>) => void;
  onRemove: () => void;
}

/**
 * id 106: the label/select id pair for one order line comes from `useId()`,
 * not from `line.key` — `useId()` is React's own request/render-scoped
 * generator (a fresh, stable id per component INSTANCE, matching between an
 * SSR pass and its hydration), so the pair cannot desync across the many
 * renders a long-lived `next start` process serves in the same module
 * instance, the way the old module-scope `nextLineKey` counter did.
 */
function OrderLineRow({ line, index, problems, catalogPrice, currency, productsUsable, products, canRemove, onUpdate, onRemove }: OrderLineRowProps) {
  const uid = useId();
  const productId = `${uid}-product`;
  const quantityId = `${uid}-quantity`;
  const unitPriceId = `${uid}-unit-price`;
  const lineDiscountId = `${uid}-line-discount`;
  return (
    <div className="grid grid-cols-1 items-start gap-3 sm:grid-cols-2 lg:grid-cols-[minmax(0,2fr)_6rem_minmax(0,1fr)_minmax(0,1fr)_auto]" data-testid="order-line">
      <div className="flex min-w-0 flex-col gap-1.5 sm:col-span-2 lg:col-span-1">
        <Label htmlFor={productId}>Product</Label>
        {productsUsable ? (
          <NativeSelect id={productId} wrapperClassName="w-full min-w-0" value={line.productCode} onChange={(event) => onUpdate({ productCode: event.target.value })}>
            <NativeSelectOption value="">Select a product</NativeSelectOption>
            {(products ?? []).map((product) => (
              <NativeSelectOption key={product.code} value={product.code}>
                {product.name} ({product.code}) — {formatMinorUnits(product.price, product.currency)}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        ) : (
          <Input id={productId} value={line.productCode} onChange={(event) => onUpdate({ productCode: event.target.value })} placeholder="e.g. PRD-0001" />
        )}
      </div>
      <div className="flex min-w-0 flex-col gap-1.5">
        <Label htmlFor={quantityId}>Quantity</Label>
        <Input id={quantityId} inputMode="numeric" value={line.quantityInput} aria-invalid={problems.quantity ? true : undefined} onChange={(event) => onUpdate({ quantityInput: event.target.value })} data-testid="quantity-input" />
        {problems.quantity ? <span className="text-xs text-destructive">{problems.quantity}</span> : null}
      </div>
      <div className="flex min-w-0 flex-col gap-1.5">
        <Label htmlFor={unitPriceId}>Unit price override</Label>
        <Input
          id={unitPriceId}
          inputMode="decimal"
          placeholder={catalogPrice !== undefined ? toDecimalString(catalogPrice, currency) : 'catalogue'}
          value={line.unitPriceInput}
          aria-invalid={problems.unitPrice ? true : undefined}
          onChange={(event) => onUpdate({ unitPriceInput: event.target.value })}
          data-testid="unit-price-input"
        />
        {problems.unitPrice ? <span className="text-xs text-destructive">{problems.unitPrice}</span> : null}
      </div>
      <div className="flex min-w-0 flex-col gap-1.5">
        <Label htmlFor={lineDiscountId}>Line discount</Label>
        <Input
          id={lineDiscountId}
          inputMode="decimal"
          placeholder={toDecimalString(0, currency)}
          value={line.lineDiscountInput}
          aria-invalid={problems.lineDiscount ? true : undefined}
          onChange={(event) => onUpdate({ lineDiscountInput: event.target.value })}
          data-testid="line-discount-input"
        />
        {problems.lineDiscount ? <span className="text-xs text-destructive">{problems.lineDiscount}</span> : null}
      </div>
      <div className="flex flex-col gap-1.5">
        <span className="hidden text-sm leading-none select-none lg:invisible lg:block" aria-hidden="true">
          &nbsp;
        </span>
        <Button type="button" variant="ghost" size="sm" disabled={!canRemove} onClick={onRemove} aria-label={`Remove line ${index + 1}`}>
          Remove
        </Button>
      </div>
    </div>
  );
}
