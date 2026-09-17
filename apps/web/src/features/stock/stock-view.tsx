'use client';

import { Fragment, useMemo, useState, type FormEvent } from 'react';
import { ErrorMessage } from '@/components/error-message';
import { Pager } from '@/components/pager';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useProducts } from '@/hooks/use-catalog';
import { useReplenishStock, useStock, type StockFilters } from '@/hooks/use-stock';
import type { ReplenishStockResponse, StockItem } from '@/lib/api-types';

const PAGE_SIZE = 20;

function rowKey(item: Pick<StockItem, 'companyCode' | 'productCode'>): string {
  return `${item.companyCode}:${item.productCode}`;
}

/**
 * `StockItem.productName` (a backend fact, when one is ever sent) wins over the
 * catalog's own `name`; `undefined` when neither is known, so the caller shows
 * the code once rather than repeating it (id 101).
 */
export function productDisplayName(item: Pick<StockItem, 'productName' | 'productCode'>, catalogNameByCode: Map<string, string>): string | undefined {
  return item.productName ?? catalogNameByCode.get(item.productCode);
}

/** Stock: a live read of Fulfillment (on hand, reserved, available = on hand − reserved, invariant F1) with a delta replenish per line. */
export function StockView() {
  const [filters, setFilters] = useState<StockFilters>({ belowThreshold: false, page: 1, pageSize: PAGE_SIZE });
  const stock = useStock(filters);
  const products = useProducts();
  const productNameByCode = useMemo(() => new Map((products.data ?? []).map((product) => [product.code, product.name])), [products.data]);
  const replenish = useReplenishStock();
  const [active, setActive] = useState<StockItem | null>(null);
  const [unitsInput, setUnitsInput] = useState('');
  const [unitsProblem, setUnitsProblem] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<{ item: StockItem; units: number; response: ReplenishStockResponse } | null>(null);

  function open(item: StockItem) {
    setActive(item);
    setUnitsInput('');
    setUnitsProblem(null);
    setOutcome(null);
    // A failed attempt on another line must not reappear on this one (#7 Pass 6 follow-up).
    replenish.reset();
  }

  function close() {
    setActive(null);
    setOutcome(null);
    replenish.reset();
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!active) return;
    const trimmed = unitsInput.trim();
    const units = /^\d+$/.test(trimmed) ? Number(trimmed) : Number.NaN;
    if (!Number.isSafeInteger(units) || units < 1) {
      setUnitsProblem('Enter a whole number of units to add (at least 1).');
      return;
    }
    setUnitsProblem(null);
    const item = active;
    replenish.mutate(
      { companyCode: item.companyCode, lines: [{ productCode: item.productCode, units }] },
      { onSuccess: (response) => setOutcome({ item, units, response }) },
    );
  }

  const applyTextFilter = (key: 'companyCode' | 'productCode', value: string) => setFilters((current) => ({ ...current, [key]: value.trim() || undefined, page: 1 }));

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-xl font-semibold">Stock</h1>
        <p className="text-sm text-muted-foreground">A live read of Fulfillment&apos;s on-hand and reserved units — not the order read model, so there is no projection lag. Available = on hand − reserved.</p>
      </div>

      {/* A failed catalog read never hides the stock table below — rows fall back to the product code alone (id 101). */}
      {products.isError ? <ErrorMessage error={products.error} prefix="Product names unavailable" fallback="the catalog could not be loaded" testId="stock-products-error" /> : null}

      <div className="flex flex-wrap items-end gap-4">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="stock-company-filter">Company</Label>
          <Input id="stock-company-filter" className="w-48" placeholder="e.g. IBERFOODS" defaultValue={filters.companyCode} onChange={(event) => applyTextFilter('companyCode', event.target.value)} />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="stock-product-filter">Product</Label>
          <Input id="stock-product-filter" className="w-48" placeholder="e.g. PRD-0001" defaultValue={filters.productCode} onChange={(event) => applyTextFilter('productCode', event.target.value)} />
        </div>
        <Button type="button" variant={filters.belowThreshold ? 'default' : 'outline'} aria-pressed={filters.belowThreshold} onClick={() => setFilters((current) => ({ ...current, belowThreshold: !current.belowThreshold, page: 1 }))} data-testid="below-threshold-toggle">
          Low stock only
        </Button>
        {stock.isFetching && !stock.isPending ? <span className="text-xs text-muted-foreground">refreshing…</span> : null}
      </div>

      {stock.isError ? (
        <ErrorMessage error={stock.error} prefix="Could not load stock" fallback="the request failed" testId="stock-error" />
      ) : stock.isPending ? (
        <p className="text-sm text-muted-foreground" data-testid="stock-loading">
          Loading stock…
        </p>
      ) : stock.data.items.length === 0 ? (
        <p className="rounded-md border border-dashed p-6 text-sm text-muted-foreground" data-testid="stock-empty">
          No stock lines match these filters.
        </p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead scope="col">Company</TableHead>
              <TableHead scope="col">Product</TableHead>
              <TableHead scope="col" className="text-right">
                On hand
              </TableHead>
              <TableHead scope="col" className="text-right">
                Reserved
              </TableHead>
              <TableHead scope="col" className="text-right">
                Available
              </TableHead>
              <TableHead scope="col" className="text-right">
                Threshold
              </TableHead>
              <TableHead scope="col">
                <span className="sr-only">Actions</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {stock.data.items.map((item) => {
              const isActive = active !== null && rowKey(active) === rowKey(item);
              return (
                <Fragment key={rowKey(item)}>
                  <TableRow data-testid="stock-row">
                    <TableCell>{item.companyCode}</TableCell>
                    <TableCell className="font-medium" data-testid="stock-product">
                      {(() => {
                        const name = productDisplayName(item, productNameByCode);
                        return name ? (
                          <>
                            {name} <span className="text-xs text-muted-foreground">({item.productCode})</span>
                          </>
                        ) : (
                          item.productCode
                        );
                      })()}
                    </TableCell>
                    <TableCell className="text-right tabular-nums" data-testid="stock-units">
                      {item.units}
                    </TableCell>
                    <TableCell className="text-right tabular-nums" data-testid="stock-reserved">
                      {item.reservedUnits}
                    </TableCell>
                    <TableCell className="text-right" data-testid="stock-available">
                      <Badge variant={item.availableUnits < item.lowStockThreshold ? 'destructive' : 'secondary'}>{item.availableUnits}</Badge>
                    </TableCell>
                    <TableCell className="text-right text-muted-foreground tabular-nums">{item.lowStockThreshold}</TableCell>
                    <TableCell className="text-right">
                      {isActive ? null : (
                        <Button type="button" size="sm" variant="outline" onClick={() => open(item)} data-testid="replenish-button">
                          Replenish
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                  {isActive ? (
                    <TableRow>
                      <TableCell colSpan={7}>
                        <div className="flex flex-col gap-3 rounded-md border p-4" data-testid="replenish-form">
                          {outcome ? (
                            <>
                              <p className="text-sm text-emerald-700" data-testid="replenish-outcome">
                                Added {outcome.units} units to {outcome.item.productCode}
                                {/* An answered replenish that omits this line has no new level to show — say so, never an ellipsis that reads as still working. */}
                                {(() => {
                                  const updated = outcome.response.items.find((i) => rowKey(i) === rowKey(outcome.item));
                                  return updated ? ` — on hand is now ${updated.units}.` : ' — the answer did not include this line’s new on-hand level.';
                                })()}
                              </p>
                              <div>
                                <Button type="button" size="sm" variant="outline" onClick={close}>
                                  Close
                                </Button>
                              </div>
                            </>
                          ) : (
                            <form method="post" onSubmit={submit} noValidate className="flex flex-col gap-3">
                              <p className="text-xs text-muted-foreground">
                                This <strong>adds</strong> to on-hand stock — a delta, not a target level. Submitting the same amount twice adds it twice; reservations are untouched.
                              </p>
                              <div className="flex flex-wrap items-end gap-3">
                                <div className="flex flex-col gap-1.5">
                                  <Label htmlFor="replenish-units">Units to add</Label>
                                  <Input id="replenish-units" className="w-36" inputMode="numeric" placeholder="e.g. 100" value={unitsInput} onChange={(event) => setUnitsInput(event.target.value)} data-testid="replenish-units-input" />
                                </div>
                                <Button type="submit" disabled={replenish.isPending} data-testid="submit-replenish-button">
                                  {replenish.isPending ? 'Adding…' : 'Add units'}
                                </Button>
                                <Button type="button" variant="ghost" onClick={close}>
                                  Cancel
                                </Button>
                              </div>
                              {unitsProblem ? <p className="text-sm text-destructive">{unitsProblem}</p> : null}
                              {replenish.isError ? <ErrorMessage error={replenish.error} fallback="Replenishing stock failed." testId="replenish-error" /> : null}
                            </form>
                          )}
                        </div>
                      </TableCell>
                    </TableRow>
                  ) : null}
                </Fragment>
              );
            })}
          </TableBody>
        </Table>
      )}

      {stock.isError ? null : <Pager page={stock.data?.page} current={filters.page} noun="stock lines" onChange={(page) => setFilters((current) => ({ ...current, page }))} testId="stock-pager" />}
    </div>
  );
}
