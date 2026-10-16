"use client"

import { Configuration } from "@/components/configuration"
import { Dashboard } from "@/components/dashboard"
import { DepartmentDetail } from "@/components/department-detail"
import { ProductLineAnalysis } from "@/components/product-line-analysis"
import { Sidebar } from "@/components/sidebar"
import { VarianceAnalysis } from "@/components/variance-analysis"
import { PaymentsHub } from "@/components/payments-hub"
import { useState } from "react"

type Page = "dashboard" | "department" | "product" | "variance" | "config" | "payments"

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
