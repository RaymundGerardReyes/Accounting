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

export interface KotobeeSheetSchemaDto {
  source: string;
  snapshotId?: string;
  spreadsheetId?: string;
  sheetRange?: string;
  contentSha256?: string;
  activeRowCount: number;
  syncedAtUtc?: string;
  mappings: Record<string, Array<{
    bookId: string;
    libraryId?: string | null;
    accessDurationDays?: number | null;
  }>>;
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
