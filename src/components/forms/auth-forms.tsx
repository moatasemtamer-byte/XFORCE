"use client";

import Link from "next/link";
import { useActionState } from "react";
import { loginAction, registerAction, type AuthFormState } from "@/lib/actions/auth-actions";
import { Button, Field, inputClass } from "@/components/ui";

const initial: AuthFormState = {};

export function LoginForm() {
  const [state, action, pending] = useActionState(loginAction, initial);

  return (
    <form action={action} className="space-y-5" noValidate>
      {state.error ? (
        <p role="alert" className="border border-nx-fail-fg/40 bg-nx-fail-bg px-3.5 py-2.5 text-xs text-nx-fail-fg">
          {state.error}
        </p>
      ) : null}

      <Field label="Work email" htmlFor="email" error={state.fieldErrors?.email}>
        <input
          id="email"
          name="email"
          type="email"
          autoComplete="email"
          required
          className={inputClass}
          placeholder="you@company.com"
        />
      </Field>

      <Field label="Password" htmlFor="password">
        <input
          id="password"
          name="password"
          type="password"
          autoComplete="current-password"
          required
          className={inputClass}
          placeholder="••••••••••"
        />
      </Field>

      <label className="flex items-center gap-2.5 text-xs text-nx-body">
        <input
          type="checkbox"
          name="remember"
          className="h-4 w-4 rounded-none border-nx-line-strong bg-nx-white accent-nx-green-700"
        />
        Keep me signed in on this device
      </label>

      <Button type="submit" className="w-full" disabled={pending}>
        {pending ? "Signing in…" : "Sign in"}
      </Button>

      <p className="text-center text-xs text-nx-hint">
        No account yet?{" "}
        <Link href="/register" className="font-semibold text-nx-green-700 underline-offset-4 hover:underline">
          Create one with a license key
        </Link>
      </p>
    </form>
  );
}

export function RegisterForm() {
  const [state, action, pending] = useActionState(registerAction, initial);

  return (
    <form action={action} className="space-y-5" noValidate>
      {state.error ? (
        <p role="alert" className="border border-nx-fail-fg/40 bg-nx-fail-bg px-3.5 py-2.5 text-xs text-nx-fail-fg">
          {state.error}
        </p>
      ) : null}

      <Field label="Full name" htmlFor="name" error={state.fieldErrors?.name}>
        <input id="name" name="name" autoComplete="name" required className={inputClass} placeholder="Mohamed Adel" />
      </Field>

      <Field label="Work email" htmlFor="email" error={state.fieldErrors?.email}>
        <input
          id="email"
          name="email"
          type="email"
          autoComplete="email"
          required
          className={inputClass}
          placeholder="you@company.com"
        />
      </Field>

      <Field label="Company" htmlFor="company" hint="Optional. Shown on invoices and in job history.">
        <input id="company" name="company" autoComplete="organization" className={inputClass} placeholder="Controls Dept." />
      </Field>

      <Field
        label="License key"
        htmlFor="licenseKey"
        error={state.fieldErrors?.licenseKey}
        hint="Required. Your administrator issues this key; each key allows a set number of accounts."
      >
        <input
          id="licenseKey"
          name="licenseKey"
          required
          className={inputClass}
          placeholder="XMSW-STD-XXXXX-XXXXX-XXXXX-XXXXX"
          autoComplete="off"
          spellCheck={false}
        />
      </Field>

      <Field
        label="Password"
        htmlFor="password"
        error={state.fieldErrors?.password}
        hint="At least 10 characters, with a letter and a number."
      >
        <input
          id="password"
          name="password"
          type="password"
          autoComplete="new-password"
          required
          className={inputClass}
          placeholder="••••••••••"
        />
      </Field>

      <Button type="submit" className="w-full" disabled={pending}>
        {pending ? "Creating account…" : "Create free account"}
      </Button>

      <p className="text-center text-xs text-nx-hint">
        Already registered?{" "}
        <Link href="/login" className="font-semibold text-nx-green-700 underline-offset-4 hover:underline">
          Sign in
        </Link>
      </p>
    </form>
  );
}
