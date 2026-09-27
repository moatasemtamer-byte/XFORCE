"use client";

import { useActionState } from "react";
import {
  submitContactAction,
  type ContactFormState,
} from "@/lib/actions/contact-action";
import { Button, Field, inputClass } from "@/components/ui";

const initial: ContactFormState = {};

export function ContactForm() {
  const [state, action, pending] = useActionState(submitContactAction, initial);

  if (state.success) {
    return (
      <div className="rounded-lg border border-good-500/40 bg-good-500/10 px-4 py-5">
        <p className="text-sm font-semibold text-good-500">Message received</p>
        <p className="mt-1.5 text-xs leading-relaxed text-mist-300">
          Thanks — we read everything ourselves and usually reply within one business day.
        </p>
      </div>
    );
  }

  return (
    <form action={action} className="space-y-5" noValidate>
      {state.error ? (
        <p
          role="alert"
          className="rounded-lg border border-bad-500/40 bg-bad-500/10 px-3.5 py-2.5 text-xs text-bad-500"
        >
          {state.error}
        </p>
      ) : null}

      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="Name" htmlFor="name">
          <input id="name" name="name" required className={inputClass} placeholder="Your name" />
        </Field>
        <Field label="Email" htmlFor="email">
          <input
            id="email"
            name="email"
            type="email"
            required
            className={inputClass}
            placeholder="you@company.com"
          />
        </Field>
      </div>

      <Field label="Subject" htmlFor="subject" hint="Optional.">
        <input
          id="subject"
          name="subject"
          className={inputClass}
          placeholder="Ladder to MSW pilot project"
        />
      </Field>

      <Field label="Message" htmlFor="body">
        <textarea
          id="body"
          name="body"
          rows={5}
          required
          className={inputClass}
          placeholder="Tell us about your drawings, volumes and target format."
        />
      </Field>

      <Button type="submit" disabled={pending}>
        {pending ? "Sending…" : "Send message"}
      </Button>
    </form>
  );
}
