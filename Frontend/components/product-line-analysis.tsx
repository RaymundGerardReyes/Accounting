"use client"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { AlertCircle, ArrowLeft, TrendingUp } from "lucide-react"
import { useEffect, useState } from "react"
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Scatter,
  ScatterChart,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts"

interface ProductLineAnalysisProps {
  onBack: () => void
}

interface ProductData {
  productData: any[]
  profitabilityData: any[]
  trendData: any[]
}

export function ProductLineAnalysis({ onBack }: ProductLineAnalysisProps) {
  const [data, setData] = useState<ProductData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const fetchProductData = async () => {
      try {
        const response = await fetch("http://localhost:5000/api/products")
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

    fetchProductData()
  }, [])

  if (loading) {
    return <div className="p-8 text-center">Loading product analysis...</div>
  }

  if (error) {
    return <div className="p-8 text-center text-red-500">Failed to load data: {error}</div>
  }

  if (!data) {
    return <div className="p-8 text-center">No product data available.</div>
  }

  const { productData, profitabilityData, trendData } = data

  const totalVolume = productData.reduce((sum, p) => sum + p.volume, 0)
  const totalProfit = productData.reduce((sum, p) => sum + p.profit, 0)
  const avgMargin = (productData.reduce((sum, p) => sum + p.margin, 0) / productData.length).toFixed(1)
  const avgUnitCost = (productData.reduce((sum, p) => sum + p.unitCost, 0) / productData.length).toFixed(0)

  const getStatusIcon = (status: string) => {
    if (status === "growing") return <TrendingUp className="w-4 h-4 text-accent" />
    if (status === "declining") return <AlertCircle className="w-4 h-4 text-primary" />
    return null
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
            <h1 className="text-4xl font-bold text-foreground">Product Line Analysis</h1>
            <p className="text-muted-foreground mt-2">Comprehensive unit cost and profitability analysis</p>
          </div>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-4 gap-4">
        <Card className="border-l-4 border-l-accent">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Avg Unit Cost</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">₱{avgUnitCost}</div>
            <p className="text-xs text-muted-foreground mt-1">Across all products</p>
          </CardContent>
        </Card>

        <Card className="border-l-4 border-l-primary">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Volume</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">{(totalVolume / 1000).toFixed(1)}K</div>
            <p className="text-xs text-muted-foreground mt-1">Units produced</p>
          </CardContent>
        </Card>

        <Card className="border-l-4 border-l-accent">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Avg Margin</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">{avgMargin}%</div>
            <p className="text-xs text-muted-foreground mt-1">Gross margin</p>
          </CardContent>
        </Card>

        <Card className="border-l-4 border-l-primary">
          <CardHeader className="pb-3">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Profit</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">₱{(totalProfit / 1000).toFixed(0)}K</div>
            <p className="text-xs text-muted-foreground mt-1">All products</p>
          </CardContent>
        </Card>
      </div>

      {/* Charts */}
      <div className="grid grid-cols-2 gap-6">
        {/* Unit Cost vs Volume */}
        <Card>
          <CardHeader>
            <CardTitle>Unit Cost vs Volume</CardTitle>
            <CardDescription>Product positioning and scale</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={300}>
              <ScatterChart margin={{ top: 20, right: 20, bottom: 20, left: 20 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--color-border))" />
                <XAxis dataKey="unitCost" name="Unit Cost" stroke="hsl(var(--color-muted-foreground))" />
                <YAxis dataKey="volume" name="Volume" stroke="hsl(var(--color-muted-foreground))" />
                <Tooltip
                  contentStyle={{
                    backgroundColor: "hsl(var(--color-card))",
                    border: "1px solid hsl(var(--color-border))",
                  }}
                  labelStyle={{ color: "hsl(var(--color-foreground))" }}
                  cursor={{ strokeDasharray: "3 3" }}
                />
                <Scatter name="Products" data={productData} fill="hsl(var(--color-chart-1))" />
              </ScatterChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        {/* Profitability */}
        <Card>
          <CardHeader>
            <CardTitle>Profitability Analysis</CardTitle>
            <CardDescription>Revenue vs Cost by product</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={300}>
              <BarChart data={profitabilityData}>
                <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--color-border))" />
                <XAxis dataKey="product" stroke="hsl(var(--color-muted-foreground))" />
                <YAxis stroke="hsl(var(--color-muted-foreground))" />
                <Tooltip
                  contentStyle={{
                    backgroundColor: "hsl(var(--color-card))",
                    border: "1px solid hsl(var(--color-border))",
                  }}
                  labelStyle={{ color: "hsl(var(--color-foreground))" }}
                />
                <Legend />
                <Bar dataKey="revenue" fill="hsl(var(--color-chart-1))" name="Revenue" />
                <Bar dataKey="cost" fill="hsl(var(--color-chart-2))" name="Cost" />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      {/* Unit Cost Trend */}
      <Card>
        <CardHeader>
          <CardTitle>Unit Cost Trend</CardTitle>
          <CardDescription>Historical unit cost movement by product</CardDescription>
        </CardHeader>
        <CardContent>
          <ResponsiveContainer width="100%" height={300}>
            <LineChart data={trendData}>
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
              <Line type="monotone" dataKey="productA" stroke="hsl(var(--color-chart-1))" strokeWidth={2} />
              <Line type="monotone" dataKey="productB" stroke="hsl(var(--color-chart-2))" strokeWidth={2} />
              <Line type="monotone" dataKey="productC" stroke="hsl(var(--color-chart-3))" strokeWidth={2} />
              <Line type="monotone" dataKey="productD" stroke="hsl(var(--color-chart-4))" strokeWidth={2} />
              <Line type="monotone" dataKey="productE" stroke="hsl(var(--color-chart-5))" strokeWidth={2} />
            </LineChart>
          </ResponsiveContainer>
        </CardContent>
      </Card>

      {/* Product Details Table */}
      <Card>
        <CardHeader>
          <CardTitle>Product Details</CardTitle>
          <CardDescription>Unit cost, margin, and profitability analysis</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="space-y-3">
            {productData.map((product) => (
              <div
                key={product.product}
                className="flex items-center justify-between p-4 border border-border rounded-lg hover:bg-muted transition-colors"
              >
                <div className="flex-1">
                  <div className="flex items-center gap-2">
                    <h3 className="font-semibold text-foreground">{product.product}</h3>
                    {getStatusIcon(product.status)}
                  </div>
                  <div className="flex gap-6 mt-1 text-sm text-muted-foreground">
                    <span>Volume: {product.volume.toLocaleString()} units</span>
                    <span>Revenue: ₱{(product.revenue / 1000).toFixed(0)}K</span>
                  </div>
                </div>
                <div className="flex items-center gap-8">
                  <div className="text-right">
                    <p className="font-semibold text-foreground">₱{product.unitCost}</p>
                    <p className="text-xs text-muted-foreground">Unit Cost</p>
                  </div>
                  <div className="text-right">
                    <p className="font-semibold text-accent">{product.margin}%</p>
                    <p className="text-xs text-muted-foreground">Margin</p>
                  </div>
                  <div className="text-right">
                    <p className="font-semibold text-foreground">₱{(product.profit / 1000).toFixed(0)}K</p>
                    <p className="text-xs text-muted-foreground">Profit</p>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

