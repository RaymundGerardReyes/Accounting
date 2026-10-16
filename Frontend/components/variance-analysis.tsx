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

// Corrected the interface to match the JSON structure
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
        const response = await fetch("http://localhost:5000/api/variance")
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
    return <div className="p-8 text-center">Loading variance analysis...</div>
  }

  if (error) {
    return <div className="p-8 text-center text-red-500">Failed to load data: {error}</div>
  }

  if (!data) {
    return <div className="p-8 text-center">No variance data available.</div>
  }

  // Corrected the destructuring to match the JSON key 'varianceData'
  const { varianceData, monthlyVariance, varianceReasons, departmentVariance } = data

  const totalBudget = varianceData.reduce((sum, item) => sum + item.budget, 0)
  const totalActual = varianceData.reduce((sum, item) => sum + item.actual, 0)
  const totalVariance = totalActual - totalBudget
  const variancePercent = totalBudget > 0 ? (totalVariance / totalBudget) * 100 : 0
  const favorableCount = varianceReasons.filter((r) => r.type === "favorable").length
  const unfavorableCount = varianceReasons.filter((r) => r.type === "unfavorable").length

  const handleExportReport = () => {
    // Generate CSV report
    const csvContent = [
      ["Variance Analysis Report"],
      ["Generated:", new Date().toLocaleDateString()],
      [],
      ["Category", "Budget", "Actual", "Variance", "Variance %"],
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

    const blob = new Blob([csvContent], { type: "text/csv" })
    const url = window.URL.createObjectURL(blob)
    const a = document.createElement("a")
    a.href = url
    a.download = `variance-report-${new Date().toISOString().split("T")[0]}.csv`
    a.click()
  }

  return (
    <div className="p-8 space-y-8">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="outline" size="icon" onClick={onBack}>
            <ArrowLeft className="w-4 h-4" />
          </Button>
          <div>
            <h1 className="text-4xl font-bold text-foreground">Variance Analysis & Reports</h1>
            <p className="text-muted-foreground mt-2">Comprehensive budget vs actual cost analysis</p>
          </div>
        </div>
        <Button onClick={handleExportReport} className="gap-2">
          <Download className="w-4 h-4" />
          Export Report
        </Button>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-4 gap-4">
        <Card className="border-l-4 border-l-accent">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Budget</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">₱{(totalBudget / 1000).toFixed(0)}K</div>
            <p className="text-xs text-muted-foreground mt-1">Current month</p>
          </CardContent>
        </Card>

        <Card className="border-l-4 border-l-primary">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Actual</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">₱{(totalActual / 1000).toFixed(0)}K</div>
            <p className="text-xs text-muted-foreground mt-1">Incurred</p>
          </CardContent>
        </Card>

        <Card className={`border-l-4 ${totalVariance > 0 ? "border-l-primary" : "border-l-accent"}`}>
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Variance</CardTitle>
          </CardHeader>
          <CardContent>
            <div className={`flex items-baseline gap-2`}>
              <div className={`text-3xl font-bold ${totalVariance > 0 ? "text-primary" : "text-accent"}`}>
                ₱{Math.abs(totalVariance / 1000).toFixed(0)}K
              </div>
              {totalVariance > 0 ? (
                <TrendingDown className="w-5 h-5 text-primary" />
              ) : (
                <TrendingUp className="w-5 h-5 text-accent" />
              )}
            </div>
            <p className={`text-xs mt-1 ${totalVariance > 0 ? "text-primary" : "text-accent"}`}>
              {totalVariance > 0 ? "Unfavorable" : "Favorable"}
            </p>
          </CardContent>
        </Card>

        <Card className="border-l-4 border-l-accent">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Variance %</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">{Math.abs(variancePercent).toFixed(2)}%</div>
            <p className="text-xs text-muted-foreground mt-1">Of budget</p>
          </CardContent>
        </Card>
      </div>

      {/* Charts */}
      <div className="grid grid-cols-2 gap-6">
        {/* Category Variance */}
        <Card>
          <CardHeader>
            <CardTitle>Variance by Category</CardTitle>
            <CardDescription>Budget vs Actual comparison</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={300}>
              <BarChart data={varianceData}>
                <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--color-border))" />
                <XAxis dataKey="category" stroke="hsl(var(--color-muted-foreground))" />
                <YAxis stroke="hsl(var(--color-muted-foreground))" />
                <Tooltip
                  contentStyle={{
                    backgroundColor: "hsl(var(--color-card))",
                    border: "1px solid hsl(var(--color-border))",
                  }}
                  labelStyle={{ color: "hsl(var(--color-foreground))" }}
                />
                <Legend />
                <Bar dataKey="budget" fill="hsl(var(--color-chart-2))" name="Budget" />
                <Bar dataKey="actual" fill="hsl(var(--color-chart-1))" name="Actual" />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        {/* Monthly Variance Trend */}
        <Card>
          <CardHeader>
            <CardTitle>Monthly Variance Trend</CardTitle>
            <CardDescription>Variance percentage over time</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={300}>
              <AreaChart data={monthlyVariance}>
                <defs>
                  <linearGradient id="colorVariance" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="hsl(var(--color-chart-1))" stopOpacity={0.8} />
                    <stop offset="95%" stopColor="hsl(var(--color-chart-1))" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--color-border))" />
                <XAxis dataKey="month" stroke="hsl(var(--color-muted-foreground))" />
                <YAxis stroke="hsl(var(--color-muted-foreground))" />
                <Tooltip
                  contentStyle={{
                    backgroundColor: "hsl(var(--color-card))",
                    border: "1px solid hsl(var(--color-border))",
                  }}
                  labelStyle={{ color: "hsl(var(--color-foreground))" }}
                />
                <Area
                  type="monotone"
                  dataKey="percentage"
                  stroke="hsl(var(--color-chart-1))"
                  fillOpacity={1}
                  fill="url(#colorVariance)"
                  name="Variance %"
                />
              </AreaChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      {/* Department Variance */}
      <Card>
        <CardHeader>
          <CardTitle>Department Variance Summary</CardTitle>
          <CardDescription>Budget vs Actual by department</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="space-y-3">
            {departmentVariance.map((dept) => (
              <div
                key={dept.department}
                className="flex items-center justify-between p-4 border border-border rounded-lg hover:bg-muted transition-colors"
              >
                <div className="flex-1">
                  <div className="flex items-center gap-2">
                    <h3 className="font-semibold text-foreground">{dept.department}</h3>
                    {dept.status === "favorable" ? (
                      <TrendingUp className="w-4 h-4 text-accent" />
                    ) : dept.status === "unfavorable" ? (
                      <TrendingDown className="w-4 h-4 text-primary" />
                    ) : null}
                  </div>
                  <div className="flex gap-6 mt-1 text-sm text-muted-foreground">
                    <span>Budget: ₱{(dept.budget / 1000).toFixed(0)}K</span>
                    <span>Actual: ₱{(dept.actual / 1000).toFixed(0)}K</span>
                  </div>
                </div>
                <div className="text-right">
                  <p
                    className={`font-semibold ${dept.status === "favorable"
                        ? "text-accent"
                        : dept.status === "unfavorable"
                          ? "text-primary"
                          : "text-muted-foreground"
                      }`}
                  >
                    ₱{Math.abs(dept.variance / 1000).toFixed(1)}K
                  </p>
                  <p
                    className={`text-xs ${dept.status === "favorable"
                        ? "text-accent"
                        : dept.status === "unfavorable"
                          ? "text-primary"
                          : "text-muted-foreground"
                      }`}
                  >
                    {dept.status === "favorable"
                      ? "Favorable"
                      : dept.status === "unfavorable"
                        ? "Unfavorable"
                        : "Neutral"}
                  </p>
                </div>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>

      {/* Variance Reasons */}
      <Card>
        <CardHeader>
          <CardTitle>Variance Analysis Breakdown</CardTitle>
          <CardDescription>
            {favorableCount} favorable, {unfavorableCount} unfavorable variances
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="space-y-3">
            {varianceReasons.map((item) => (
              <div
                key={item.reason}
                className="flex items-center justify-between p-4 border border-border rounded-lg hover:bg-muted transition-colors"
              >
                <div className="flex-1">
                  <div className="flex items-center gap-2">
                    <h3 className="font-semibold text-foreground">{item.reason}</h3>
                    {item.impact === "High" && <AlertCircle className="w-4 h-4 text-primary" />}
                  </div>
                  <div className="flex gap-4 mt-1 text-sm">
                    <span className={item.type === "favorable" ? "text-accent" : "text-primary"}>
                      {item.type === "favorable" ? "Favorable" : "Unfavorable"}
                    </span>
                    <span className="text-muted-foreground">Impact: {item.impact}</span>
                  </div>
                </div>
                <div className="flex items-center gap-2">
                  {item.type === "favorable" ? (
                    <TrendingUp className="w-5 h-5 text-accent" />
                  ) : (
                    <TrendingDown className="w-5 h-5 text-primary" />
                  )}
                  <span className={`text-2xl font-bold ${item.type === "favorable" ? "text-accent" : "text-primary"}`}>
                    ₱{Math.abs(item.amount / 1000).toFixed(1)}K
                  </span>
                </div>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

