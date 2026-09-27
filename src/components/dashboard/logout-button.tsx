"use client";

import { useState } from "react";
import { logoutAction } from "@/lib/actions/auth-actions";
import { Button } from "@/components/ui";

export function LogoutButton({ className }: { className?: string }) {
  const [pending, setPending] = useState(false);

  return (
    <form
      action={logoutAction}
      onSubmit={() => setPending(true)}
      className={className}
    >
      <Button type="submit" variant="ghost" size="sm" disabled={pending}>
        {pending ? "Signing out…" : "Sign out"}
      </Button>
    </form>
  );
}
