import { Badge } from '@/components/ui/badge';
import { STACK_LABEL } from '@/lib/stack-label';

/** A small, quiet badge naming the stack (backlog id 99). */
export function StackLabel({ className }: { className?: string }) {
  return (
    <Badge variant="outline" className={className ?? 'font-normal text-muted-foreground'} data-testid="stack-label">
      {STACK_LABEL}
    </Badge>
  );
}
