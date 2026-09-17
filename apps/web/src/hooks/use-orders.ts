'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { OrderStatus, OrderSummaryPage, PlaceOrderRequest, PlaceOrderResponse } from '@/lib/api-types';
import { apiGet, apiRequest } from '@/lib/api-client';

export interface OrderListFilters {
  status?: OrderStatus;
  retailerCode?: string;
  page: number;
  pageSize: number;
}

/** `GET /api/orders` — the read model (R54). Polled: the list has no stream of its own in the contract. */
export function useOrders(filters: OrderListFilters) {
  return useQuery({
    queryKey: ['orders', filters],
    queryFn: () => apiGet<OrderSummaryPage>('/api/orders', { status: filters.status ? [filters.status] : undefined, retailerCode: filters.retailerCode, page: filters.page, pageSize: filters.pageSize }),
    refetchInterval: 5_000,
    placeholderData: keepPreviousData,
  });
}

/** Resolves an order's id from its business reference (invoices link to orders by `orderReference`, never by id). */
export function useOrderIdByReference(orderReference: string | undefined) {
  return useQuery({
    queryKey: ['orders', 'by-reference', orderReference],
    queryFn: async () => (await apiGet<OrderSummaryPage>('/api/orders', { orderReference, page: 1, pageSize: 1 })).items[0]?.orderId ?? null,
    enabled: Boolean(orderReference),
  });
}

export interface PlaceOrderVariables {
  request: PlaceOrderRequest;
  idempotencyKey: string;
}

/** `POST /api/orders`. `201` means accepted — not queryable yet, not complete. */
export function usePlaceOrder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ request, idempotencyKey }: PlaceOrderVariables) =>
      (await apiRequest<PlaceOrderResponse>('/api/orders', { method: 'POST', json: request, headers: { 'Idempotency-Key': idempotencyKey } })).data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['orders'] }),
  });
}
