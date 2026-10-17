"use client"

import * as React from "react"
import { cn } from "@/lib/utils"
import { Button } from "@/components/ui/button"
import {
  LayoutDashboard,
  Building2,
  Package,
  Scale,
  ShieldCheck,
  CreditCard,
  SlidersHorizontal,
  ChevronLeft,
  ChevronRight,
} from "lucide-react"

export type AppModuleId =
  | "dashboard"
  | "departments"
  | "products"
  | "variance"
  | "finance-ops"
  | "payments"
  | "configuration"

export interface SidebarProps {
  /** Supports activeTab, activeView, and legacy currentPage to ensure zero breaking changes */
  activeTab?: string
  setActiveTab?: (tab: AppModuleId | string) => void
  activeView?: string
  onViewChange?: (view: AppModuleId | string) => void
  currentPage?: string
  onNavigate?: (page: AppModuleId | string) => void
  collapsed?: boolean
  onCollapsedChange?: (collapsed: boolean) => void
  className?: string
}

export interface NavItem {
  id: AppModuleId
  label: string
  shortLabel: string
  icon: React.ElementType
  group: "Overview" | "Operations & Treasury" | "Cost & Ledger" | "System"
  badge?: string
}

export const APP_NAVIGATION_ITEMS: NavItem[] = [
  {
    id: "dashboard",
    label: "Executive Dashboard",
    shortLabel: "Dashboard",
    icon: LayoutDashboard,
    group: "Overview",
  },
  {
    id: "finance-ops",
    label: "Finance Operations",
    shortLabel: "Finance Ops",
    icon: ShieldCheck,
    group: "Operations & Treasury",
    badge: "CPA",
  },
  {
    id: "payments",
    label: "Payments & Treasury",
    shortLabel: "Payments",
    icon: CreditCard,
    group: "Operations & Treasury",
  },
  {
    id: "departments",
    label: "Department Ledger",
    shortLabel: "Departments",
    icon: Building2,
    group: "Cost & Ledger",
  },
  {
    id: "products",
    label: "Product Costing",
    shortLabel: "Products",
    icon: Package,
    group: "Cost & Ledger",
  },
  {
    id: "variance",
    label: "Variance Analysis",
    shortLabel: "Variance",
    icon: Scale,
    group: "Cost & Ledger",
  },
  {
    id: "configuration",
    label: "Allocation Rules",
    shortLabel: "Settings",
    icon: SlidersHorizontal,
    group: "System",
  },
]

export function normalizeModuleId(rawId?: string): AppModuleId {
  if (!rawId) return "dashboard"
  const normalized = rawId.toLowerCase().trim()
  if (normalized === "finance" || normalized === "finance-ops") return "finance-ops"
  if (normalized === "department" || normalized === "departments") return "departments"
  if (normalized === "product" || normalized === "products") return "products"
  if (normalized === "variance") return "variance"
  if (normalized === "config" || normalized === "configuration") return "configuration"
  if (normalized === "payments") return "payments"
  return (normalized as AppModuleId) || "dashboard"
}

export function Sidebar({
  activeTab,
  setActiveTab,
  activeView,
  onViewChange,
  currentPage,
  onNavigate,
  collapsed: controlledCollapsed,
  onCollapsedChange,
  className,
}: SidebarProps) {
  const [internalCollapsed, setInternalCollapsed] = React.useState(false)
  const isCollapsed = controlledCollapsed ?? internalCollapsed

  const rawModule = activeTab ?? activeView ?? currentPage ?? "dashboard"
  const currentModule = normalizeModuleId(rawModule)

  const handleSelect = (id: AppModuleId) => {
    setActiveTab?.(id)
    onViewChange?.(id)
    onNavigate?.(id)
  }

  const toggleCollapse = () => {
    const next = !isCollapsed
    setInternalCollapsed(next)
    onCollapsedChange?.(next)
  }

  const groups = ["Overview", "Operations & Treasury", "Cost & Ledger", "System"] as const

  return (
    <aside
      aria-label="Primary Financial Modules"
      className={cn(
        "flex flex-col h-full bg-sidebar text-sidebar-foreground border-r border-sidebar-border select-none transition-all duration-200 ease-in-out shrink-0",
        isCollapsed ? "w-16" : "w-64",
        className
      )}
    >
      {/* Brand Identity Header */}
      <div
        className={cn(
          "h-14 flex items-center border-b border-sidebar-border shrink-0",
          isCollapsed ? "justify-center px-2" : "justify-between px-3.5"
        )}
      >
        {!isCollapsed ? (
          <>
            <div className="flex items-center gap-2.5 overflow-hidden">
              <div className="h-8 w-8 rounded-md bg-primary text-primary-foreground flex items-center justify-center font-mono font-bold text-sm shrink-0 shadow-xs">
                FA
              </div>
              <div className="flex flex-col truncate">
                <span className="font-semibold text-sm leading-none tracking-tight text-sidebar-foreground">
                  FinOps<span className="text-secondary font-bold">ERP</span>
                </span>
                <span className="text-[11px] text-muted-foreground mt-0.5 font-mono">
                  CPA-Audited Suite
                </span>
              </div>
            </div>

            <Button
              variant="ghost"
              size="icon"
              onClick={toggleCollapse}
              className="h-7 w-7 text-muted-foreground hover:text-foreground hidden md:inline-flex shrink-0"
              aria-label="Collapse sidebar"
              title="Collapse sidebar"
            >
              <ChevronLeft className="h-4 w-4" />
            </Button>
          </>
        ) : (
          <Button
            variant="ghost"
            size="icon"
            onClick={toggleCollapse}
            className="h-8 w-8 rounded-md bg-primary text-primary-foreground hover:bg-primary/90 hover:text-primary-foreground flex items-center justify-center font-mono font-bold text-xs shrink-0 shadow-xs group"
            aria-label="Expand sidebar"
            title="Expand sidebar"
          >
            <span className="group-hover:hidden">FA</span>
            <ChevronRight className="h-4 w-4 hidden group-hover:inline-block" />
          </Button>
        )}
      </div>

      {/* Grouped Module Navigation */}
      <nav className="flex-1 overflow-y-auto py-4 px-2.5 space-y-6">
        {groups.map((groupName) => {
          const items = APP_NAVIGATION_ITEMS.filter((item) => item.group === groupName)
          if (items.length === 0) return null

          return (
            <div key={groupName} className="space-y-1">
              {!isCollapsed && (
                <div className="px-2.5 pb-1 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
                  {groupName}
                </div>
              )}
              {items.map((item) => {
                const Icon = item.icon
                const isActive = currentModule === item.id

                return (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => handleSelect(item.id)}
                    aria-current={isActive ? "page" : undefined}
                    title={isCollapsed ? item.label : undefined}
                    className={cn(
                      "w-full flex items-center gap-3 rounded-md px-2.5 py-2 text-sm font-medium transition-colors focus-visible:outline-2 focus-visible:outline-ring",
                      isActive
                        ? "bg-primary text-primary-foreground shadow-xs"
                        : "text-muted-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground",
                      isCollapsed && "justify-center px-0"
                    )}
                  >
                    <Icon
                      className={cn(
                        "h-4 w-4 shrink-0",
                        isActive ? "text-secondary" : "text-current"
                      )}
                      aria-hidden="true"
                    />
                    {!isCollapsed && (
                      <>
                        <span className="truncate flex-1 text-left">{item.label}</span>
                        {item.badge && (
                          <span
                            className={cn(
                              "px-1.5 py-0.5 text-[10px] font-mono font-semibold rounded",
                              isActive
                                ? "bg-secondary text-secondary-foreground"
                                : "bg-muted text-foreground"
                            )}
                          >
                            {item.badge}
                          </span>
                        )}
                      </>
                    )}
                  </button>
                )
              })}
            </div>
          )
        })}
      </nav>

      {/* Footer System Status Indicator */}
      <div className="p-3 border-t border-sidebar-border bg-muted/30">
        <div
          className={cn(
            "flex items-center gap-2.5",
            isCollapsed && "justify-center"
          )}
          title={isCollapsed ? "Ledger Controls Active (Precision: Minor Units)" : undefined}
        >
          <span
            className="h-2 w-2 rounded-full bg-primary shrink-0"
            aria-hidden="true"
          />
          {!isCollapsed && (
            <div className="truncate text-xs">
              <p className="font-medium text-foreground leading-none">
                Ledger Controls Active
              </p>
              <p className="text-[11px] text-muted-foreground mt-1 font-mono">
                Precision: Minor Units
              </p>
            </div>
          )}
        </div>
      </div>
    </aside>
  )
}

export default Sidebar
