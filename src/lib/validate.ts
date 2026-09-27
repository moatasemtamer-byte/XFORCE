const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

export function isEmail(value: string): boolean {
  const email = value.trim();
  return email.length <= 254 && EMAIL_PATTERN.test(email);
}

export function clean(value: FormDataEntryValue | null, maxLength = 500): string {
  if (typeof value !== "string") return "";
  return value.replace(/[\u0000-\u001f]/g, " ").trim().slice(0, maxLength);
}

export function isSafeRelativePath(value: string): boolean {
  return (
    value.length > 0 &&
    !value.startsWith("/") &&
    !value.includes("..") &&
    !/^[A-Za-z]:/.test(value) &&
    !value.includes("\\")
  );
}
