"use client"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { AlertCircle, ArrowDownRight, ArrowUpRight, TrendingUp } from "lucide-react"
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
        const response = await fetch("http://localhost:5000/api/dashboard")
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
    return <div className="p-8 text-center">Loading dashboard...</div>
  }

  if (error) {
    return <div className="p-8 text-center text-red-500">Failed to load data: {error}</div>
  }

  if (!data) {
    return <div className="p-8 text-center">No dashboard data available.</div>
  }

  const { costTrendData, departmentData, departmentMetrics, varianceTrendData } = data

  const totalAllocated = departmentMetrics.reduce((sum, dept) => sum + dept.allocated, 0)
  const totalActual = departmentMetrics.reduce((sum, dept) => sum + dept.actual, 0)
  const totalVariance = totalActual - totalAllocated
  const favorableCount = departmentMetrics.filter((d) => d.status === "favorable").length
  const unfavorableCount = departmentMetrics.filter((d) => d.status === "unfavorable").length

  return (
    <div className="p-8 space-y-8">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-4xl font-bold text-foreground">Cost Analysis Dashboard</h1>
          <p className="text-muted-foreground mt-2">Real-time cost allocation and variance analysis</p>
        </div>
        <div className="text-right text-sm text-muted-foreground">
          <p>Last updated: Today at 2:30 PM</p>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-4 gap-4">
        <Card className="border-l-4 border-l-accent">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Allocated</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">₱{(totalAllocated / 1000000).toFixed(2)}M</div>
            <p className="text-xs text-muted-foreground mt-1">All departments</p>
          </CardContent>
        </Card>

        <Card className="border-l-4 border-l-primary">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Actual</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">₱{(totalActual / 1000000).toFixed(2)}M</div>
            <p className="text-xs text-muted-foreground mt-1">Actual spend</p>
          </CardContent>
        </Card>

        <Card className={`border-l-4 ${totalVariance > 0 ? "border-l-accent" : "border-l-primary"}`}>
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Variance</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-baseline gap-2">
              <div className="text-3xl font-bold">₱{Math.abs(totalVariance / 1000).toFixed(0)}K</div>
              {totalVariance > 0 ? (
                <ArrowUpRight className="w-5 h-5 text-accent" />
              ) : (
                <ArrowDownRight className="w-5 h-5 text-primary" />
              )}
            </div>
            <p className={`text-xs mt-1 ${totalVariance > 0 ? "text-accent" : "text-primary"}`}>
              {totalVariance > 0 ? "Over budget" : "Under budget"}
            </p>
          </CardContent>
        </Card>

        <Card className="border-l-4 border-l-accent">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Departments</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">{departmentMetrics.length}</div>
            <div className="flex gap-2 mt-2 text-xs">
              <span className="text-accent">{favorableCount} favorable</span>
              <span className="text-primary">{unfavorableCount} unfavorable</span>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Charts */}
      <div className="grid grid-cols-3 gap-6">
        {/* Cost Trend */}
        <Card className="col-span-2">
          <CardHeader>
            <CardTitle>Cost Trend Analysis</CardTitle>
            <CardDescription>Allocated vs Actual costs over time</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={300}>
              <LineChart data={costTrendData}>
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
                <Legend />
                <Line
                  type="monotone"
                  dataKey="allocated"
                  stroke="hsl(var(--color-chart-1))"
                  strokeWidth={2}
                  dot={false}
                  name="Allocated"
                />
                <Line
                  type="monotone"
                  dataKey="actual"
                  stroke="hsl(var(--color-chart-2))"
                  strokeWidth={2}
                  dot={false}
                  name="Actual"
                />
              </LineChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        {/* Department Distribution */}
        <Card>
          <CardHeader>
            <CardTitle>Cost Distribution</CardTitle>
            <CardDescription>By department (%)</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={300}>
              <PieChart>
                <Pie
                  data={departmentData}
                  cx="50%"
                  cy="50%"
                  innerRadius={60}
                  outerRadius={100}
                  paddingAngle={2}
                  dataKey="value"
                >
                  {departmentData.map((entry, index) => (
                    <Cell key={`cell-${index}`} fill={entry.color} />
                  ))}
                </Pie>
              </PieChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Variance Trend</CardTitle>
          <CardDescription>Department performance over time</CardDescription>
        </CardHeader>
        <CardContent>
          <ResponsiveContainer width="100%" height={250}>
            <BarChart data={varianceTrendData}>
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
              <Legend />
              <Bar dataKey="favorable" stackId="a" fill="hsl(var(--color-chart-1))" name="Favorable" />
              <Bar dataKey="unfavorable" stackId="a" fill="hsl(var(--color-chart-2))" name="Unfavorable" />
            </BarChart>
          </ResponsiveContainer>
        </CardContent>
      </Card>

      {/* Department Metrics Table */}
      <Card>
        <CardHeader>
          <CardTitle>Department Performance</CardTitle>
          <CardDescription>Cost allocation and variance by department</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="space-y-3">
            {departmentMetrics.map((dept) => (
              <div
                key={dept.name}
                onClick={() => onSelectDepartment(dept.name.toLowerCase().replace(" ", "-"))}
                className="flex items-center justify-between p-4 border border-border rounded-lg hover:bg-muted cursor-pointer transition-colors"
              >
                <div className="flex-1">
                  <div className="flex items-center gap-2">
                    <h3 className="font-semibold text-foreground">{dept.name}</h3>
                    {dept.status === "favorable" ? (
                      <TrendingUp className="w-4 h-4 text-accent" />
                    ) : dept.status === "unfavorable" ? (
                      <AlertCircle className="w-4 h-4 text-primary" />
                    ) : null}
                  </div>
                  <div className="flex gap-6 mt-1 text-sm text-muted-foreground">
                    <span>Allocated: ₱{(dept.allocated / 1000).toFixed(0)}K</span>
                    <span>Actual: ₱{(dept.actual / 1000).toFixed(0)}K</span>
                  </div>
                </div>
                <div className="flex items-center gap-4">
                  <div
                    className={`flex flex-col items-end ${dept.status === "favorable"
                        ? "text-accent"
                        : dept.status === "unfavorable"
                          ? "text-primary"
                          : "text-muted-foreground"
                      }`}
                  >
                    <span className="font-semibold">₱{Math.abs(dept.variance / 1000).toFixed(1)}K</span>
                    <span className="text-xs">
                      {dept.variancePercent > 0 ? "+" : ""}
                      {dept.variancePercent.toFixed(1)}%
                    </span>
                  </div>
                  <Button variant="outline" size="sm">
                    View
                  </Button>
                </div>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

