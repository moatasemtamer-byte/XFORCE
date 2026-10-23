// Carries a freshly built .pt2/.pt2x from a conversion result to the project viewer.
//
// A window-level slot is used instead of sessionStorage: TriStation containers are
// tens of megabytes and their base64 form is another third larger, so storing one in
// sessionStorage throws "exceeded the quota" (~5 MB). The handoff only needs to
// survive client-side navigation, which a global is enough for.

export interface ViewerHandoff {
  name: string;
  base64: string;
}

interface HandoffSlot {
  pending: ViewerHandoff | null;
}

declare global {
  interface Window {
    __xmswViewerHandoff?: HandoffSlot;
  }
}

function slot(): HandoffSlot {
  if (typeof window === "undefined") return { pending: null };
  window.__xmswViewerHandoff ??= { pending: null };
  return window.__xmswViewerHandoff;
}

export function setViewerHandoff(handoff: ViewerHandoff): void {
  slot().pending = handoff;
}

export function takeViewerHandoff(): ViewerHandoff | null {
  const store = slot();
  const pending = store.pending;
  store.pending = null;
  return pending;
}
