import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/auth-shell";
import { RegisterForm } from "@/components/forms/auth-forms";
import { getCurrentPublicUser } from "@/lib/auth";

export const metadata: Metadata = {
  title: "Create account",
  description: "Create your XforceMsw account using the license key issued by your administrator.",
};

export default async function RegisterPage() {
  if (await getCurrentPublicUser()) redirect("/dashboard");

  return (
    <AuthShell
      title="Create your account"
      subtitle="Enter the license key your administrator issued. Each key allows a set number of accounts."
      footer={
        <Link href="/">← Back to xforcemsw.com</Link>
      }
    >
      <RegisterForm />
    </AuthShell>
  );
}
