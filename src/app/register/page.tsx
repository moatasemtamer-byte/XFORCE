import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/auth-shell";
import { RegisterForm } from "@/components/forms/auth-forms";
import { getCurrentPublicUser } from "@/lib/auth";

export const metadata: Metadata = {
  title: "Create account",
  description: "Start converting ladder diagrams to MSW with five free conversions every month.",
};

export default async function RegisterPage() {
  if (await getCurrentPublicUser()) redirect("/dashboard");

  return (
    <AuthShell
      title="Create your account"
      subtitle="Free forever tier: 5 conversions per month, no card required."
      footer={
        <Link href="/">← Back to xforcemsw.com</Link>
      }
    >
      <RegisterForm />
    </AuthShell>
  );
}
