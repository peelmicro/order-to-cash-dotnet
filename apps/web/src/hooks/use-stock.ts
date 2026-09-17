'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ReplenishStockRequest, ReplenishStockResponse, StockPage } from '@/lib/api-types';
import { apiGet, apiRequest } from '@/lib/api-client';

export interface StockFilters {
  companyCode?: string;
  productCode?: string;
  belowThreshold: boolean;
  page: number;
  pageSize: number;
}

/** `GET /api/stock` — a live read of Fulfillment's write model (no projection lag). */
export function useStock(filters: StockFilters) {
  return useQuery({
    queryKey: ['stock', filters],
    queryFn: () => apiGet<StockPage>('/api/stock', { companyCode: filters.companyCode, productCode: filters.productCode, belowThreshold: filters.belowThreshold || undefined, page: filters.page, pageSize: filters.pageSize }),
    refetchInterval: 5_000,
    placeholderData: keepPreviousData,
  });
}

/** `POST /api/stock/replenish` — `units` is a DELTA added to on-hand stock, never a target level. Emits no fact. */
export function useReplenishStock() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (request: ReplenishStockRequest) => (await apiRequest<ReplenishStockResponse>('/api/stock/replenish', { method: 'POST', json: request })).data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['stock'] }),
  });
}
