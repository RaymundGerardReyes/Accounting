"use client"

import { BarChart3, TrendingUp, AlertCircle, Cog, CreditCard, Landmark } from "lucide-react"

interface SidebarProps {
  currentPage: string
  onNavigate: (page: any) => void
}

export function Sidebar({ currentPage, onNavigate }: SidebarProps) {
  const menuItems = [
    { id: "dashboard", label: "Dashboard", icon: BarChart3 },
    { id: "finance", label: "Finance Operations", icon: Landmark },
    { id: "payments", label: "Payments & Checkout", icon: CreditCard },
    { id: "product", label: "Product Analysis", icon: TrendingUp },
    { id: "variance", label: "Variance Analysis", icon: AlertCircle },
    { id: "config", label: "Configuration", icon: Cog },
  ]

  return (
    <aside className="w-64 bg-sidebar text-sidebar-foreground border-r border-sidebar-border flex flex-col justify-between shrink-0 select-none">
      <div>
        {/* Brand Header */}
        <div className="h-16 px-6 border-b border-sidebar-border flex items-center gap-3">
          <div className="w-7 h-7 rounded-md bg-primary text-primary-foreground flex items-center justify-center font-bold text-sm tracking-tighter shadow-xs">
            A
          </div>
          <div className="flex flex-col">
            <div className="flex items-center gap-1.5">
              <span className="text-sm font-bold tracking-tight text-foreground">Acme</span>
              <span className="w-1.5 h-1.5 rounded-full bg-accent" aria-hidden="true" />
            </div>
            <span className="text-[11px] text-muted-foreground leading-none">
              Cost & Payment Ledger
            </span>
          </div>
        </div>

        {/* Navigation Section */}
        <div className="px-3 py-5">
          <p className="px-3 mb-2 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground/70">
            Platform Workspace
          </p>
          <nav aria-label="Main Navigation" className="space-y-1">
            {menuItems.map((item) => {
              const Icon = item.icon
              const isActive = currentPage === item.id
              return (
                <button
                  key={item.id}
                  type="button"
                  aria-current={isActive ? "page" : undefined}
                  onClick={() => onNavigate(item.id)}
                  className={`group relative w-full flex items-center gap-3 px-3 py-2.5 rounded-md text-xs font-medium transition-all duration-150 focus:outline-none focus-visible:ring-2 focus-visible:ring-ring ${
                    isActive
                      ? "bg-primary text-primary-foreground shadow-xs"
                      : "text-muted-foreground hover:text-foreground hover:bg-muted/60"
                  }`}
                >
                  {/* Subtle Red Accent Bar on Active Item */}
                  {isActive && (
                    <span
                      className="absolute left-0 top-1.5 bottom-1.5 w-1 rounded-r-full bg-accent"
                      aria-hidden="true"
                    />
                  )}
                  <Icon
                    className={`w-4 h-4 shrink-0 transition-colors ${
                      isActive
                        ? "text-accent"
                        : "text-muted-foreground group-hover:text-foreground"
                    }`}
                    aria-hidden="true"
                  />
                  <span className="truncate">{item.label}</span>
                </button>
              )
            })}
          </nav>
        </div>
      </div>

      {/* Minimalist System Status Footer */}
      <div className="p-4 m-3 rounded-lg bg-muted/40 border border-border/60">
        <div className="flex items-center justify-between">
          <span className="text-[11px] font-medium text-foreground">PayMongo Engine</span>
          <span className="inline-flex items-center gap-1 text-[10px] font-mono text-accent font-semibold">
            <span className="w-1.5 h-1.5 rounded-full bg-accent animate-pulse" />
            LIVE
          </span>
        </div>
        <p className="text-[10px] text-muted-foreground mt-1 font-mono">
          © 2026 Acme Mfg · v1.0
        </p>
      </div>
    </aside>
  )
}

