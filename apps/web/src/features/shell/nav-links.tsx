'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { cn } from '@/lib/utils';

const LINKS = [
  { href: '/orders', label: 'Orders', exact: true },
  { href: '/orders/place', label: 'Place order', exact: true },
  { href: '/stock', label: 'Stock', exact: false },
  { href: '/billing', label: 'Billing', exact: false },
] as const;

export function NavLinks() {
  const pathname = usePathname();
  return (
    <nav className="flex flex-wrap items-center gap-x-4 gap-y-1" aria-label="Main">
      <Link href="/orders" className="text-sm font-semibold">
        Order-To-Cash
      </Link>
      {LINKS.map((link) => {
        const active = link.exact ? pathname === link.href : pathname.startsWith(link.href);
        return (
          <Link key={link.href} href={link.href} aria-current={active ? 'page' : undefined} className={cn('text-sm text-muted-foreground hover:text-foreground', active && 'font-medium text-foreground')}>
            {link.label}
          </Link>
        );
      })}
    </nav>
  );
}
