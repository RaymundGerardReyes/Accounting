import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { KotobeeAccessStatusCard } from "./payment/KotobeeAccessStatusCard";

describe("KotobeeAccessStatusCard — Regression Scenarios R37–R47", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  // R37–R39: Never renders or calls API when order is NOT paid
  it.each([false])("renders nothing and makes 0 fetches when isOrderPaid is %s", (isPaid) => {
    const fetchSpy = vi.spyOn(globalThis, "fetch");

    const { container } = render(
      <KotobeeAccessStatusCard orderId="ORD-UNPAID" isOrderPaid={isPaid} />
    );

    expect(container.firstChild).toBeNull();
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  // R40 & R46: Fetches once paid and displays Active + Lifetime access
  it("fetches entitlements once paid and stops polling when all books are Active", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      json: async () => [
        {
          productSku: "BOOK-A",
          kotobeeBookId: "501",
          status: "Active",
          bookAccessState: "full",
          activatedAtUtc: "2026-11-21T04:00:00Z",
          expiresAtUtc: null,
        },
      ],
    } as Response);

    render(<KotobeeAccessStatusCard orderId="ORD-PAID-1" isOrderPaid={true} pollIntervalMs={50} />);

    await waitFor(() => {
      expect(screen.getByText("BOOK-A")).toBeTruthy();
      expect(screen.getByText("Access Active")).toBeTruthy();
      expect(screen.getByText(/Lifetime access/i)).toBeTruthy();
    });

    expect(fetchSpy).toHaveBeenCalledTimes(1);
  });

  // R41: Continues polling while Pending, stops when Active
  it("polls while Pending and stops once status becomes Active", async () => {
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [
          {
            productSku: "BOOK-A",
            kotobeeBookId: "501",
            status: "Pending",
            bookAccessState: null,
            activatedAtUtc: null,
            expiresAtUtc: null,
          },
        ],
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [
          {
            productSku: "BOOK-A",
            kotobeeBookId: "501",
            status: "Active",
            bookAccessState: "full",
            activatedAtUtc: "2026-11-21T04:00:02Z",
            expiresAtUtc: null,
          },
        ],
      } as Response);

    render(<KotobeeAccessStatusCard orderId="ORD-POLL-1" isOrderPaid={true} pollIntervalMs={20} />);

    await waitFor(() => {
      expect(screen.getByText("Access Active")).toBeTruthy();
    });

    expect(fetchSpy).toHaveBeenCalledTimes(2);
  });

  // R44: Graceful error handling without crashing Payment Result page
  it("displays a friendly message if the entitlements endpoint returns 500", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: false,
      status: 500,
    } as Response);

    render(<KotobeeAccessStatusCard orderId="ORD-ERR-1" isOrderPaid={true} />);

    await waitFor(() => {
      expect(screen.getByText(/Unable to check ebook activation status/i)).toBeTruthy();
    });
  });
});
