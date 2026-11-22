import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { KotobeeAccessStatusCard } from "./payment/KotobeeAccessStatusCard";

describe("KotobeeAccessStatusCard — Regression Scenarios R37–R51", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  // R37–R39: Never renders or calls API when order is NOT paid
  it.each([false])("renders nothing and makes 0 fetches when isOrderPaid is %s", (isPaid: boolean) => {
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

  // R48: Stops polling after maxPollAttempts on empty [] and allows manual Check Again
  it("stops polling after maxPollAttempts on empty results and recovers on Check Again", async () => {
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce({ ok: true, json: async () => [] } as Response)
      .mockResolvedValueOnce({ ok: true, json: async () => [] } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [
          {
            productSku: "BOOK-B",
            kotobeeBookId: "502",
            status: "Active",
            bookAccessState: "full",
            activatedAtUtc: "2026-11-21T05:00:00Z",
            expiresAtUtc: null,
          },
        ],
      } as Response);

    render(
      <KotobeeAccessStatusCard
        orderId="ORD-EMPTY-TIMEOUT"
        isOrderPaid={true}
        pollIntervalMs={10}
        maxPollAttempts={2}
      />
    );

    // After 2 empty polls, stops polling and shows Check Again
    await waitFor(() => {
      expect(screen.getByText(/No ebook permissions are ready/i)).toBeTruthy();
      expect(screen.getByRole("button", { name: /Check Again/i })).toBeTruthy();
    });
    expect(fetchSpy).toHaveBeenCalledTimes(2);

    // Clicking Check Again restarts polling and picks up the newly Active entitlement
    fireEvent.click(screen.getByRole("button", { name: /Check Again/i }));

    await waitFor(() => {
      expect(screen.getByText("BOOK-B")).toBeTruthy();
      expect(screen.getByText("Access Active")).toBeTruthy();
    });
    expect(fetchSpy).toHaveBeenCalledTimes(3);
  });

  // R49: Revoked entitlements display "Access Revoked" (not "Activating…") and stop polling
  it("renders Access Revoked badge for Revoked entitlements and does not poll", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      json: async () => [
        {
          productSku: "BOOK-REV",
          kotobeeBookId: "503",
          status: "Revoked",
          bookAccessState: "none",
          activatedAtUtc: "2026-11-21T04:00:00Z",
          expiresAtUtc: null,
        },
      ],
    } as Response);

    render(<KotobeeAccessStatusCard orderId="ORD-REV-1" isOrderPaid={true} pollIntervalMs={20} />);

    await waitFor(() => {
      expect(screen.getByText("Access Revoked")).toBeTruthy();
    });

    expect(screen.queryByText(/Activating/i)).toBeNull();
    expect(fetchSpy).toHaveBeenCalledTimes(1);
  });

  // R50: Never fetches when orderId is empty even if isOrderPaid is true
  it("renders nothing and makes 0 fetches when orderId is empty", () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch");

    const { container } = render(
      <KotobeeAccessStatusCard orderId="" isOrderPaid={true} />
    );

    expect(container.firstChild).toBeNull();
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  // R51: Instantly hides and stops polling when header is "not_applicable"
  it("instantly hides and makes 0 further polls when header is not_applicable", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      headers: new Headers({ "X-Kotobee-Fulfillment-Status": "not_applicable" }),
      json: async () => [],
    } as Response);

    const { container } = render(
      <KotobeeAccessStatusCard
        orderId="ORD-NON-EBOOK"
        isOrderPaid={true}
        pollIntervalMs={15}
        maxPollAttempts={5}
      />
    );

    // Wait for first fetch to finish
    await waitFor(() => {
      expect(fetchSpy).toHaveBeenCalledTimes(1);
    });

    // Give enough time for a second poll to fire if the guard were broken
    await new Promise((resolve) => setTimeout(resolve, 60));

    expect(container.firstChild).toBeNull();
    expect(fetchSpy).toHaveBeenCalledTimes(1);
  });
});
