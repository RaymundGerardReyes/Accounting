"use client"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { AlertCircle, ArrowLeft, CheckCircle } from "lucide-react"
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
        const response = await fetch(`http://localhost:5000/api/department/${department}`)
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
    return <div className="p-8 text-center">Loading department details...</div>
  }

  if (error) {
    return <div className="p-8 text-center text-red-500">Failed to load data: {error}</div>
  }

  if (!data) {
    return <div className="p-8 text-center">No data available for this department.</div>
  }

  const { costBreakdown, monthlyTrend, departmentStats } = data

  const totalAllocated = costBreakdown.reduce((sum, item) => sum + item.allocated, 0)
  const totalActual = costBreakdown.reduce((sum, item) => sum + item.actual, 0)
  const favorableCategories = costBreakdown.filter((item) => item.variance > 0).length

  return (
    <div className="p-8 space-y-8">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="outline" size="icon" onClick={onBack}>
            <ArrowLeft className="w-4 h-4" />
          </Button>
          <div>
            <h1 className="text-4xl font-bold text-foreground capitalize">{department} Department</h1>
            <p className="text-muted-foreground mt-2">Detailed cost analysis and allocation management</p>
          </div>
        </div>
      </div>

      {/* Metrics */}
      <div className="grid grid-cols-4 gap-4">
        <Card className="border-l-4 border-l-accent">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Allocated</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">₱{(departmentStats.allocated / 1000).toFixed(0)}K</div>
            <p className="text-xs text-muted-foreground mt-1">Current month</p>
          </CardContent>
        </Card>

        <Card className="border-l-4 border-l-primary">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Actual Cost</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">₱{(departmentStats.actual / 1000).toFixed(0)}K</div>
            <p className="text-xs text-muted-foreground mt-1">Incurred</p>
          </CardContent>
        </Card>

        <Card
          className={`border-l-4 ${departmentStats.status === "favorable" ? "border-l-accent" : "border-l-primary"}`}
        >
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Variance</CardTitle>
          </CardHeader>
          <CardContent>
            <div
              className={`text-3xl font-bold ${departmentStats.status === "favorable" ? "text-accent" : "text-primary"}`}
            >
              ₱{Math.abs(departmentStats.variance / 1000).toFixed(0)}K
            </div>
            <p className={`text-xs mt-1 ${departmentStats.status === "favorable" ? "text-accent" : "text-primary"}`}>
              {departmentStats.status === "favorable" ? "Favorable" : "Unfavorable"}
            </p>
          </CardContent>
        </Card>

        <Card className="border-l-4 border-l-accent">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Categories</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">{costBreakdown.length}</div>
            <div className="flex gap-2 mt-2 text-xs">
              <span className="text-accent">{favorableCategories} favorable</span>
              <span className="text-primary">{costBreakdown.length - favorableCategories} unfavorable</span>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Charts */}
      <div className="grid grid-cols-2 gap-6">
        {/* Cost Breakdown */}
        <Card>
          <CardHeader>
            <CardTitle>Cost Breakdown</CardTitle>
            <CardDescription>Allocated vs Actual by category</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={300}>
              <BarChart data={costBreakdown}>
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
                <Bar dataKey="allocated" fill="hsl(var(--color-chart-1))" name="Allocated" />
                <Bar dataKey="actual" fill="hsl(var(--color-chart-2))" name="Actual" />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        {/* Monthly Trend */}
        <Card>
          <CardHeader>
            <CardTitle>Monthly Trend</CardTitle>
            <CardDescription>Cost vs Budget over time</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={300}>
              <LineChart data={monthlyTrend}>
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
                  dataKey="cost"
                  stroke="hsl(var(--color-chart-1))"
                  strokeWidth={2}
                  name="Actual Cost"
                />
                <Line
                  type="monotone"
                  dataKey="budget"
                  stroke="hsl(var(--color-chart-2))"
                  strokeWidth={2}
                  name="Budget"
                />
              </LineChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      {/* Cost Categories Table */}
      <Card>
        <CardHeader>
          <CardTitle>Cost Categories</CardTitle>
          <CardDescription>Detailed breakdown and variance analysis</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="space-y-3">
            {costBreakdown.map((item) => (
              <div
                key={item.category}
                className="flex items-center justify-between p-4 border border-border rounded-lg hover:bg-muted transition-colors"
              >
                <div className="flex-1">
                  <div className="flex items-center gap-2">
                    <h3 className="font-semibold text-foreground">{item.category}</h3>
                    {item.variance > 0 ? (
                      <CheckCircle className="w-4 h-4 text-accent" />
                    ) : (
                      <AlertCircle className="w-4 h-4 text-primary" />
                    )}
                  </div>
                  <div className="flex gap-6 mt-1 text-sm text-muted-foreground">
                    <span>Allocated: ₱{(item.allocated / 1000).toFixed(0)}K</span>
                    <span>Actual: ₱{(item.actual / 1000).toFixed(0)}K</span>
                  </div>
                </div>
                <div className="text-right">
                  <p className={`font-semibold ${item.variance > 0 ? "text-accent" : "text-primary"}`}>
                    ₱{Math.abs(item.variance / 1000).toFixed(1)}K
                  </p>
                  <p className={`text-xs ${item.variance > 0 ? "text-accent" : "text-primary"}`}>
                    {item.variance > 0 ? "+" : ""}
                    {item.variancePercent.toFixed(1)}%
                  </p>
                </div>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>

      {/* Summary Stats */}
      <div className="grid grid-cols-3 gap-6">
        <Card>
          <CardHeader>
            <CardTitle className="text-sm">Total Allocated</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">₱{(totalAllocated / 1000).toFixed(0)}K</div>
            <p className="text-xs text-muted-foreground mt-1">All categories</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-sm">Total Actual</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">₱{(totalActual / 1000).toFixed(0)}K</div>
            <p className="text-xs text-muted-foreground mt-1">Total spent</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-sm">Total Variance</CardTitle>
          </CardHeader>
          <CardContent>
            <div className={`text-2xl font-bold ${totalActual > totalAllocated ? "text-primary" : "text-accent"}`}>
              ₱{Math.abs(totalActual - totalAllocated) / 1000}K
            </div>
            <p className={`text-xs mt-1 ${totalActual > totalAllocated ? "text-primary" : "text-accent"}`}>
              {totalActual > totalAllocated ? "Over" : "Under"} budget
            </p>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
