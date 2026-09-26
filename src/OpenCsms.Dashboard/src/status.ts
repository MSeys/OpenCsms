/** One vocabulary for every state the API returns, so a badge never guesses. */

const TONES: Record<string, string> = {
  available: "success",
  charging: "accent",
  preparing: "info",
  finishing: "info",
  reserved: "info",
  suspendedevse: "warning",
  suspendedev: "warning",
  unavailable: "neutral",
  faulted: "danger",
  open: "warning",
  ended: "success"
};

const LABELS: Record<string, string> = {
  suspendedevse: "Suspended EVSE",
  suspendedev: "Suspended EV"
};

export function statusTone(status: string): string {
  return TONES[status.toLowerCase()] ?? "neutral";
}

export function statusLabel(status: string): string {
  return LABELS[status.toLowerCase()] ?? status;
}
