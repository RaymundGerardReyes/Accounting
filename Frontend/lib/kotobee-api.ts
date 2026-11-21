export interface KotobeeEntitlementDto {
  productSku: string;
  kotobeeBookId: string;
  status: "Pending" | "Active" | "Failed" | "Revoked";
  bookAccessState: "full" | "partial" | "none" | null;
  activatedAtUtc: string | null;
  expiresAtUtc: string | null;
  lastError?: string | null;
  attemptCount?: number;
}

export async function fetchOrderKotobeeEntitlements(
  orderId: string,
  signal?: AbortSignal
): Promise<KotobeeEntitlementDto[]> {
  const res = await fetch(
    `/api/integrations/kotobee/orders/${encodeURIComponent(orderId)}/entitlements`,
    { credentials: "include", signal }
  );
  if (!res.ok) {
    throw new Error(`Failed to load ebook access status (${res.status})`);
  }
  return res.json();
}
