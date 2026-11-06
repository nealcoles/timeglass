import type { AnchorHTMLAttributes } from 'react';
import { followLink } from './router.ts';

type LinkProps = AnchorHTMLAttributes<HTMLAnchorElement> & { href: string };

/** A link to another screen of the dashboard, followed without reloading the page. */
export function Link({ onClick, ...props }: LinkProps) {
  return (
    <a
      {...props}
      onClick={(event) => {
        onClick?.(event);
        followLink(event);
      }}
    />
  );
}
