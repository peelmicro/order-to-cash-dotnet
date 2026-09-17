'use client';

import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { CreditPage, InvoicePage, InvoiceStatus, RegisterPaymentRequest, RegisterPaymentResponse } from '@/lib/api-types';
import { apiGet, apiRequest } from '@/lib/api-client';

export interface InvoiceFilters {
  status?: InvoiceStatus;
  retailerCode?: string;
  page: number;
  pageSize: number;
}

export function useInvoices(filters: InvoiceFilters) {
  return useQuery({
    queryKey: ['invoices', filters],
    queryFn: () => apiGet<InvoicePage>('/api/invoices', { status: filters.status, retailerCode: filters.retailerCode, page: filters.page, pageSize: filters.pageSize }),
    refetchInterval: 5_000,
    placeholderData: keepPreviousData,
  });
}

export interface CreditFilters {
  retailerCode?: string;
  page: number;
  pageSize: number;
}

export function useCredits(filters: CreditFilters) {
  return useQuery({
    queryKey: ['credits', filters],
    queryFn: () => apiGet<CreditPage>('/api/credits', { retailerCode: filters.retailerCode, page: filters.page, pageSize: filters.pageSize }),
    refetchInterval: 5_000,
    placeholderData: keepPreviousData,
  });
}

export interface RegisterPaymentVariables {
  invoiceId: string;
  request: RegisterPaymentRequest;
}

/** The server's answer and whether it recorded a NEW payment (`201`/`accepted`) or replayed one (`200`/`duplicate`, R48). */
export interface RegisterPaymentResult {
  status: number;
  response: RegisterPaymentResponse;
}

/** `POST /api/invoices/{id}/payments` — `paymentReference` in the body is the idempotency key (B10). */
export function useRegisterPayment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ invoiceId, request }: RegisterPaymentVariables): Promise<RegisterPaymentResult> => {
      const { status, data } = await apiRequest<RegisterPaymentResponse>(`/api/invoices/${encodeURIComponent(invoiceId)}/payments`, { method: 'POST', json: request });
      return { status, response: data };
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['invoices'] });
      void queryClient.invalidateQueries({ queryKey: ['credits'] });
    },
  });
}
