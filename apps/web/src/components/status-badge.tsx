import { Badge } from '@/components/ui/badge';
import type { OrderStatus } from '@/lib/api-types';

export function orderStatusVariant(status: OrderStatus): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === 'cancelled') return 'destructive';
  if (status === 'completed' || status === 'paid') return 'secondary';
  return 'outline';
}

export function OrderStatusBadge({ status, testId }: { status: OrderStatus; testId?: string }) {
  return (
    <Badge variant={orderStatusVariant(status)} data-testid={testId}>
      {status}
    </Badge>
  );
}
