import React from "react"
import { render, screen } from "@testing-library/react"
import { describe, it, expect, vi, beforeEach } from "vitest"
import LoginPage from "@/app/login/page"

vi.mock("@/lib/auth-context", () => ({
  useAuth: vi.fn(),
}))

import { useAuth } from "@/lib/auth-context"

describe("LoginPage Component", () => {
  const mockLoginWithCredentials = vi.fn().mockResolvedValue({ ok: true })
  const mockLoginWithOAuth = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(useAuth).mockReturnValue({
      authenticated: false,
      loading: false,
      user: null,
      authenticatedUser: null,
      allUsers: [
        { id: "emp_field_01", full_name: "Juan Dela Cruz", email: "juan.delacruz@rge.com.ph", role: "field_staff", is_active: true },
        { id: "emp_mgr_01", full_name: "Roberto Lim", email: "roberto.lim@rge.com.ph", role: "operations_manager", is_active: true },
        { id: "emp_exec_01", full_name: "Victorino Bautista", email: "victorino.bautista@rge.com.ph", role: "executive", is_active: true },
        { id: "emp_admin_01", full_name: "System Administrator", email: "admin@rge.com.ph", role: "admin", is_active: true },
      ],
      csrfToken: "test-token",
      loginWithCredentials: mockLoginWithCredentials,
      loginWithOAuth: mockLoginWithOAuth,
      switchPersona: vi.fn(),
      logout: vi.fn(),
      refreshSession: vi.fn(),
    })
  })

  it("renders corporate login fields and clean description without persona references", () => {
    render(<LoginPage />)

    expect(screen.getByRole("heading", { name: /R\.G\.E Accounting Firm Inc\./i })).toBeDefined()
    expect(screen.getByText("Enter your corporate credentials to access your account")).toBeDefined()
    expect(screen.getByLabelText(/Identifier \/ Email \/ Employee Code/i)).toBeDefined()
    expect(screen.getByLabelText(/Password/i)).toBeDefined()
    expect(screen.getByRole("button", { name: /Authenticate/i })).toBeDefined()
  })

  it("does NOT render Quick Persona Fill card footer or any seeded persona buttons", () => {
    render(<LoginPage />)

    expect(screen.queryByText(/Quick Persona Fill/i)).toBeNull()
    expect(screen.queryByText(/Loading personas/i)).toBeNull()
    expect(screen.queryByText(/No personas available/i)).toBeNull()

    const seededPersonas = [
      "Juan Dela Cruz",
      "Roberto Lim",
      "Victorino Bautista",
      "Patricia Tan",
      "Carlo Reyes",
      "Angela Mendoza",
      "Elena Gomez",
      "Maria Santos",
      "System Administrator",
    ]

    for (const name of seededPersonas) {
      expect(screen.queryByText(name)).toBeNull()
    }
  })
})
