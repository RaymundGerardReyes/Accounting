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

/**
 * Non-breaking response header sent by KotobeeEndpoints.cs:
 * - "not_applicable": Order contains no Kotobee ebook SKUs -> hide card & stop polling
 * - "pending":        Order has Kotobee ebook SKUs still being provisioned -> keep polling
 * - "ready":          All entitlement rows are in a terminal state -> stop polling
 */
export type KotobeeFulfillmentHeaderStatus =
  | "not_applicable"
  | "pending"
  | "ready";

export const KOTOBEE_FULFILLMENT_HEADER = "X-Kotobee-Fulfillment-Status";

export interface KotobeeOrderEntitlementsResult {
  entitlements: KotobeeEntitlementDto[];
  fulfillmentStatus: KotobeeFulfillmentHeaderStatus | null;
}

function parseFulfillmentHeader(
  rawValue: string | null | undefined
): KotobeeFulfillmentHeaderStatus | null {
  if (!rawValue) return null;
  const normalized = rawValue.trim().toLowerCase();
  if (
    normalized === "not_applicable" ||
    normalized === "pending" ||
    normalized === "ready"
  ) {
    return normalized;
  }
  return null;
}

/**
 * Header-aware fetcher used by KotobeeAccessStatusCard.
 * Safely falls back to fulfillmentStatus = null if the header is absent.
 */
export async function fetchOrderKotobeeEntitlementsWithStatus(
  orderId: string,
  signal?: AbortSignal
): Promise<KotobeeOrderEntitlementsResult> {
  const res = await fetch(
    `/api/integrations/kotobee/orders/${encodeURIComponent(orderId)}/entitlements`,
    { credentials: "include", signal }
  );
  if (!res.ok) {
    throw new Error(`Failed to load ebook access status (${res.status})`);
  }

  // Optional chaining preserves compatibility with minimal test Response mocks
  const rawHeader = res.headers?.get?.(KOTOBEE_FULFILLMENT_HEADER);
  const entitlements: KotobeeEntitlementDto[] = await res.json();

  return {
    entitlements,
    fulfillmentStatus: parseFulfillmentHeader(rawHeader),
  };
}

/**
 * Existing array-only helper preserved so all other callers keep working unchanged.
 */
export async function fetchOrderKotobeeEntitlements(
  orderId: string,
  signal?: AbortSignal
): Promise<KotobeeEntitlementDto[]> {
  const result = await fetchOrderKotobeeEntitlementsWithStatus(orderId, signal);
  return result.entitlements;
}

export interface KotobeeSheetSchemaDto {
  source: string;
  snapshotId?: string;
  spreadsheetId?: string;
  sheetRange?: string;
  contentSha256?: string;
  activeRowCount: number;
  syncedAtUtc?: string;
  mappings: Record<
    string,
    Array<{
      bookId: string;
      libraryId?: string | null;
      accessDurationDays?: number | null;
    }>
  >;
}

export async function fetchKotobeeSheetSchema(
  signal?: AbortSignal
): Promise<KotobeeSheetSchemaDto> {
  const res = await fetch("/api/integrations/kotobee/sheet/schema", {
    credentials: "include",
    signal,
  });
  if (!res.ok) {
    throw new Error(`Failed to load sheet schema (${res.status})`);
  }
  return res.json();
}
