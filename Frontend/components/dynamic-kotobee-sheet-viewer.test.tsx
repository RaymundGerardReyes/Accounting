import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DynamicKotobeeSheetViewer } from "./payment/DynamicKotobeeSheetViewer";

describe("DynamicKotobeeSheetViewer", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("renders loaded sheet metadata and mapping table successfully", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      json: async () => ({
        source: "PostgreSQL LKG Snapshot",
        snapshotId: "00000000-0000-0000-0000-000000000001",
        spreadsheetId: "1abc-sheet-id-xyz",
        sheetRange: "Books!A1:Z50",
        contentSha256: "abcdef1234567890abcdef1234567890",
        activeRowCount: 2,
        syncedAtUtc: "2026-11-21T10:00:00Z",
        mappings: {
          "SKU-CPA-001": [
            { bookId: "101", libraryId: "SHELF-1", accessDurationDays: 365 },
          ],
          "SKU-BUNDLE-002": [
            { bookId: "102", libraryId: null, accessDurationDays: null },
          ],
        },
      }),
    } as Response);

    render(<DynamicKotobeeSheetViewer />);

    await waitFor(() => {
      expect(screen.getByText("Kotobee Dynamic Sheet Catalog")).toBeTruthy();
      expect(screen.getByText(/PostgreSQL LKG Snapshot/i)).toBeTruthy();
      expect(screen.getByText("SKU-CPA-001")).toBeTruthy();
      expect(screen.getByText("101")).toBeTruthy();
      expect(screen.getByText("365 days")).toBeTruthy();
      expect(screen.getByText("SKU-BUNDLE-002")).toBeTruthy();
      expect(screen.getByText("Lifetime")).toBeTruthy();
    });
  });

  it("renders error state when fetch fails and allows refresh retry", async () => {
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce({
        ok: false,
        status: 500,
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          source: "appsettings.json (static fallback)",
          activeRowCount: 1,
          mappings: {
            "SKU-FALLBACK": [{ bookId: "999", libraryId: null, accessDurationDays: null }],
          },
        }),
      } as Response);

    render(<DynamicKotobeeSheetViewer />);

    await waitFor(() => {
      expect(screen.getByRole("alert")).toBeTruthy();
      expect(screen.getByText(/Failed to load sheet schema \(500\)/i)).toBeTruthy();
    });

    const user = userEvent.setup();
    const refreshBtn = screen.getByRole("button", { name: /Refresh/i });
    await user.click(refreshBtn);

    await waitFor(() => {
      expect(screen.getByText(/appsettings.json/i)).toBeTruthy();
      expect(screen.getByText("SKU-FALLBACK")).toBeTruthy();
    });

    expect(fetchSpy).toHaveBeenCalledTimes(2);
  });
});
