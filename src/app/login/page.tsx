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

export default async function LoginPage() {
  if (await getCurrentPublicUser()) redirect("/dashboard");

  return (
    <AuthShell
      title="Sign in"
      subtitle="Access your conversions, licenses and job history."
      footer={
        <Link href="/">← Back to xforcemsw.com</Link>
      }
    >
      <LoginForm />
    </AuthShell>
  );
}
