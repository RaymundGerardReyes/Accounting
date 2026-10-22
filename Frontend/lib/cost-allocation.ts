/**
 * Cost Allocation Engine
 * Handles various cost allocation methods for manufacturing cost analysis
 */

export interface CostAllocationInput {
  totalCost: number
  allocationBasis: "direct" | "activity-based" | "volume-based" | "machine-hours"
  departments: DepartmentAllocation[]
  activities?: ActivityAllocation[]
}

export interface DepartmentAllocation {
  id: string
  name: string
  directCost?: number
  volume?: number
  machineHours?: number
  laborHours?: number
  weight?: number
}

export interface ActivityAllocation {
  id: string
  name: string
  cost: number
  costDriver: string
  driverValue: number
}

export interface AllocationResult {
  departmentId: string
  departmentName: string
  allocatedCost: number
  allocationPercentage: number
  allocationMethod: string
}

/**
 * Direct Cost Allocation
 * Allocates costs directly based on department's direct costs
 */
export function directCostAllocation(input: CostAllocationInput): AllocationResult[] {
  const departmentsWithDirect = input.departments.filter((d) => d.directCost !== undefined)
  const totalDirect = departmentsWithDirect.reduce((sum, d) => sum + (d.directCost || 0), 0)

  if (totalDirect === 0) {
    throw new Error("No direct costs found for allocation")
  }

  return input.departments.map((dept) => {
    const directCost = dept.directCost || 0
    const percentage = (directCost / totalDirect) * 100
    const allocatedCost = (percentage / 100) * input.totalCost

    return {
      departmentId: dept.id,
      departmentName: dept.name,
      allocatedCost,
      allocationPercentage: percentage,
      allocationMethod: "Direct Cost",
    }
  })
}

/**
 * Volume-Based Allocation
 * Allocates costs based on production volume
 */
export function volumeBasedAllocation(input: CostAllocationInput): AllocationResult[] {
  const departmentsWithVolume = input.departments.filter((d) => d.volume !== undefined && d.volume > 0)
  const totalVolume = departmentsWithVolume.reduce((sum, d) => sum + (d.volume || 0), 0)

  if (totalVolume === 0) {
    throw new Error("No volume data found for allocation")
  }

  return input.departments.map((dept) => {
    const volume = dept.volume || 0
    const percentage = (volume / totalVolume) * 100
    const allocatedCost = (percentage / 100) * input.totalCost

    return {
      departmentId: dept.id,
      departmentName: dept.name,
      allocatedCost,
      allocationPercentage: percentage,
      allocationMethod: "Volume-Based",
    }
  })
}

/**
 * Machine Hours Allocation
 * Allocates costs based on machine hours used
 */
export function machineHoursAllocation(input: CostAllocationInput): AllocationResult[] {
  const departmentsWithHours = input.departments.filter((d) => d.machineHours !== undefined && d.machineHours > 0)
  const totalHours = departmentsWithHours.reduce((sum, d) => sum + (d.machineHours || 0), 0)

  if (totalHours === 0) {
    throw new Error("No machine hours data found for allocation")
  }

  return input.departments.map((dept) => {
    const hours = dept.machineHours || 0
    const percentage = (hours / totalHours) * 100
    const allocatedCost = (percentage / 100) * input.totalCost

    return {
      departmentId: dept.id,
      departmentName: dept.name,
      allocatedCost,
      allocationPercentage: percentage,
      allocationMethod: "Machine Hours",
    }
  })
}

/**
 * Activity-Based Allocation
 * Allocates costs based on specific activities and cost drivers
 */
export function activityBasedAllocation(input: CostAllocationInput): AllocationResult[] {
  if (!input.activities || input.activities.length === 0) {
    throw new Error("No activities defined for activity-based allocation")
  }

  const activityMap = new Map<string, number>()

  // Calculate total cost driver value for each department
  input.activities.forEach((activity) => {
    input.departments.forEach((dept) => {
      const key = dept.id
      const currentValue = activityMap.get(key) || 0
      const costPerUnit = activity.cost / activity.driverValue
      activityMap.set(key, currentValue + costPerUnit * (dept.weight || 0))
    })
  })

  const totalAllocated = Array.from(activityMap.values()).reduce((sum, val) => sum + val, 0)

  return input.departments.map((dept) => {
    const allocatedCost = activityMap.get(dept.id) || 0
    const allocationPercentage = totalAllocated > 0 ? (allocatedCost / totalAllocated) * 100 : 0

    return {
      departmentId: dept.id,
      departmentName: dept.name,
      allocatedCost,
      allocationPercentage,
      allocationMethod: "Activity-Based",
    }
  })
}

/**
 * Main allocation function that routes to appropriate method
 */
export function allocateCosts(input: CostAllocationInput): AllocationResult[] {
  switch (input.allocationBasis) {
    case "direct":
      return directCostAllocation(input)
    case "volume-based":
      return volumeBasedAllocation(input)
    case "machine-hours":
      return machineHoursAllocation(input)
    case "activity-based":
      return activityBasedAllocation(input)
    default:
      throw new Error(`Unknown allocation method: ${input.allocationBasis}`)
  }
}

/**
 * Calculate variance between allocated and actual costs
 */
export interface VarianceAnalysis {
  departmentId: string
  departmentName: string
  allocatedCost: number
  actualCost: number
  variance: number
  variancePercentage: number
  status: "favorable" | "unfavorable" | "neutral"
}

export function calculateVariance(
  allocations: AllocationResult[],
  actualCosts: Map<string, number>,
): VarianceAnalysis[] {
  return allocations.map((allocation) => {
    const actualCost = actualCosts.get(allocation.departmentId) || 0
    const variance = allocation.allocatedCost - actualCost
    const variancePercentage = allocation.allocatedCost > 0 ? (variance / allocation.allocatedCost) * 100 : 0

    let status: "favorable" | "unfavorable" | "neutral" = "neutral"
    if (variance < 0) {
      status = "unfavorable" // Over budget (actual > allocated)
    } else if (variance > 0) {
      status = "favorable" // Under budget (actual < allocated)
    }

    return {
      departmentId: allocation.departmentId,
      departmentName: allocation.departmentName,
      allocatedCost: allocation.allocatedCost,
      actualCost,
      variance,
      variancePercentage,
      status,
    }
  })
}

/**
 * Calculate cost per unit for products
 */
export interface ProductCostAnalysis {
  productId: string
  productName: string
  totalCost: number
  unitVolume: number
  costPerUnit: number
  margin: number
  profitPerUnit: number
}

export function calculateProductCosts(
  products: Array<{
    id: string
    name: string
    allocatedCost: number
    volume: number
    sellingPrice: number
  }>,
): ProductCostAnalysis[] {
  return products.map((product) => {
    const costPerUnit = product.volume > 0 ? product.allocatedCost / product.volume : 0
    const profitPerUnit = product.sellingPrice - costPerUnit
    const margin = product.sellingPrice > 0 ? (profitPerUnit / product.sellingPrice) * 100 : 0

    return {
      productId: product.id,
      productName: product.name,
      totalCost: product.allocatedCost,
      unitVolume: product.volume,
      costPerUnit,
      margin,
      profitPerUnit,
    }
  })
}
