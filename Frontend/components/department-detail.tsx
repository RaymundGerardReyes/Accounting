"use client"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { AlertCircle, ArrowLeft, CheckCircle2 } from "lucide-react"
import { useEffect, useState } from "react"
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts"

interface DepartmentDetailProps {
  department: string
  onBack: () => void
}

interface DepartmentData {
  costBreakdown: any[]
  monthlyTrend: any[]
  departmentStats: {
    allocated: number
    actual: number
    variance: number
    variancePercent: number
    status: "favorable" | "unfavorable" | "neutral"
  }
}

export function DepartmentDetail({ department, onBack }: DepartmentDetailProps) {
  const [data, setData] = useState<DepartmentData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const fetchDepartmentData = async () => {
      try {
        const response = await fetch(`/api/department/${department}`)
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

    fetchDepartmentData()
  }, [department])

  if (loading) {
    return <div className="p-8 text-center text-sm text-muted-foreground">Loading department details...</div>
  }

  if (error) {
    return (
      <div className="p-8 max-w-7xl mx-auto">
        <div className="p-4 rounded-lg border border-accent/30 bg-accent/5 text-accent text-sm flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>Failed to load department data: {error}</span>
        </div>
      </div>
    )
  }

  if (!data) {
    return <div className="p-8 text-center text-sm text-muted-foreground">No data available for this department.</div>
  }

  const rawCostBreakdown = data?.costBreakdown ?? (data as any)?.cost_breakdown
  const rawMonthlyTrend = data?.monthlyTrend ?? (data as any)?.monthly_trend
  const rawDeptStats: any = data?.departmentStats ?? (data as any)?.department_stats

  const costBreakdown: any[] = (
    Array.isArray(rawCostBreakdown) && rawCostBreakdown.length > 0
      ? rawCostBreakdown
      : (data as any)?.direct_cost_minor !== undefined || (data as any)?.directCostMinor !== undefined
        ? [
            {
              category: "Direct Operations",
              allocated: (((data as any).budget_limit_minor ?? (data as any).budgetLimitMinor ?? 0) / 100) * 0.65,
              actual: ((data as any).direct_cost_minor ?? (data as any).directCostMinor ?? 0) / 100,
              variance: ((((data as any).budget_limit_minor ?? (data as any).budgetLimitMinor ?? 0) / 100) * 0.65) - (((data as any).direct_cost_minor ?? (data as any).directCostMinor ?? 0) / 100),
              variancePercent: 0,
            },
            {
              category: "Allocated Overhead",
              allocated: (((data as any).budget_limit_minor ?? (data as any).budgetLimitMinor ?? 0) / 100) * 0.35,
              actual: ((data as any).overhead_share_minor ?? (data as any).overheadShareMinor ?? 0) / 100,
              variance: ((((data as any).budget_limit_minor ?? (data as any).budgetLimitMinor ?? 0) / 100) * 0.35) - (((data as any).overhead_share_minor ?? (data as any).overheadShareMinor ?? 0) / 100),
              variancePercent: 0,
            },
          ]
        : []
  ).map((item: any) => ({
    category: item.category ?? "General",
    allocated: item.allocated ?? 0,
    actual: item.actual ?? 0,
    variance: item.variance ?? ((item.allocated ?? 0) - (item.actual ?? 0)),
    variancePercent: item.variancePercent ?? item.variance_percent ?? 0,
  }))

  const monthlyTrend: any[] = Array.isArray(rawMonthlyTrend)
    ? rawMonthlyTrend
    : [
        { month: "Jan", cost: 125000, budget: 120000, variance: -5000 },
        { month: "Feb", cost: 132000, budget: 125000, variance: -7000 },
        { month: "Mar", cost: 128000, budget: 125000, variance: -3000 },
        { month: "Apr", cost: 140000, budget: 135000, variance: -5000 },
        { month: "May", cost: 145000, budget: 140000, variance: -5000 },
        { month: "Jun", cost: 152000, budget: 150000, variance: -2000 },
      ]

  const totalAllocated = costBreakdown.reduce((sum, item) => sum + (item.allocated || 0), 0)
  const totalActual = costBreakdown.reduce((sum, item) => sum + (item.actual || 0), 0)
  const favorableCategories = costBreakdown.filter((item) => (item.variance || 0) >= 0).length

  const computedVariance = totalAllocated - totalActual
  const departmentStats = {
    allocated: rawDeptStats?.allocated ?? (rawDeptStats?.budget_limit_minor ? rawDeptStats.budget_limit_minor / 100 : totalAllocated),
    actual: rawDeptStats?.actual ?? (rawDeptStats?.total_cost_minor ? rawDeptStats.total_cost_minor / 100 : totalActual),
    variance: rawDeptStats?.variance ?? (rawDeptStats?.variance_minor ? rawDeptStats.variance_minor / 100 : computedVariance),
    variancePercent: rawDeptStats?.variancePercent ?? rawDeptStats?.variance_percent ?? (totalAllocated > 0 ? (computedVariance / totalAllocated) * 100 : 0),
    status: rawDeptStats?.status ?? (computedVariance < 0 ? "unfavorable" : "favorable"),
  }




  const isUnfavorable = departmentStats.status === "unfavorable"

  return (
    <div className="p-8 space-y-8 max-w-7xl mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-border pb-6">
        <div className="flex items-center gap-4">
          <Button variant="outline" size="icon-sm" onClick={onBack} className="border-border">
            <ArrowLeft className="w-4 h-4" />
          </Button>
          <div>
            <p className="text-xs font-mono uppercase tracking-widest text-muted-foreground">
              Cost Center Drill-Down
            </p>
            <h1 className="text-3xl font-bold tracking-tight text-foreground capitalize mt-0.5">
              {department.replace("-", " ")} Department
            </h1>
          </div>
        </div>
      </div>

      {/* KPI Metrics */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Total Allocated
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              ₱{((departmentStats.allocated || 0) / 1000).toFixed(0)}K
            </div>
            <p className="text-xs text-muted-foreground mt-1">Current period budget</p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Actual Cost
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              ₱{((departmentStats.actual || 0) / 1000).toFixed(0)}K
            </div>
            <p className="text-xs text-muted-foreground mt-1">Incurred expenditure</p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Variance
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div
              className={`text-2xl font-bold font-mono tabular-nums ${
                isUnfavorable ? "text-accent" : "text-foreground"
              }`}
            >
              ₱{Math.abs((departmentStats.variance || 0) / 1000).toFixed(0)}K
            </div>
            <p className={`text-xs mt-1 font-medium ${isUnfavorable ? "text-accent" : "text-muted-foreground"}`}>
              {isUnfavorable ? "Unfavorable (Over budget)" : "Favorable (Within budget)"}
            </p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Cost Categories
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              {costBreakdown.length}
            </div>
            <div className="flex gap-2 mt-1 text-xs">
              <span className="text-foreground font-medium">{favorableCategories} favorable</span>
              <span className="text-border">·</span>
              <span className="text-accent font-medium">{costBreakdown.length - favorableCategories} unfavorable</span>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Charts */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-base font-semibold">Cost Breakdown</CardTitle>
            <CardDescription className="text-xs">Allocated vs. Actual by category</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={280}>
              <BarChart data={costBreakdown} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
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
                <Bar dataKey="allocated" fill="var(--chart-1)" radius={[4, 4, 0, 0]} name="Allocated (₱)" />
                <Bar dataKey="actual" fill="var(--chart-2)" radius={[4, 4, 0, 0]} name="Actual (₱)" />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-base font-semibold">Monthly Trend</CardTitle>
            <CardDescription className="text-xs">Actual Cost vs. Budget over time</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={280}>
              <LineChart data={monthlyTrend} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
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
                <Legend iconType="circle" wrapperStyle={{ fontSize: "12px", paddingTop: "10px" }} />
                <Line type="monotone" dataKey="budget" stroke="var(--chart-1)" strokeWidth={2} dot={false} name="Budget (₱)" />
                <Line type="monotone" dataKey="cost" stroke="var(--chart-2)" strokeWidth={2} dot={{ r: 3 }} name="Actual Cost (₱)" />
              </LineChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      {/* Cost Categories List */}
      <Card className="shadow-none border-border/90 overflow-hidden">
        <CardHeader className="border-b border-border/60 pb-4">
          <CardTitle className="text-base font-semibold">Cost Categories Ledger</CardTitle>
          <CardDescription className="text-xs">Granular breakdown and variance analysis</CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          <div className="divide-y divide-border/70">
            {costBreakdown.map((item) => {
              const itemUnfavorable = item.actual > item.allocated
              return (
                <div
                  key={item.category}
                  className="flex items-center justify-between px-6 py-4 hover:bg-muted/30 transition-colors"
                >
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <span className="font-semibold text-sm text-foreground">{item.category}</span>
                      {itemUnfavorable ? (
                        <AlertCircle className="w-3.5 h-3.5 text-accent" />
                      ) : (
                        <CheckCircle2 className="w-3.5 h-3.5 text-foreground" />
                      )}
                    </div>
                    <div className="flex gap-6 text-xs text-muted-foreground font-mono tabular-nums">
                      <span>Allocated: ₱{((item.allocated || 0) / 1000).toFixed(0)}K</span>
                      <span>Actual: ₱{((item.actual || 0) / 1000).toFixed(0)}K</span>
                    </div>
                  </div>
                  <div className="text-right font-mono tabular-nums">
                    <p className={`text-sm font-bold ${itemUnfavorable ? "text-accent" : "text-foreground"}`}>
                      ₱{Math.abs((item.variance || 0) / 1000).toFixed(1)}K
                    </p>
                    <p className="text-[11px] text-muted-foreground">
                      {(item.variancePercent ?? 0) > 0 ? "+" : ""}
                      {(item.variancePercent ?? 0).toFixed(1)}%
                    </p>
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

