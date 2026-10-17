"use client"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { AlertCircle, ArrowLeft, Download, TrendingDown, TrendingUp } from "lucide-react"
import { useEffect, useState } from "react"
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts"

interface VarianceAnalysisProps {
  onBack: () => void
}

interface VarianceData {
  varianceData: any[]
  monthlyVariance: any[]
  varianceReasons: any[]
  departmentVariance: any[]
}

export function VarianceAnalysis({ onBack }: VarianceAnalysisProps) {
  const [data, setData] = useState<VarianceData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const fetchVarianceData = async () => {
      try {
        const response = await fetch("/api/variance")
        if (!response.ok) {
          throw new Error(`HTTP error! status: ${response.status}`)
        }
        const result = await response.json()
        setData(result)
      } catch (e: any) {
        setError(e.message)
      } finally {
        setLoading(false)
      }
    }

    fetchVarianceData()
  }, [])

  if (loading) {
    return <div className="p-8 text-center text-sm text-muted-foreground">Loading variance analysis...</div>
  }

  if (error) {
    return (
      <div className="p-8 max-w-7xl mx-auto">
        <div className="p-4 rounded-lg border border-accent/30 bg-accent/5 text-accent text-sm flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>Failed to load variance data: {error}</span>
        </div>
      </div>
    )
  }

  if (!data) {
    return <div className="p-8 text-center text-sm text-muted-foreground">No variance data available.</div>
  }

  const { varianceData, monthlyVariance, varianceReasons, departmentVariance } = data

  const totalBudget = varianceData.reduce((sum, item) => sum + item.budget, 0)
  const totalActual = varianceData.reduce((sum, item) => sum + item.actual, 0)
  const totalVariance = totalActual - totalBudget
  const variancePercent = totalBudget > 0 ? (totalVariance / totalBudget) * 100 : 0
  const favorableCount = varianceReasons.filter((r) => r.type === "favorable").length
  const unfavorableCount = varianceReasons.filter((r) => r.type === "unfavorable").length

  const handleExportReport = () => {
    const csvContent = [
      ["Variance Analysis Report"],
      ["Generated:", new Date().toLocaleDateString()],
      [],
      ["Category", "Budget (PHP)", "Actual (PHP)", "Variance (PHP)", "Variance %"],
      ...varianceData.map((item) => [
        item.category,
        `₱${item.budget}`,
        `₱${item.actual}`,
        `₱${item.variance}`,
        `${item.variancePercent.toFixed(2)}%`,
      ]),
      [],
      ["Total", `₱${totalBudget}`, `₱${totalActual}`, `₱${totalVariance}`, `${variancePercent.toFixed(2)}%`],
    ]
      .map((row) => row.join(","))
      .join("\n")

    const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" })
    const url = window.URL.createObjectURL(blob)
    const a = document.createElement("a")
    a.href = url
    a.download = `variance-report-${new Date().toISOString().split("T")[0]}.csv`
    a.click()
  }

  return (
    <div className="p-8 space-y-8 max-w-7xl mx-auto">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-4 border-b border-border pb-6">
        <div className="flex items-center gap-4">
          <Button variant="outline" size="icon-sm" onClick={onBack} className="border-border">
            <ArrowLeft className="w-4 h-4" />
          </Button>
          <div>
            <p className="text-xs font-mono uppercase tracking-widest text-muted-foreground">
              Budget Auditing
            </p>
            <h1 className="text-3xl font-bold tracking-tight text-foreground mt-0.5">
              Variance Analysis & Reports
            </h1>
          </div>
        </div>
        <Button
          onClick={handleExportReport}
          size="sm"
          className="gap-2 bg-primary text-primary-foreground hover:bg-primary/90"
        >
          <Download className="w-3.5 h-3.5" />
          <span>Export CSV Report</span>
        </Button>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Total Budget
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              ₱{(totalBudget / 1000).toFixed(0)}K
            </div>
            <p className="text-xs text-muted-foreground mt-1">Allocated baseline</p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Total Actual
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              ₱{(totalActual / 1000).toFixed(0)}K
            </div>
            <p className="text-xs text-muted-foreground mt-1">Incurred ledger spend</p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Net Variance
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-baseline gap-2">
              <div
                className={`text-2xl font-bold font-mono tabular-nums ${
                  totalVariance > 0 ? "text-accent" : "text-foreground"
                }`}
              >
                ₱{Math.abs(totalVariance / 1000).toFixed(0)}K
              </div>
              {totalVariance > 0 ? (
                <TrendingDown className="w-4 h-4 text-accent" />
              ) : (
                <TrendingUp className="w-4 h-4 text-foreground" />
              )}
            </div>
            <p className={`text-xs mt-1 font-medium ${totalVariance > 0 ? "text-accent" : "text-muted-foreground"}`}>
              {totalVariance > 0 ? "Unfavorable drift" : "Favorable savings"}
            </p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Variance Ratio
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              {Math.abs(variancePercent).toFixed(2)}%
            </div>
            <p className="text-xs text-muted-foreground mt-1">Percentage of total budget</p>
          </CardContent>
        </Card>
      </div>

      {/* Charts */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-base font-semibold">Variance by Category</CardTitle>
            <CardDescription className="text-xs">Budget (Black) vs. Actual (Red)</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={280}>
              <BarChart data={varianceData} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="var(--border)" />
                <XAxis dataKey="category" stroke="var(--muted-foreground)" fontSize={12} tickLine={false} axisLine={false} />
                <YAxis stroke="var(--muted-foreground)" fontSize={12} tickLine={false} axisLine={false} />
                <Tooltip
                  contentStyle={{
                    backgroundColor: "var(--card)",
                    border: "1px solid var(--border)",
                    borderRadius: "var(--radius)",
                    fontSize: "12px",
                  }}
                />
                <Legend iconType="circle" wrapperStyle={{ fontSize: "12px", paddingTop: "10px" }} />
                <Bar dataKey="budget" fill="var(--chart-1)" radius={[4, 4, 0, 0]} name="Budget (₱)" />
                <Bar dataKey="actual" fill="var(--chart-2)" radius={[4, 4, 0, 0]} name="Actual (₱)" />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-base font-semibold">Monthly Variance Trend</CardTitle>
            <CardDescription className="text-xs">Variance percentage movement over time</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={280}>
              <AreaChart data={monthlyVariance} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
                <defs>
                  <linearGradient id="colorVariance" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="var(--chart-2)" stopOpacity={0.25} />
                    <stop offset="95%" stopColor="var(--chart-2)" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="var(--border)" />
                <XAxis dataKey="month" stroke="var(--muted-foreground)" fontSize={12} tickLine={false} axisLine={false} />
                <YAxis stroke="var(--muted-foreground)" fontSize={12} tickLine={false} axisLine={false} />
                <Tooltip
                  contentStyle={{
                    backgroundColor: "var(--card)",
                    border: "1px solid var(--border)",
                    borderRadius: "var(--radius)",
                    fontSize: "12px",
                  }}
                />
                <Area
                  type="monotone"
                  dataKey="percentage"
                  stroke="var(--chart-2)"
                  strokeWidth={2}
                  fillOpacity={1}
                  fill="url(#colorVariance)"
                  name="Variance %"
                />
              </AreaChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      {/* Root Cause Breakdown */}
      <Card className="shadow-none border-border/90 overflow-hidden">
        <CardHeader className="border-b border-border/60 pb-4">
          <CardTitle className="text-base font-semibold">Variance Drivers & Root Causes</CardTitle>
          <CardDescription className="text-xs">
            {favorableCount} favorable · {unfavorableCount} unfavorable cost drivers identified
          </CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          <div className="divide-y divide-border/70">
            {varianceReasons.map((item) => {
              const isUnfavorable = item.type === "unfavorable"
              return (
                <div
                  key={item.reason}
                  className="flex items-center justify-between px-6 py-4 hover:bg-muted/30 transition-colors"
                >
                  <div className="space-y-1">
                    <div className="flex items-center gap-2.5">
                      <span className="font-semibold text-sm text-foreground">{item.reason}</span>
                      {item.impact === "High" && (
                        <span className="text-[10px] font-mono uppercase px-2 py-0.5 rounded-full bg-accent/10 text-accent border border-accent/20 font-medium">
                          High Impact
                        </span>
                      )}
                    </div>
                    <div className="flex gap-4 text-xs">
                      <span className={isUnfavorable ? "text-accent font-medium" : "text-foreground font-medium"}>
                        {isUnfavorable ? "Unfavorable" : "Favorable"}
                      </span>
                      <span className="text-muted-foreground">· Severity: {item.impact}</span>
                    </div>
                  </div>
                  <div className="flex items-center gap-2 font-mono tabular-nums">
                    {isUnfavorable ? (
                      <TrendingDown className="w-4 h-4 text-accent" />
                    ) : (
                      <TrendingUp className="w-4 h-4 text-foreground" />
                    )}
                    <span className={`text-lg font-bold ${isUnfavorable ? "text-accent" : "text-foreground"}`}>
                      ₱{Math.abs(item.amount / 1000).toFixed(1)}K
                    </span>
                  </div>
                </div>
              )
            })}
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

