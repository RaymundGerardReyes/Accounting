"use client";

import { useEffect, useState } from "react";
import { fetchKotobeeSheetSchema, type KotobeeSheetSchemaDto } from "@/lib/kotobee-api";

export function DynamicKotobeeSheetViewer() {
  const [data, setData] = useState<KotobeeSheetSchemaDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function loadSchema() {
    try {
      setLoading(true);
      setError(null);
      const res = await fetchKotobeeSheetSchema();
      setData(res);
    } catch (err) {
      setError((err as Error).message || "Failed to load dynamic sheet schema.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    loadSchema();
  }, []);

  return (
    <div className="rounded-lg border bg-card p-5 text-card-foreground shadow-xs">
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-base font-semibold">Kotobee Dynamic Sheet Catalog</h3>
          <p className="text-xs text-muted-foreground">
            Source: {data?.source ?? "Loading…"}
          </p>
        </div>
        <button
          onClick={loadSchema}
          disabled={loading}
          className="rounded-md border px-3 py-1 text-xs font-medium hover:bg-muted disabled:opacity-50"
        >
          {loading ? "Refreshing…" : "Refresh"}
        </button>
      </div>

      {error && (
        <p className="mt-3 text-sm text-destructive" role="alert">
          {error}
        </p>
      )}

      {loading && !data && (
        <p className="mt-3 text-sm text-muted-foreground">
          Loading catalog configuration…
        </p>
      )}

      {data && (
        <div className="mt-4 space-y-4">
          <div className="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
            <div className="rounded border bg-muted/30 p-2">
              <span className="text-muted-foreground">Active Books</span>
              <p className="font-semibold text-sm">{data.activeRowCount}</p>
            </div>
            <div className="rounded border bg-muted/30 p-2">
              <span className="text-muted-foreground">Spreadsheet</span>
              <p className="font-semibold text-sm truncate" title={data.spreadsheetId ?? "Static"}>
                {data.spreadsheetId ?? "Local File"}
              </p>
            </div>
            <div className="rounded border bg-muted/30 p-2">
              <span className="text-muted-foreground">Content SHA-256</span>
              <p className="font-mono text-xs truncate" title={data.contentSha256 ?? "N/A"}>
                {data.contentSha256 ? `${data.contentSha256.substring(0, 10)}…` : "N/A"}
              </p>
            </div>
            <div className="rounded border bg-muted/30 p-2">
              <span className="text-muted-foreground">Last Synced</span>
              <p className="text-xs">
                {data.syncedAtUtc ? new Date(data.syncedAtUtc).toLocaleDateString() : "Built-in"}
              </p>
            </div>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="border-b bg-muted/50 text-muted-foreground">
                <tr>
                  <th className="py-2 px-2">SKU</th>
                  <th className="py-2 px-2">Kotobee Book ID</th>
                  <th className="py-2 px-2">Library ID</th>
                  <th className="py-2 px-2">Access Duration</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {Object.entries(data.mappings || {}).flatMap(([sku, books]) =>
                  books.map((b, idx) => (
                    <tr key={`${sku}-${b.bookId}-${idx}`} className="hover:bg-muted/20">
                      <td className="py-2 px-2 font-medium">{sku}</td>
                      <td className="py-2 px-2 font-mono">{b.bookId}</td>
                      <td className="py-2 px-2 text-muted-foreground">{b.libraryId ?? "Default"}</td>
                      <td className="py-2 px-2">
                        {b.accessDurationDays ? `${b.accessDurationDays} days` : "Lifetime"}
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
