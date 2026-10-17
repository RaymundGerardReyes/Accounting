"use client"

import dynamic from "next/dynamic"
import { Dashboard } from "@/components/dashboard"
import { Sidebar } from "@/components/sidebar"
import { useState } from "react"

const PaymentsHub = dynamic(() => import("@/components/payments-hub").then((m) => m.PaymentsHub), {
  loading: () => <div className="p-8 text-xs font-mono text-muted-foreground">Loading payments ledger...</div>,
  ssr: false,
})
const DepartmentDetail = dynamic(() => import("@/components/department-detail").then((m) => m.DepartmentDetail), {
  loading: () => <div className="p-8 text-xs font-mono text-muted-foreground">Loading department details...</div>,
  ssr: false,
})
const ProductLineAnalysis = dynamic(() => import("@/components/product-line-analysis").then((m) => m.ProductLineAnalysis), {
  loading: () => <div className="p-8 text-xs font-mono text-muted-foreground">Loading product analysis...</div>,
  ssr: false,
})
const VarianceAnalysis = dynamic(() => import("@/components/variance-analysis").then((m) => m.VarianceAnalysis), {
  loading: () => <div className="p-8 text-xs font-mono text-muted-foreground">Loading variance analysis...</div>,
  ssr: false,
})
const Configuration = dynamic(() => import("@/components/configuration").then((m) => m.Configuration), {
  loading: () => <div className="p-8 text-xs font-mono text-muted-foreground">Loading configuration...</div>,
  ssr: false,
})
const FinanceOperationsHub = dynamic(() => import("@/components/finance-operations-hub").then((m) => m.FinanceOperationsHub), {
  loading: () => <div className="p-8 text-xs font-mono text-muted-foreground">Loading finance operations hub...</div>,
  ssr: false,
})

type Page = "dashboard" | "department" | "product" | "variance" | "config" | "payments" | "finance"

export default function Home() {
  const [currentPage, setCurrentPage] = useState<Page>("dashboard")
  const [selectedDepartment, setSelectedDepartment] = useState<string>("manufacturing")

  const renderPage = () => {
    switch (currentPage) {
      case "dashboard":
        return (
          <Dashboard
            onSelectDepartment={(dept) => {
              setSelectedDepartment(dept)
              setCurrentPage("department")
            }}
          />
        )
      case "finance":
        return <FinanceOperationsHub />
      case "payments":
        return <PaymentsHub />
      case "department":
        return <DepartmentDetail department={selectedDepartment} onBack={() => setCurrentPage("dashboard")} />
      case "product":
        return <ProductLineAnalysis onBack={() => setCurrentPage("dashboard")} />
      case "variance":
        return <VarianceAnalysis onBack={() => setCurrentPage("dashboard")} />
      case "config":
        return <Configuration onBack={() => setCurrentPage("dashboard")} />
      default:
        return (
          <Dashboard
            onSelectDepartment={(dept) => {
              setSelectedDepartment(dept)
              setCurrentPage("department")
            }}
          />
        )
    }
  }

  return (
    <div className="flex h-screen bg-background">
      <Sidebar currentPage={currentPage} onNavigate={setCurrentPage} />
      <main className="flex-1 overflow-auto">{renderPage()}</main>
    </div>
  )
}
