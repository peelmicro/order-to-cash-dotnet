'use client';

import { useQuery } from '@tanstack/react-query';
import type { CatalogPartiesResponse, CatalogProductsResponse } from '@/lib/api-types';
import { apiGet } from '@/lib/api-client';

/** Reference data for the place-order form and the retailer filters. Rarely changes, hence the long `staleTime`. */
export function useProducts() {
  return useQuery({
    queryKey: ['catalog', 'products'],
    queryFn: async () => (await apiGet<CatalogProductsResponse>('/api/catalog/products')).items,
    staleTime: 60_000,
  });
}

export function useRetailers() {
  return useQuery({
    queryKey: ['catalog', 'retailers'],
    queryFn: async () => (await apiGet<CatalogPartiesResponse>('/api/catalog/retailers')).items,
    staleTime: 60_000,
  });
}

export function useCompanies() {
  return useQuery({
    queryKey: ['catalog', 'companies'],
    queryFn: async () => (await apiGet<CatalogPartiesResponse>('/api/catalog/companies')).items,
    staleTime: 60_000,
  });
}
