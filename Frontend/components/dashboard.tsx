"use client"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { AlertCircle, ArrowDownRight, ArrowUpRight, ChevronRight, TrendingUp } from "lucide-react"
import { useEffect, useState } from "react"
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts"

interface DashboardProps {
  onSelectDepartment: (dept: string) => void
}

interface DashboardData {
  costTrendData: any[]
  departmentData: any[]
  departmentMetrics: any[]
  varianceTrendData: any[]
}

export function Dashboard({ onSelectDepartment }: DashboardProps) {
  const [data, setData] = useState<DashboardData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const fetchDashboardData = async () => {
      try {
        const response = await fetch("/api/dashboard")
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

    fetchDashboardData()
  }, [])

  if (loading) {
    return (
      <div className="p-8 space-y-6 max-w-7xl mx-auto">
        <div className="h-8 w-64 bg-muted animate-pulse rounded-md" />
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          {[...Array(4)].map((_, i) => (
            <div key={i} className="h-28 bg-muted/60 animate-pulse rounded-lg border border-border" />
          ))}
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="p-8 max-w-7xl mx-auto">
        <div className="p-4 rounded-lg border border-accent/30 bg-accent/5 text-accent text-sm flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>Failed to load dashboard data: {error}</span>
        </div>
      </div>
    )
  }

  if (!data) {
    return <div className="p-8 text-center text-sm text-muted-foreground">No dashboard data available.</div>
  }

  const { costTrendData, departmentData, departmentMetrics, varianceTrendData } = data

  const totalAllocated = departmentMetrics.reduce((sum, dept) => sum + dept.allocated, 0)
  const totalActual = departmentMetrics.reduce((sum, dept) => sum + dept.actual, 0)
  const totalVariance = totalActual - totalAllocated
  const favorableCount = departmentMetrics.filter((d) => d.status === "favorable").length
  const unfavorableCount = departmentMetrics.filter((d) => d.status === "unfavorable").length

  // Strict palette shades for pie slices (Black, Red, and Neutrals)
  const PIE_COLORS = [
    "oklch(0.15 0 0)",
    "oklch(0.55 0.2 25)",
    "oklch(0.38 0 0)",
    "oklch(0.68 0.14 25)",
    "oklch(0.65 0 0)",
  ]

  return (
    <div className="p-8 space-y-8 max-w-7xl mx-auto">
      {/* Minimalist Header */}
      <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-4 border-b border-border pb-6">
        <div>
          <p className="text-xs font-mono uppercase tracking-widest text-muted-foreground">
            Executive Overview
          </p>
          <h1 className="text-3xl font-bold tracking-tight text-foreground mt-1">
            Cost Analysis Dashboard
          </h1>
        </div>
        <div className="flex items-center gap-2 text-xs text-muted-foreground font-mono bg-card px-3 py-1.5 rounded-md border border-border">
          <span className="w-2 h-2 rounded-full bg-primary" />
          <span>Synced · Real-time PHP (₱) Ledger</span>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Total Allocated
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums tracking-tight text-foreground">
              ₱{(totalAllocated / 1000000).toFixed(2)}M
            </div>
            <p className="text-xs text-muted-foreground mt-1">Master budget across cost centers</p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Total Actual
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums tracking-tight text-foreground">
              ₱{(totalActual / 1000000).toFixed(2)}M
            </div>
            <p className="text-xs text-muted-foreground mt-1">Verified cumulative spend</p>
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
                className={`text-2xl font-bold font-mono tabular-nums tracking-tight ${
                  totalVariance > 0 ? "text-accent" : "text-foreground"
                }`}
              >
                {totalVariance > 0 ? "+" : "-"}₱{Math.abs(totalVariance / 1000).toFixed(0)}K
              </div>
              {totalVariance > 0 ? (
                <ArrowUpRight className="w-4 h-4 text-accent" />
              ) : (
                <ArrowDownRight className="w-4 h-4 text-foreground" />
              )}
            </div>
            <p className={`text-xs mt-1 font-medium ${totalVariance > 0 ? "text-accent" : "text-muted-foreground"}`}>
              {totalVariance > 0 ? "Over budget threshold" : "Within allocated budget"}
            </p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Active Cost Centers
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums tracking-tight text-foreground">
              {departmentMetrics.length}
            </div>
            <div className="flex items-center gap-3 mt-1 text-xs">
              <span className="text-foreground font-medium">{favorableCount} favorable</span>
              <span className="text-border">·</span>
              <span className="text-accent font-medium">{unfavorableCount} unfavorable</span>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Primary Charts */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Cost Trend */}
        <Card className="lg:col-span-2 shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-base font-semibold">Cost Trend Analysis</CardTitle>
            <CardDescription className="text-xs">
              Allocated budget (Black) vs. Actual expenditure (Red)
            </CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={280}>
              <LineChart data={costTrendData} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="var(--border)" />
                <XAxis
                  dataKey="month"
                  stroke="var(--muted-foreground)"
                  fontSize={12}
                  tickLine={false}
                  axisLine={false}
                />
                <YAxis
                  stroke="var(--muted-foreground)"
                  fontSize={12}
                  tickLine={false}
                  axisLine={false}
                />
                <Tooltip
                  contentStyle={{
                    backgroundColor: "var(--card)",
                    border: "1px solid var(--border)",
                    borderRadius: "var(--radius)",
                    fontSize: "12px",
                  }}
                  labelStyle={{ color: "var(--foreground)", fontWeight: 600 }}
                />
                <Legend iconType="circle" wrapperStyle={{ fontSize: "12px", paddingTop: "12px" }} />
                <Line
                  type="monotone"
                  dataKey="allocated"
                  stroke="var(--chart-1)"
                  strokeWidth={2}
                  dot={false}
                  name="Allocated (₱)"
                />
                <Line
                  type="monotone"
                  dataKey="actual"
                  stroke="var(--chart-2)"
                  strokeWidth={2}
                  dot={{ r: 3, fill: "var(--chart-2)" }}
                  name="Actual (₱)"
                />
              </LineChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        {/* Department Distribution */}
        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-base font-semibold">Cost Distribution</CardTitle>
            <CardDescription className="text-xs">Share by department (%)</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={280}>
              <PieChart>
                <Pie
                  data={departmentData}
                  cx="50%"
                  cy="50%"
                  innerRadius={62}
                  outerRadius={95}
                  paddingAngle={3}
                  dataKey="value"
                >
                  {departmentData.map((_: any, index: number) => (
                    <Cell key={`cell-${index}`} fill={PIE_COLORS[index % PIE_COLORS.length]} />
                  ))}
                </Pie>
                <Tooltip
                  contentStyle={{
                    backgroundColor: "var(--card)",
                    border: "1px solid var(--border)",
                    borderRadius: "var(--radius)",
                    fontSize: "12px",
                  }}
                />
              </PieChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      {/* Variance Bar Chart */}
      <Card className="shadow-none border-border/90">
        <CardHeader className="pb-2">
          <CardTitle className="text-base font-semibold">Monthly Variance Distribution</CardTitle>
          <CardDescription className="text-xs">
            Favorable vs. Unfavorable cost center drift over time
          </CardDescription>
        </CardHeader>
        <CardContent>
          <ResponsiveContainer width="100%" height={240}>
            <BarChart data={varianceTrendData} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="var(--border)" />
              <XAxis
                dataKey="month"
                stroke="var(--muted-foreground)"
                fontSize={12}
                tickLine={false}
                axisLine={false}
              />
              <YAxis
                stroke="var(--muted-foreground)"
                fontSize={12}
                tickLine={false}
                axisLine={false}
              />
              <Tooltip
                contentStyle={{
                  backgroundColor: "var(--card)",
                  border: "1px solid var(--border)",
                  borderRadius: "var(--radius)",
                  fontSize: "12px",
                }}
                labelStyle={{ color: "var(--foreground)", fontWeight: 600 }}
              />
              <Legend iconType="circle" wrapperStyle={{ fontSize: "12px", paddingTop: "10px" }} />
              <Bar dataKey="favorable" stackId="a" fill="var(--chart-1)" radius={[0, 0, 4, 4]} name="Favorable" />
              <Bar dataKey="unfavorable" stackId="a" fill="var(--chart-2)" radius={[4, 4, 0, 0]} name="Unfavorable" />
            </BarChart>
          </ResponsiveContainer>
        </CardContent>
      </Card>

      {/* Department Performance List */}
      <Card className="shadow-none border-border/90 overflow-hidden">
        <CardHeader className="border-b border-border/60 pb-4">
          <CardTitle className="text-base font-semibold">Department Ledger Performance</CardTitle>
          <CardDescription className="text-xs">
            Click any department row to inspect granular cost drivers
          </CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          <div className="divide-y divide-border/70">
            {departmentMetrics.map((dept) => {
              const isUnfavorable = dept.status === "unfavorable"
              return (
                <div
                  key={dept.name}
                  onClick={() => onSelectDepartment(dept.name.toLowerCase().replace(" ", "-"))}
                  className="group flex items-center justify-between px-6 py-4 hover:bg-muted/40 cursor-pointer transition-colors"
                >
                  <div className="space-y-1">
                    <div className="flex items-center gap-2.5">
                      <span className="font-semibold text-sm text-foreground">{dept.name}</span>
                      <span
                        className={`text-[10px] font-mono uppercase px-2 py-0.5 rounded-full font-medium ${
                          isUnfavorable
                            ? "bg-accent/10 text-accent border border-accent/20"
                            : "bg-muted text-foreground"
                        }`}
                      >
                        {dept.status}
                      </span>
                    </div>
                    <div className="flex gap-6 text-xs text-muted-foreground font-mono tabular-nums">
                      <span>Allocated: ₱{(dept.allocated / 1000).toFixed(0)}K</span>
                      <span>Actual: ₱{(dept.actual / 1000).toFixed(0)}K</span>
                    </div>
                  </div>

                  <div className="flex items-center gap-6">
                    <div
                      className={`text-right font-mono tabular-nums ${
                        isUnfavorable ? "text-accent" : "text-foreground"
                      }`}
                    >
                      <div className="text-sm font-bold">
                        {dept.variance > 0 ? "+" : ""}₱{Math.abs(dept.variance / 1000).toFixed(1)}K
                      </div>
                      <div className="text-[11px] text-muted-foreground">
                        {dept.variancePercent > 0 ? "+" : ""}
                        {dept.variancePercent.toFixed(1)}%
                      </div>
                    </div>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      className="text-muted-foreground group-hover:text-foreground group-hover:translate-x-0.5 transition-all"
                    >
                      <ChevronRight className="w-4 h-4" />
                    </Button>
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

