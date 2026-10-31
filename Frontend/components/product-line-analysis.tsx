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

export interface ProductItem {
  product: string
  unitCost: number
  volume: number
  margin: number
  revenue: number
  profit: number
  status: string
}

export interface ProductDataPayload {
  productData?: any[]
  profitabilityData?: any[]
  trendData?: any[]
  products?: any[]
}

export interface ProductLineAnalysisProps {
  onBack: () => void
  initialData?: ProductDataPayload | any[]
}

export function ProductLineAnalysis({ onBack, initialData }: ProductLineAnalysisProps) {
  const [data, setData] = useState<any | null>(initialData ?? null)
  const [loading, setLoading] = useState(initialData === undefined)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (initialData !== undefined) {
      setData(initialData)
      setLoading(false)
      return
    }

    const fetchProductData = async () => {
      try {
        const response = await fetch("/api/products")
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
  }, [initialData])

  if (loading) {
    return <div className="p-8 text-center text-sm text-muted-foreground">Loading product analysis...</div>
  }

  if (error) {
    return (
      <div className="p-8 max-w-7xl mx-auto">
        <div className="p-4 rounded-lg border border-accent/30 bg-accent/5 text-accent text-sm flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>Failed to load product data: {error}</span>
        </div>
      </div>
    )
  }

  if (!data) {
    return <div className="p-8 text-center text-sm text-muted-foreground">No product data available.</div>
  }

  // Handle both array responses (from .NET) and object responses (from Python / JSON)
  const rawProductList: any[] = Array.isArray(data)
    ? data
    : Array.isArray((data as any)?.productData)
      ? (data as any).productData
      : Array.isArray((data as any)?.products)
        ? (data as any).products
        : []

  const productData: ProductItem[] = rawProductList.map((p: any, idx: number) => {
    const name = p?.product ?? p?.name ?? `Product ${idx + 1}`
    const unitCost = Number(p?.unitCost ?? (p?.unitDirectCostMinor != null ? p.unitDirectCostMinor / 100 : 0)) || 0
    const volume = Number(p?.volume ?? p?.unitsProduced ?? 0) || 0
    const revenue = Number(p?.revenue ?? (unitCost * volume * 1.3)) || 0
    const profit = Number(p?.profit ?? (revenue - unitCost * volume)) || 0
    const margin = Number(p?.margin ?? (revenue > 0 ? Math.round((profit / revenue) * 100) : 0)) || 0
    const status = String(p?.status ?? (margin >= 30 ? "growing" : margin < 20 ? "declining" : "stable"))
    return {
      product: String(name),
      unitCost,
      volume,
      margin,
      revenue,
      profit,
      status,
    }
  })

  const profitabilityData: any[] = Array.isArray((data as any)?.profitabilityData)
    ? (data as any).profitabilityData
    : productData.map((p) => ({
        product: p.product,
        revenue: p.revenue,
        cost: Math.max(0, p.revenue - p.profit),
        profit: p.profit,
      }))

  const trendData: any[] = Array.isArray((data as any)?.trendData)
    ? (data as any).trendData
    : []

  const totalVolume = productData.reduce((sum, p) => sum + (p?.volume || 0), 0)
  const totalProfit = productData.reduce((sum, p) => sum + (p?.profit || 0), 0)
  const productCount = productData.length
  const avgMargin = productCount > 0
    ? (productData.reduce((sum, p) => sum + (p?.margin || 0), 0) / productCount).toFixed(1)
    : "0.0"
  const avgUnitCost = productCount > 0
    ? (productData.reduce((sum, p) => sum + (p?.unitCost || 0), 0) / productCount).toFixed(0)
    : "0"

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
              Unit Economics
            </p>
            <h1 className="text-3xl font-bold tracking-tight text-foreground mt-0.5">
              Product Line Analysis
            </h1>
          </div>
        </div>
      </div>

      {/* KPI Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Avg Unit Cost
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              ₱{avgUnitCost}
            </div>
            <p className="text-xs text-muted-foreground mt-1">Across all active SKUs</p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Total Volume
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              {(totalVolume / 1000).toFixed(1)}K
            </div>
            <p className="text-xs text-muted-foreground mt-1">Units manufactured</p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Avg Gross Margin
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              {avgMargin}%
            </div>
            <p className="text-xs text-muted-foreground mt-1">Weighted product margin</p>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
              Total Net Profit
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono tabular-nums text-foreground">
              ₱{(totalProfit / 1000).toFixed(0)}K
            </div>
            <p className="text-xs text-muted-foreground mt-1">Cumulative product lines</p>
          </CardContent>
        </Card>
      </div>

      {/* Charts */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-base font-semibold">Unit Cost vs. Volume</CardTitle>
            <CardDescription className="text-xs">Production scale positioning</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={280}>
              <ScatterChart margin={{ top: 15, right: 15, bottom: 10, left: -10 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
                <XAxis dataKey="unitCost" name="Unit Cost (₱)" stroke="var(--muted-foreground)" fontSize={12} tickLine={false} axisLine={false} />
                <YAxis dataKey="volume" name="Volume" stroke="var(--muted-foreground)" fontSize={12} tickLine={false} axisLine={false} />
                <Tooltip
                  contentStyle={{
                    backgroundColor: "var(--card)",
                    border: "1px solid var(--border)",
                    borderRadius: "var(--radius)",
                    fontSize: "12px",
                  }}
                  cursor={{ strokeDasharray: "3 3" }}
                />
                <Scatter name="Products" data={productData} fill="var(--chart-2)" />
              </ScatterChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        <Card className="shadow-none border-border/90">
          <CardHeader className="pb-2">
            <CardTitle className="text-base font-semibold">Profitability Analysis</CardTitle>
            <CardDescription className="text-xs">Revenue (Black) vs. Direct Cost (Red)</CardDescription>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={280}>
              <BarChart data={profitabilityData} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="var(--border)" />
                <XAxis dataKey="product" stroke="var(--muted-foreground)" fontSize={12} tickLine={false} axisLine={false} />
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
                <Bar dataKey="revenue" fill="var(--chart-1)" radius={[4, 4, 0, 0]} name="Revenue (₱)" />
                <Bar dataKey="cost" fill="var(--chart-2)" radius={[4, 4, 0, 0]} name="Cost (₱)" />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      {/* Historical Unit Cost Trend */}
      <Card className="shadow-none border-border/90">
        <CardHeader className="pb-2">
          <CardTitle className="text-base font-semibold">Historical Unit Cost Movement</CardTitle>
          <CardDescription className="text-xs">Monthly unit cost trajectory across Product A–E</CardDescription>
        </CardHeader>
        <CardContent>
          <ResponsiveContainer width="100%" height={260}>
            <LineChart data={trendData} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
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
              <Line type="monotone" dataKey="productA" stroke="var(--chart-1)" strokeWidth={2} dot={false} name="Product A" />
              <Line type="monotone" dataKey="productB" stroke="var(--chart-2)" strokeWidth={2} dot={false} name="Product B" />
              <Line type="monotone" dataKey="productC" stroke="var(--chart-3)" strokeWidth={2} dot={false} name="Product C" />
              <Line type="monotone" dataKey="productD" stroke="var(--chart-4)" strokeWidth={2} dot={false} name="Product D" />
              <Line type="monotone" dataKey="productE" stroke="var(--chart-5)" strokeWidth={2} dot={false} name="Product E" />
            </LineChart>
          </ResponsiveContainer>
        </CardContent>
      </Card>

      {/* Product Details Table */}
      <Card className="shadow-none border-border/90 overflow-hidden">
        <CardHeader className="border-b border-border/60 pb-4">
          <CardTitle className="text-base font-semibold">Product Line Ledger</CardTitle>
          <CardDescription className="text-xs">Unit cost, gross margin, and net contribution</CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          {productData.length === 0 ? (
            <div className="p-8 text-center text-sm text-muted-foreground">
              No active product lines found.
            </div>
          ) : (
            <div className="divide-y divide-border/70">
              {productData.map((product, idx) => (
                <div
                  key={`${product.product}-${idx}`}
                  className="flex items-center justify-between px-6 py-4 hover:bg-muted/30 transition-colors"
                >
                  <div className="space-y-1">
                    <div className="flex items-center gap-2.5">
                      <span className="font-semibold text-sm text-foreground">{product.product}</span>
                      {product.status === "growing" && (
                        <span className="inline-flex items-center gap-1 text-[10px] font-mono uppercase px-2 py-0.5 rounded-full bg-muted text-foreground">
                          <TrendingUp className="w-3 h-3" /> Growing
                        </span>
                      )}
                      {product.status === "declining" && (
                        <span className="inline-flex items-center gap-1 text-[10px] font-mono uppercase px-2 py-0.5 rounded-full bg-accent/10 text-accent border border-accent/20">
                          <AlertCircle className="w-3 h-3" /> Declining
                        </span>
                      )}
                    </div>
                    <div className="flex gap-6 text-xs text-muted-foreground font-mono tabular-nums">
                      <span>Volume: {(product.volume || 0).toLocaleString()} units</span>
                      <span>Revenue: ₱{((product.revenue || 0) / 1000).toFixed(0)}K</span>
                    </div>
                  </div>
                  <div className="flex items-center gap-8 font-mono tabular-nums">
                    <div className="text-right">
                      <p className="text-sm font-semibold text-foreground">₱{product.unitCost ?? 0}</p>
                      <p className="text-[11px] text-muted-foreground">Unit Cost</p>
                    </div>
                    <div className="text-right">
                      <p className="text-sm font-semibold text-foreground">{product.margin ?? 0}%</p>
                      <p className="text-[11px] text-muted-foreground">Margin</p>
                    </div>
                    <div className="text-right">
                      <p className="text-sm font-bold text-foreground">₱{((product.profit || 0) / 1000).toFixed(0)}K</p>
                      <p className="text-[11px] text-muted-foreground">Profit</p>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

