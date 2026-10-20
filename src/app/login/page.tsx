import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/auth-shell";
import { LoginForm } from "@/components/forms/auth-forms";
import { getCurrentPublicUser } from "@/lib/auth";

export const metadata: Metadata = {
  title: "Sign in",
  description: "Sign in to your XforceMsw dashboard to convert ladder diagrams and manage licenses.",
};

const NOTICES: Record<string, string> = {
  sign_in_required:
    "That email already has a password on this account, so we did not sign you in automatically. Your license is linked to it — enter your password to continue.",
};

export default async function LoginPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  if (await getCurrentPublicUser()) redirect("/dashboard");

  const { error } = await searchParams;
  const notice = error ? NOTICES[error] : undefined;

  return (
    <AuthShell
      title="Sign in"
      subtitle="Access your conversions, licenses and job history."
      notice={
        notice ? (
          <p role="alert" className="border border-nx-green-700/40 bg-nx-green-50 px-3.5 py-2.5 text-xs text-nx-green-700">
            {notice}
          </p>
        ) : undefined
      }
      footer={
        <Link href="/">← Back to xforcemsw.com</Link>
      }
    >
      <LoginForm />
    </AuthShell>
  );
}
