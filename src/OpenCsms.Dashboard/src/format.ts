const dateTime = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  year: "numeric",
  hour: "numeric",
  minute: "2-digit"
});

export function formatDateTime(value: string | null): string {
  return value ? dateTime.format(new Date(value)) : "Never";
}

export function formatMoney(value: number, currency: string): string {
  try {
    return new Intl.NumberFormat("en-US", { style: "currency", currency }).format(value);
  } catch {
    return `${value.toFixed(2)} ${currency}`;
  }
}

export function formatKwh(value: number): string {
  return `${new Intl.NumberFormat("en-US", { maximumFractionDigits: 3 }).format(value)} kWh`;
}

/** A .NET TimeSpan binding like "00:10:00" as the grace period a screen can read. */
export function formatDuration(value: string): string {
  const [hours = "0", minutes = "0", seconds = "0"] = value.split(":");
  const parts: string[] = [];
  if (Number(hours)) parts.push(`${Number(hours)} h`);
  if (Number(minutes)) parts.push(`${Number(minutes)} min`);
  if (!parts.length && Number(seconds)) parts.push(`${Number(seconds)} s`);
  return parts.length ? parts.join(" ") : "none";
}
