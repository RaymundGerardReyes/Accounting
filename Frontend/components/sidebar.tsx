"use client"

import { BarChart3, TrendingUp, AlertCircle, Cog, CreditCard } from "lucide-react"

interface SidebarProps {
  currentPage: string
  onNavigate: (page: any) => void
}

export function Sidebar({ currentPage, onNavigate }: SidebarProps) {
  const menuItems = [
    { id: "dashboard", label: "Dashboard", icon: BarChart3 },
    { id: "payments", label: "Payments & Checkout", icon: CreditCard },
    { id: "product", label: "Product Analysis", icon: TrendingUp },
    { id: "variance", label: "Variance Analysis", icon: AlertCircle },
    { id: "config", label: "Configuration", icon: Cog },
  ]

  return (
    <aside className="w-64 bg-sidebar text-sidebar-foreground border-r border-sidebar-border flex flex-col">
      {/* Header */}
      <div className="p-6 border-b border-sidebar-border">
        <h1 className="text-2xl font-bold tracking-tight">Acme</h1>
        <p className="text-xs text-sidebar-foreground/60 mt-1">Cost Analysis & Payments</p>
      </div>

      {/* Navigation */}
      <nav aria-label="Main Navigation" className="flex-1 p-4 space-y-2">
        {menuItems.map((item) => {
          const Icon = item.icon
          const isActive = currentPage === item.id
          return (
            <button
              key={item.id}
              type="button"
              aria-current={isActive ? "page" : undefined}
              onClick={() => onNavigate(item.id)}
              className={`w-full flex items-center gap-3 px-4 py-3 rounded-md transition-colors focus:outline-none focus:ring-2 focus:ring-sidebar-ring ${
                isActive
                  ? "bg-sidebar-accent text-sidebar-accent-foreground font-semibold"
                  : "text-sidebar-foreground hover:bg-sidebar-accent/10"
              }`}
            >
              <Icon className="w-5 h-5" aria-hidden="true" />
              <span className="text-sm font-medium">{item.label}</span>
            </button>
          )
        })}
      </nav>

      {/* Footer */}
      <div className="p-4 border-t border-sidebar-border">
        <p className="text-xs text-sidebar-foreground/50">© 2025 Acme Mfg • PayMongo Live</p>
      </div>
    </aside>
  )
}
