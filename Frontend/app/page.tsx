"use client"

import * as React from "react"
import dynamic from "next/dynamic"
import {
  Sidebar,
  APP_NAVIGATION_ITEMS,
  type AppModuleId,
  normalizeModuleId,
} from "@/components/sidebar"
import { Dashboard } from "@/components/dashboard"
import { Button } from "@/components/ui/button"
import { Menu, X, ChevronRight, ShieldCheck } from "lucide-react"

// Dynamic imports with graceful loading fallbacks for code-split modules
const PaymentsHub = dynamic(
  () => import("@/components/payments-hub").then((m) => m.PaymentsHub),
  {
    loading: () => (
      <div className="p-8 text-xs font-mono text-muted-foreground">
        Loading payments ledger...
      </div>
    ),
    ssr: false,
  }
)

const DepartmentDetail = dynamic(
  () => import("@/components/department-detail").then((m) => m.DepartmentDetail),
  {
    loading: () => (
      <div className="p-8 text-xs font-mono text-muted-foreground">
        Loading department details...
      </div>
    ),
    ssr: false,
  }
)

const ProductLineAnalysis = dynamic(
  () => import("@/components/product-line-analysis").then((m) => m.ProductLineAnalysis),
  {
    loading: () => (
      <div className="p-8 text-xs font-mono text-muted-foreground">
        Loading product analysis...
      </div>
    ),
    ssr: false,
  }
)

const VarianceAnalysis = dynamic(
  () => import("@/components/variance-analysis").then((m) => m.VarianceAnalysis),
  {
    loading: () => (
      <div className="p-8 text-xs font-mono text-muted-foreground">
        Loading variance analysis...
      </div>
    ),
    ssr: false,
  }
)

const Configuration = dynamic(
  () => import("@/components/configuration").then((m) => m.Configuration),
  {
    loading: () => (
      <div className="p-8 text-xs font-mono text-muted-foreground">
        Loading configuration...
      </div>
    ),
    ssr: false,
  }
)

const FinanceOperationsHub = dynamic(
  () => import("@/components/finance-operations-hub").then((m) => m.FinanceOperationsHub),
  {
    loading: () => (
      <div className="p-8 text-xs font-mono text-muted-foreground">
        Loading finance operations hub...
      </div>
    ),
    ssr: false,
  }
)

export type PageId =
  | AppModuleId
  | "department"
  | "product"
  | "config"
  | "finance"

export default function Home() {
  const [activeTab, setActiveTab] = React.useState<PageId>("dashboard")
  const [selectedDepartment, setSelectedDepartment] = React.useState<string>("manufacturing")
  const [sidebarCollapsed, setSidebarCollapsed] = React.useState(false)
  const [mobileMenuOpen, setMobileMenuOpen] = React.useState(false)

  // Close mobile drawer on Escape key press
  React.useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape" && mobileMenuOpen) {
        setMobileMenuOpen(false)
      }
    }
    window.addEventListener("keydown", handleKeyDown)
    return () => window.removeEventListener("keydown", handleKeyDown)
  }, [mobileMenuOpen])

  // Prevent background scrolling when mobile navigation modal is open
  React.useEffect(() => {
    if (mobileMenuOpen) {
      document.body.style.overflow = "hidden"
    } else {
      document.body.style.overflow = ""
    }
    return () => {
      document.body.style.overflow = ""
    }
  }, [mobileMenuOpen])

  // Map any legacy or modern module ID to metadata for breadcrumb display
  const normalizedId = normalizeModuleId(activeTab)
  const activeNavMeta = React.useMemo(
    () =>
      APP_NAVIGATION_ITEMS.find((item) => item.id === normalizedId) ??
      APP_NAVIGATION_ITEMS[0],
    [normalizedId]
  )

  const handleModuleChange = (nextTab: AppModuleId | string) => {
    setActiveTab(nextTab as PageId)
    setMobileMenuOpen(false)
  }

  const renderModuleContent = () => {
    switch (activeTab) {
      case "dashboard":
        return (
          <Dashboard
            onSelectDepartment={(dept) => {
              setSelectedDepartment(dept)
              setActiveTab("departments")
            }}
          />
        )
      case "finance":
      case "finance-ops":
        return <FinanceOperationsHub />
      case "payments":
        return <PaymentsHub />
      case "department":
      case "departments":
        return (
          <DepartmentDetail
            department={selectedDepartment}
            onBack={() => setActiveTab("dashboard")}
          />
        )
      case "product":
      case "products":
        return <ProductLineAnalysis onBack={() => setActiveTab("dashboard")} />
      case "variance":
        return <VarianceAnalysis onBack={() => setActiveTab("dashboard")} />
      case "config":
      case "configuration":
        return <Configuration onBack={() => setActiveTab("dashboard")} />
      default:
        return (
          <Dashboard
            onSelectDepartment={(dept) => {
              setSelectedDepartment(dept)
              setActiveTab("departments")
            }}
          />
        )
    }
  }

  return (
    <div className="flex h-screen w-full overflow-hidden bg-background text-foreground">
      {/* Skip to main content link for keyboard accessibility */}
      <a
        href="#main-financial-workspace"
        className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:px-4 focus:py-2 focus:bg-primary focus:text-primary-foreground focus:rounded-md focus:shadow-md"
      >
        Skip to main financial workspace
      </a>

      {/* Desktop Persistent Sidebar */}
      <div className="hidden md:flex h-full">
        <Sidebar
          activeTab={activeTab}
          setActiveTab={handleModuleChange}
          collapsed={sidebarCollapsed}
          onCollapsedChange={setSidebarCollapsed}
        />
      </div>

      {/* Mobile Navigation Overlay Drawer */}
      {mobileMenuOpen && (
        <div
          className="fixed inset-0 z-50 flex md:hidden"
          role="dialog"
          aria-modal="true"
          aria-label="Mobile Navigation"
        >
          <div
            className="fixed inset-0 bg-foreground/40 backdrop-blur-xs transition-opacity"
            onClick={() => setMobileMenuOpen(false)}
            aria-hidden="true"
          />
          <div className="relative z-10 flex w-64 flex-col bg-sidebar h-full shadow-xl">
            <div className="absolute top-3 right-3 z-20">
              <Button
                variant="ghost"
                size="icon"
                onClick={() => setMobileMenuOpen(false)}
                aria-label="Close navigation menu"
              >
                <X className="h-4 w-4" />
              </Button>
            </div>
            <Sidebar
              activeTab={activeTab}
              setActiveTab={handleModuleChange}
              collapsed={false}
            />
          </div>
        </div>
      )}

      {/* Main Content Column */}
      <div className="flex flex-1 flex-col min-w-0 overflow-hidden">
        {/* Top Enterprise Context Bar */}
        <header className="h-14 border-b border-border bg-card px-4 sm:px-6 flex items-center justify-between gap-4 shrink-0 shadow-2xs">
          <div className="flex items-center gap-3 min-w-0">
            <Button
              variant="outline"
              size="icon"
              className="md:hidden h-8 w-8 shrink-0"
              onClick={() => setMobileMenuOpen(true)}
              aria-label="Open navigation menu"
            >
              <Menu className="h-4 w-4" />
            </Button>

            {/* Breadcrumb Context */}
            <nav aria-label="Breadcrumb" className="flex items-center gap-1.5 text-sm truncate">
              <span className="text-muted-foreground hidden sm:inline">
                {activeNavMeta.group}
              </span>
              <ChevronRight
                className="h-3.5 w-3.5 text-muted-foreground hidden sm:inline shrink-0"
                aria-hidden="true"
              />
              <span className="font-semibold text-foreground truncate">
                {activeTab === "departments" || activeTab === "department"
                  ? `${activeNavMeta.label} (${
                      selectedDepartment.toUpperCase() === "IT" || selectedDepartment.toUpperCase() === "HR"
                        ? selectedDepartment.toUpperCase()
                        : selectedDepartment.charAt(0).toUpperCase() + selectedDepartment.slice(1)
                    })`
                  : activeNavMeta.label}
              </span>
            </nav>
          </div>

          {/* Right Global Controls: Audit Mode & Status */}
          <div className="flex items-center gap-3 shrink-0">
            <div className="hidden sm:flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-muted text-xs font-mono text-foreground border border-border">
              <ShieldCheck className="h-3.5 w-3.5 text-secondary" aria-hidden="true" />
              <span>CPA Audit Trail: Enforced</span>
            </div>
          </div>
        </header>

        {/* Scrollable Module Canvas */}
        <main
          id="main-financial-workspace"
          className="flex-1 overflow-y-auto focus:outline-none"
          tabIndex={-1}
        >
          {renderModuleContent()}
        </main>
      </div>
    </div>
  )
}
