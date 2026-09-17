'use client';

import Link from 'next/link';
import { useState } from 'react';
import { DateTime } from '@/components/date-time';
import { ErrorMessage } from '@/components/error-message';
import { Pager } from '@/components/pager';
import { OrderStatusBadge } from '@/components/status-badge';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useRetailers } from '@/hooks/use-catalog';
import { useOrders, type OrderListFilters } from '@/hooks/use-orders';
import { ORDER_STATUSES, type OrderStatus } from '@/lib/api-types';
import { formatMinorUnits } from '@/lib/money';

const PAGE_SIZE = 20;

/** The order list (read model, R54): filters, paging, and three distinct states — loading, empty, error. */
export function OrdersList() {
  const [filters, setFilters] = useState<OrderListFilters>({ page: 1, pageSize: PAGE_SIZE });
  const orders = useOrders(filters);
  const retailers = useRetailers();

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">Orders</h1>
        <Button asChild>
          <Link href="/orders/place">Place order</Link>
        </Button>
      </div>

      <div className="flex flex-wrap items-end gap-4">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="order-status-filter">Status</Label>
          <NativeSelect
            id="order-status-filter"
            className="w-48"
            value={filters.status ?? ''}
            onChange={(event) => setFilters((current) => ({ ...current, status: (event.target.value || undefined) as OrderStatus | undefined, page: 1 }))}
          >
            <NativeSelectOption value="">All statuses</NativeSelectOption>
            {ORDER_STATUSES.map((status) => (
              <NativeSelectOption key={status} value={status}>
                {status}
              </NativeSelectOption>
            ))}
          </NativeSelect>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="order-retailer-filter">Retailer</Label>
          <NativeSelect
            id="order-retailer-filter"
            className="w-64"
            value={filters.retailerCode ?? ''}
            onChange={(event) => setFilters((current) => ({ ...current, retailerCode: event.target.value || undefined, page: 1 }))}
          >
            <NativeSelectOption value="">All retailers</NativeSelectOption>
            {(retailers.data ?? []).map((retailer) => (
              <NativeSelectOption key={retailer.code} value={retailer.code}>
                {retailer.name} ({retailer.code})
              </NativeSelectOption>
            ))}
          </NativeSelect>
          {retailers.isError ? <ErrorMessage error={retailers.error} prefix="Retailer filter unavailable" fallback="the retailer list could not be loaded" testId="order-retailer-filter-error" /> : null}
        </div>
        {orders.isFetching && !orders.isPending ? <span className="text-xs text-muted-foreground">refreshing…</span> : null}
      </div>

      {orders.isError ? (
        <ErrorMessage error={orders.error} prefix="Could not load orders" fallback="the request failed" testId="orders-error" />
      ) : orders.isPending ? (
        <p className="text-sm text-muted-foreground" data-testid="orders-loading">
          Loading orders…
        </p>
      ) : orders.data.items.length === 0 ? (
        <p className="rounded-md border border-dashed p-6 text-sm text-muted-foreground" data-testid="orders-empty">
          No orders match these filters.
        </p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead scope="col">Reference</TableHead>
              <TableHead scope="col">Date</TableHead>
              <TableHead scope="col">Retailer</TableHead>
              <TableHead scope="col">Company</TableHead>
              <TableHead scope="col">Status</TableHead>
              <TableHead scope="col" className="text-right">
                Total
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {orders.data.items.map((order) => (
              <TableRow key={order.orderId} data-testid="order-row">
                <TableCell className="font-medium">
                  <Link href={`/orders/${order.orderId}`} className="hover:underline">
                    {order.orderReference}
                  </Link>
                </TableCell>
                <TableCell>
                  <DateTime value={order.orderDate} />
                </TableCell>
                <TableCell>{order.retailer.name ?? order.retailer.code}</TableCell>
                <TableCell>{order.company.name ?? order.company.code}</TableCell>
                <TableCell>
                  <OrderStatusBadge status={order.status} />
                </TableCell>
                <TableCell className="text-right tabular-nums" data-testid="order-total">
                  {formatMinorUnits(order.totals.totalAmount, order.currency)}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {orders.isError ? null : <Pager page={orders.data?.page} current={filters.page} noun="orders" onChange={(page) => setFilters((current) => ({ ...current, page }))} testId="orders-pager" />}
    </div>
  );
}
