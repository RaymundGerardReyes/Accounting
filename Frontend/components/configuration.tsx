"use client"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import {
  AlertCircle,
  ArrowLeft,
  Bell,
  CreditCard,
  CheckCircle2,
  Database,
  Edit2,
  Plus,
  Save,
  Shield,
  Trash2,
  X,
} from "lucide-react"
import { useEffect, useState } from "react"

interface ConfigurationProps {
  onBack: () => void
}

interface Department {
  id: string
  name: string
  costCenter: string
  manager: string
  budget: number
}

interface User {
  id: number
  name: string
  role: string
  email: string
  status: string
}

interface ConfigData {
  config: any
  departments: Department[]
  users: User[]
  auditLogs: any[]
  integrations: any[]
}

export function Configuration({ onBack }: ConfigurationProps) {
  const [data, setData] = useState<ConfigData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [saveStatus, setSaveStatus] = useState<string | null>(null)

  const [config, setConfig] = useState<any>(null)
  const [departments, setDepartments] = useState<Department[]>([])
  const [users, setUsers] = useState<User[]>([])

  const [activeTab, setActiveTab] = useState<"general" | "departments" | "users" | "audit" | "integrations">("general")
  const [showAddDeptModal, setShowAddDeptModal] = useState(false)
  const [showEditDeptModal, setShowEditDeptModal] = useState(false)
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false)
  const [selectedDept, setSelectedDept] = useState<Department | null>(null)
  const [deptForm, setDeptForm] = useState({ name: "", costCenter: "", manager: "", budget: "" })

  const [showAddUserModal, setShowAddUserModal] = useState(false)
  const [showEditUserModal, setShowEditUserModal] = useState(false)
  const [showDeleteUserConfirm, setShowDeleteUserConfirm] = useState(false)
  const [selectedUser, setSelectedUser] = useState<User | null>(null)
  const [userForm, setUserForm] = useState({ name: "", email: "", role: "Cost Analyst", status: "Active" })

  useEffect(() => {
    const fetchConfigData = async () => {
      try {
        const response = await fetch("/api/configuration")
        if (!response.ok) {
          throw new Error(`HTTP error! status: ${response.status}`)
        }
        const result = await response.json()
        setData(result)
        setConfig(result.config)
        setDepartments(result.departments || [])
        setUsers(result.users || [])
      } catch (e: any) {
        setError(e.message)
      } finally {
        setLoading(false)
      }
    }

    fetchConfigData()
  }, [])

  const handleSaveConfig = async () => {
    try {
      const response = await fetch("/api/configuration", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ config }),
      })
      if (!response.ok) throw new Error("Failed to save configuration")
      setSaveStatus("System configuration saved to server.")
      setTimeout(() => setSaveStatus(null), 3500)
    } catch (e: any) {
      setSaveStatus(`Error saving settings: ${e.message}`)
    }
  }

  const handleAddDept = async () => {
    if (deptForm.name && deptForm.costCenter && deptForm.manager && deptForm.budget) {
      const newDept: Department = {
        id: deptForm.name.toLowerCase().replace(/\s+/g, "-"),
        name: deptForm.name,
        costCenter: deptForm.costCenter,
        manager: deptForm.manager,
        budget: Number.parseInt(deptForm.budget),
      }
      const updated = [...departments, newDept]
      setDepartments(updated)
      setDeptForm({ name: "", costCenter: "", manager: "", budget: "" })
      setShowAddDeptModal(false)
      try {
        await fetch("/api/configuration/departments", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ departments: updated }),
        })
      } catch (err) {
        console.error("Failed to persist department:", err)
      }
    }
  }

  const handleEditDept = async () => {
    if (selectedDept && deptForm.name && deptForm.costCenter && deptForm.manager && deptForm.budget) {
      const updated = departments.map((d) =>
        d.id === selectedDept.id
          ? {
              ...d,
              name: deptForm.name,
              costCenter: deptForm.costCenter,
              manager: deptForm.manager,
              budget: Number.parseInt(deptForm.budget),
            }
          : d
      )
      setDepartments(updated)
      setDeptForm({ name: "", costCenter: "", manager: "", budget: "" })
      setShowEditDeptModal(false)
      setSelectedDept(null)
      try {
        await fetch("/api/configuration/departments", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ departments: updated }),
        })
      } catch (err) {
        console.error("Failed to persist department edits:", err)
      }
    }
  }

  const handleDeleteDept = async () => {
    if (selectedDept) {
      const updated = departments.filter((d) => d.id !== selectedDept.id)
      setDepartments(updated)
      setShowDeleteConfirm(false)
      setSelectedDept(null)
      try {
        await fetch("/api/configuration/departments", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ departments: updated }),
        })
      } catch (err) {
        console.error("Failed to persist department deletion:", err)
      }
    }
  }

  const handleAddUser = async () => {
    if (userForm.name && userForm.email) {
      const newUser: User = {
        id: users.length > 0 ? Math.max(...users.map((u) => u.id)) + 1 : 1,
        name: userForm.name,
        email: userForm.email,
        role: userForm.role,
        status: userForm.status,
      }
      const updated = [...users, newUser]
      setUsers(updated)
      setUserForm({ name: "", email: "", role: "Cost Analyst", status: "Active" })
      setShowAddUserModal(false)
      try {
        await fetch("/api/configuration/users", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ users: updated }),
        })
      } catch (err) {
        console.error("Failed to persist user:", err)
      }
    }
  }

  const handleEditUser = async () => {
    if (selectedUser && userForm.name && userForm.email) {
      const updated = users.map((u) =>
        u.id === selectedUser.id
          ? {
              ...u,
              name: userForm.name,
              email: userForm.email,
              role: userForm.role,
              status: userForm.status,
            }
          : u
      )
      setUsers(updated)
      setUserForm({ name: "", email: "", role: "Cost Analyst", status: "Active" })
      setShowEditUserModal(false)
      setSelectedUser(null)
      try {
        await fetch("/api/configuration/users", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ users: updated }),
        })
      } catch (err) {
        console.error("Failed to persist user edits:", err)
      }
    }
  }

  const handleDeleteUser = async () => {
    if (selectedUser) {
      const updated = users.filter((u) => u.id !== selectedUser.id)
      setUsers(updated)
      setShowDeleteUserConfirm(false)
      setSelectedUser(null)
      try {
        await fetch("/api/configuration/users", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ users: updated }),
        })
      } catch (err) {
        console.error("Failed to persist user deletion:", err)
      }
    }
  }


  const openEditModal = (dept: Department) => {
    setSelectedDept(dept)
    setDeptForm({
      name: dept.name,
      costCenter: dept.costCenter,
      manager: dept.manager,
      budget: dept.budget.toString(),
    })
    setShowEditDeptModal(true)
  }

  const openDeleteConfirm = (dept: Department) => {
    setSelectedDept(dept)
    setShowDeleteConfirm(true)
  }

  if (loading) {
    return <div className="p-8 text-center text-sm text-muted-foreground">Loading configuration...</div>
  }

  if (error) {
    return (
      <div className="p-8 max-w-7xl mx-auto">
        <div className="p-4 rounded-lg border border-accent/30 bg-accent/5 text-accent text-sm flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>Failed to load configuration: {error}</span>
        </div>
      </div>
    )
  }

  if (!data || !config) {
    return <div className="p-8 text-center text-sm text-muted-foreground">No configuration data available.</div>
  }

  const { auditLogs = [], integrations = [] } = data

  const getIntegrationIcon = (name: string) => {
    switch (name) {
      case "PayMongo Payment Gateway":
        return <CreditCard className="w-5 h-5 text-accent" />
      case "Database":
        return <Database className="w-5 h-5 text-foreground" />
      case "Email Notifications":
        return <Bell className="w-5 h-5 text-foreground" />
      case "API Access":
        return <Shield className="w-5 h-5 text-foreground" />
      default:
        return null
    }
  }

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
              System Preferences
            </p>
            <h1 className="text-3xl font-bold tracking-tight text-foreground mt-0.5">
              Configuration & Administration
            </h1>
          </div>
        </div>
      </div>

      {/* Minimalist Tab Navigation */}
      <div className="flex gap-6 border-b border-border">
        {(["general", "departments", "users", "audit", "integrations"] as const).map((tab) => {
          const isActive = activeTab === tab
          return (
            <button
              key={tab}
              type="button"
              onClick={() => setActiveTab(tab)}
              className={`pb-3 font-medium text-xs uppercase tracking-wider transition-colors border-b-2 -mb-px ${
                isActive
                  ? "border-accent text-foreground font-semibold"
                  : "border-transparent text-muted-foreground hover:text-foreground"
              }`}
            >
              {tab}
            </button>
          )
        })}
      </div>

      {/* General Settings Tab */}
      {activeTab === "general" && (
        <Card className="shadow-none border-border/90">
          <CardHeader className="border-b border-border/60 pb-4">
            <CardTitle className="text-base font-semibold">System Parameters</CardTitle>
            <CardDescription className="text-xs">Configure core accounting rules and reporting defaults</CardDescription>
          </CardHeader>
          <CardContent className="space-y-6 pt-6">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-6">
              <div>
                <label className="block text-xs font-semibold text-foreground mb-2">Company Name</label>
                <input
                  type="text"
                  value={config.companyName}
                  onChange={(e) => setConfig({ ...config, companyName: e.target.value })}
                  className="w-full px-3.5 py-2 text-sm border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-foreground mb-2">Fiscal Year</label>
                <input
                  type="text"
                  value={config.fiscalYear}
                  onChange={(e) => setConfig({ ...config, fiscalYear: e.target.value })}
                  className="w-full px-3.5 py-2 text-sm font-mono border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-foreground mb-2">Default Allocation Basis</label>
                <select
                  value={config.allocationMethod}
                  onChange={(e) => setConfig({ ...config, allocationMethod: e.target.value })}
                  className="w-full px-3.5 py-2 text-sm border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                >
                  <option value="activity-based">Activity-Based Costing (ABC)</option>
                  <option value="volume-based">Volume-Based</option>
                  <option value="direct">Direct Cost</option>
                  <option value="machine-hours">Machine Hours</option>
                </select>
              </div>
              <div>
                <label className="block text-xs font-semibold text-foreground mb-2">Reporting Currency</label>
                <select
                  value={config.reportingCurrency}
                  onChange={(e) => setConfig({ ...config, reportingCurrency: e.target.value })}
                  className="w-full px-3.5 py-2 text-sm border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                >
                  <option value="PHP">PHP (₱)</option>
                  <option value="USD">USD ($)</option>
                  <option value="EUR">EUR (€)</option>
                  <option value="GBP">GBP (£)</option>
                  <option value="JPY">JPY (¥)</option>
                </select>
              </div>
              <div>
                <label className="block text-xs font-semibold text-foreground mb-2">Decimal Precision</label>
                <input
                  type="number"
                  min="0"
                  max="4"
                  value={config.decimalPlaces}
                  onChange={(e) => setConfig({ ...config, decimalPlaces: Number.parseInt(e.target.value) })}
                  className="w-full px-3.5 py-2 text-sm font-mono border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-foreground mb-2">Notification Email</label>
                <input
                  type="email"
                  value={config.notificationEmail}
                  onChange={(e) => setConfig({ ...config, notificationEmail: e.target.value })}
                  className="w-full px-3.5 py-2 text-sm border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                />
              </div>
            </div>

            <div className="pt-2">
              <label className="inline-flex items-center gap-2.5 cursor-pointer">
                <input
                  type="checkbox"
                  checked={config.autoBackup}
                  onChange={(e) => setConfig({ ...config, autoBackup: e.target.checked })}
                  className="w-4 h-4 rounded border-border accent-primary"
                />
                <span className="text-xs font-medium text-foreground">
                  Enable automated daily ledger snapshots
                </span>
              </label>
            </div>

            {saveStatus && (
              <div className="p-3 rounded-md bg-muted border border-border text-xs text-foreground flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 text-accent shrink-0" />
                <span>{saveStatus}</span>
              </div>
            )}

            <Button onClick={handleSaveConfig} className="bg-primary text-primary-foreground hover:bg-primary/90 gap-2">
              <Save className="w-4 h-4" />
              <span>Save Settings</span>
            </Button>
          </CardContent>
        </Card>
      )}

      {/* Departments Tab */}
      {activeTab === "departments" && (
        <Card className="shadow-none border-border/90 overflow-hidden">
          <CardHeader className="flex flex-row items-center justify-between border-b border-border/60 pb-4">
            <div>
              <CardTitle className="text-base font-semibold">Cost Center Departments</CardTitle>
              <CardDescription className="text-xs">Manage active manufacturing cost centers and budgets</CardDescription>
            </div>
            <Button
              size="sm"
              className="gap-2 bg-primary text-primary-foreground hover:bg-primary/90"
              onClick={() => {
                setDeptForm({ name: "", costCenter: "", manager: "", budget: "" })
                setShowAddDeptModal(true)
              }}
            >
              <Plus className="w-3.5 h-3.5" />
              <span>Add Department</span>
            </Button>
          </CardHeader>
          <CardContent className="p-0">
            <div className="divide-y divide-border/70">
              {departments.map((dept) => (
                <div
                  key={dept.id}
                  className="flex items-center justify-between px-6 py-4 hover:bg-muted/30 transition-colors group"
                >
                  <div className="space-y-1">
                    <h3 className="font-semibold text-sm text-foreground">{dept.name}</h3>
                    <div className="flex gap-6 text-xs text-muted-foreground font-mono tabular-nums">
                      <span>Code: {dept.costCenter}</span>
                      <span>Manager: {dept.manager}</span>
                      <span>Budget: ₱{(dept.budget / 1000).toFixed(0)}K</span>
                    </div>
                  </div>
                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      className="h-8 gap-1.5 text-xs"
                      onClick={() => openEditModal(dept)}
                    >
                      <Edit2 className="w-3.5 h-3.5" />
                      <span>Edit</span>
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      className="h-8 gap-1.5 text-xs text-accent border-accent/30 hover:bg-accent/10"
                      onClick={() => openDeleteConfirm(dept)}
                    >
                      <Trash2 className="w-3.5 h-3.5" />
                      <span>Delete</span>
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Users Tab */}
      {activeTab === "users" && (
        <Card className="shadow-none border-border/90 overflow-hidden">
          <CardHeader className="flex flex-row items-center justify-between border-b border-border/60 pb-4">
            <div>
              <CardTitle className="text-base font-semibold">User Access Control</CardTitle>
              <CardDescription className="text-xs">Manage platform operators and role permissions</CardDescription>
            </div>
            <Button
              className="bg-primary text-primary-foreground hover:bg-primary/90 text-xs gap-1.5 shadow-xs"
              onClick={() => {
                setUserForm({ name: "", email: "", role: "Cost Analyst", status: "Active" })
                setShowAddUserModal(true)
              }}
            >
              <Plus className="w-3.5 h-3.5" />
              Add User
            </Button>
          </CardHeader>
          <CardContent className="p-0">
            <div className="divide-y divide-border/70">
              {users.map((user) => (
                <div
                  key={user.id}
                  className="flex items-center justify-between px-6 py-4 hover:bg-muted/30 transition-colors"
                >
                  <div className="space-y-1">
                    <h3 className="font-semibold text-sm text-foreground">{user.name}</h3>
                    <div className="flex gap-6 text-xs text-muted-foreground font-mono">
                      <span>{user.email}</span>
                      <span>Role: {user.role}</span>
                    </div>
                  </div>
                  <div className="flex items-center gap-3">
                    <span
                      className={`text-[11px] font-mono px-2.5 py-0.5 rounded-full font-semibold ${
                        user.status === "Active"
                          ? "bg-primary text-primary-foreground"
                          : "bg-muted text-muted-foreground"
                      }`}
                    >
                      {user.status}
                    </span>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Edit ${user.name}`}
                      onClick={() => {
                        setSelectedUser(user)
                        setUserForm({
                          name: user.name,
                          email: user.email,
                          role: user.role,
                          status: user.status,
                        })
                        setShowEditUserModal(true)
                      }}
                    >
                      <Edit2 className="w-3.5 h-3.5" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Delete ${user.name}`}
                      onClick={() => {
                        setSelectedUser(user)
                        setShowDeleteUserConfirm(true)
                      }}
                    >
                      <Trash2 className="w-3.5 h-3.5 text-accent" />
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Audit Trail Tab */}
      {activeTab === "audit" && (
        <Card className="shadow-none border-border/90 overflow-hidden">
          <CardHeader className="border-b border-border/60 pb-4">
            <CardTitle className="text-base font-semibold">System Audit Trail</CardTitle>
            <CardDescription className="text-xs">Immutable activity and webhook settlement log</CardDescription>
          </CardHeader>
          <CardContent className="p-0">
            <div className="divide-y divide-border/70">
              {auditLogs.map((log) => (
                <div
                  key={log.id}
                  className="flex items-center justify-between px-6 py-4 hover:bg-muted/30 transition-colors"
                >
                  <div className="space-y-1">
                    <div className="flex items-center gap-2.5">
                      <span className="font-semibold text-sm text-foreground">{log.action}</span>
                      <span
                        className={`text-[10px] font-mono uppercase px-2 py-0.5 rounded-full font-medium ${
                          log.severity === "warning"
                            ? "bg-accent/10 text-accent border border-accent/20"
                            : "bg-muted text-foreground"
                        }`}
                      >
                        {log.severity}
                      </span>
                    </div>
                    <p className="text-xs text-muted-foreground">Operator: {log.user}</p>
                  </div>
                  <span className="text-xs font-mono text-muted-foreground">{log.time}</span>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Integrations Tab */}
      {activeTab === "integrations" && (
        <Card className="shadow-none border-border/90 overflow-hidden">
          <CardHeader className="border-b border-border/60 pb-4">
            <CardTitle className="text-base font-semibold">Connected Services</CardTitle>
            <CardDescription className="text-xs">External gateways, databases, and webhook endpoints</CardDescription>
          </CardHeader>
          <CardContent className="p-0">
            <div className="divide-y divide-border/70">
              {integrations.map((integration) => (
                <div
                  key={integration.name}
                  className="flex items-center justify-between px-6 py-4 hover:bg-muted/30 transition-colors"
                >
                  <div className="flex items-center gap-4">
                    <div className="w-9 h-9 rounded-md bg-muted flex items-center justify-center">
                      {getIntegrationIcon(integration.name)}
                    </div>
                    <div>
                      <h3 className="font-semibold text-sm text-foreground">{integration.name}</h3>
                      {integration.lastSync !== "N/A" && (
                        <p className="text-xs text-muted-foreground font-mono mt-0.5">
                          Last synced: {integration.lastSync}
                        </p>
                      )}
                    </div>
                  </div>
                  <div className="flex items-center gap-4">
                    <span className="text-xs font-mono font-semibold text-foreground">{integration.status}</span>
                    <Button variant="outline" size="sm" className="h-8 text-xs">
                      Configure
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Add / Edit Department Modal */}
      {(showAddDeptModal || (showEditDeptModal && selectedDept)) && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-md border-border shadow-xl">
            <CardHeader className="flex flex-row items-center justify-between border-b border-border/60 pb-4">
              <CardTitle className="text-base font-semibold">
                {showAddDeptModal ? "Add New Department" : "Edit Department"}
              </CardTitle>
              <Button
                variant="ghost"
                size="icon-sm"
                onClick={() => {
                  setShowAddDeptModal(false)
                  setShowEditDeptModal(false)
                }}
              >
                <X className="w-4 h-4" />
              </Button>
            </CardHeader>
            <CardContent className="space-y-4 pt-4">
              <div>
                <label className="block text-xs font-semibold text-foreground mb-1.5">Department Name</label>
                <input
                  type="text"
                  value={deptForm.name}
                  onChange={(e) => setDeptForm({ ...deptForm, name: e.target.value })}
                  placeholder="e.g., Manufacturing"
                  className="w-full px-3 py-2 text-sm border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-foreground mb-1.5">Cost Center Code</label>
                <input
                  type="text"
                  value={deptForm.costCenter}
                  onChange={(e) => setDeptForm({ ...deptForm, costCenter: e.target.value })}
                  placeholder="e.g., CC-001"
                  className="w-full px-3 py-2 text-sm font-mono border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-foreground mb-1.5">Manager</label>
                <input
                  type="text"
                  value={deptForm.manager}
                  onChange={(e) => setDeptForm({ ...deptForm, manager: e.target.value })}
                  placeholder="e.g., John Smith"
                  className="w-full px-3 py-2 text-sm border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-foreground mb-1.5">Budget (PHP ₱)</label>
                <input
                  type="number"
                  value={deptForm.budget}
                  onChange={(e) => setDeptForm({ ...deptForm, budget: e.target.value })}
                  placeholder="e.g., 450000"
                  className="w-full px-3 py-2 text-sm font-mono border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                />
              </div>
              <div className="flex gap-2 justify-end pt-3">
                <Button
                  variant="outline"
                  onClick={() => {
                    setShowAddDeptModal(false)
                    setShowEditDeptModal(false)
                  }}
                >
                  Cancel
                </Button>
                <Button
                  className="bg-primary text-primary-foreground hover:bg-primary/90"
                  onClick={showAddDeptModal ? handleAddDept : handleEditDept}
                >
                  {showAddDeptModal ? "Add Department" : "Save Changes"}
                </Button>
              </div>
            </CardContent>
          </Card>
        </div>
      )}


      {/* Add / Edit User Modal */}
      {(showAddUserModal || showEditUserModal) && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-md border-border shadow-xl">
            <CardHeader className="flex flex-row items-center justify-between border-b border-border/60 pb-4">
              <CardTitle className="text-base font-semibold">
                {showAddUserModal ? "Add New User" : "Edit User"}
              </CardTitle>
              <Button
                variant="ghost"
                size="icon-sm"
                onClick={() => {
                  setShowAddUserModal(false)
                  setShowEditUserModal(false)
                }}
              >
                <X className="w-4 h-4" />
              </Button>
            </CardHeader>
            <CardContent className="space-y-4 pt-4">
              <div>
                <label className="block text-xs font-semibold text-foreground mb-1.5">Full Name</label>
                <input
                  type="text"
                  value={userForm.name}
                  onChange={(e) => setUserForm({ ...userForm, name: e.target.value })}
                  placeholder="e.g., Maria Santos"
                  className="w-full px-3 py-2 text-sm border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-foreground mb-1.5">Email Address</label>
                <input
                  type="email"
                  value={userForm.email}
                  onChange={(e) => setUserForm({ ...userForm, email: e.target.value })}
                  placeholder="e.g., maria.santos@acme.com"
                  className="w-full px-3 py-2 text-sm font-mono border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-foreground mb-1.5">Role</label>
                <select
                  value={userForm.role}
                  onChange={(e) => setUserForm({ ...userForm, role: e.target.value })}
                  className="w-full px-3 py-2 text-sm border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                >
                  <option value="Admin">Admin</option>
                  <option value="Cost Analyst">Cost Analyst</option>
                  <option value="Department Manager">Department Manager</option>
                  <option value="Auditor">Auditor</option>
                </select>
              </div>
              <div>
                <label className="block text-xs font-semibold text-foreground mb-1.5">Status</label>
                <select
                  value={userForm.status}
                  onChange={(e) => setUserForm({ ...userForm, status: e.target.value })}
                  className="w-full px-3 py-2 text-sm border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
                >
                  <option value="Active">Active</option>
                  <option value="Inactive">Inactive</option>
                </select>
              </div>
              <div className="flex gap-2 justify-end pt-3">
                <Button
                  variant="outline"
                  onClick={() => {
                    setShowAddUserModal(false)
                    setShowEditUserModal(false)
                  }}
                >
                  Cancel
                </Button>
                <Button
                  className="bg-primary text-primary-foreground hover:bg-primary/90"
                  onClick={showAddUserModal ? handleAddUser : handleEditUser}
                >
                  {showAddUserModal ? "Add User" : "Save Changes"}
                </Button>
              </div>
            </CardContent>
          </Card>
        </div>
      )}

      {/* Delete User Confirmation Modal */}
      {showDeleteUserConfirm && selectedUser && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-md border-border shadow-xl">
            <CardHeader className="flex flex-row items-center justify-between border-b border-border/60 pb-4">
              <div className="flex items-center gap-2">
                <AlertCircle className="w-4 h-4 text-accent" />
                <CardTitle className="text-base font-semibold">Delete User</CardTitle>
              </div>
              <Button variant="ghost" size="icon-sm" onClick={() => setShowDeleteUserConfirm(false)}>
                <X className="w-4 h-4" />
              </Button>
            </CardHeader>
            <CardContent className="space-y-4 pt-4">
              <p className="text-sm text-foreground">
                Are you sure you want to remove <strong>{selectedUser.name}</strong> ({selectedUser.email})? This user will no longer be able to log in or audit records.
              </p>
              <div className="flex gap-2 justify-end pt-2">
                <Button variant="outline" onClick={() => setShowDeleteUserConfirm(false)}>
                  Cancel
                </Button>
                <Button className="bg-accent text-accent-foreground hover:bg-accent/90" onClick={handleDeleteUser}>
                  Delete User
                </Button>
              </div>
            </CardContent>
          </Card>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm && selectedDept && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-xs flex items-center justify-center z-50 p-4">
          <Card className="w-full max-w-md border-border shadow-xl">
            <CardHeader className="flex flex-row items-center justify-between border-b border-border/60 pb-4">
              <div className="flex items-center gap-2">
                <AlertCircle className="w-4 h-4 text-accent" />
                <CardTitle className="text-base font-semibold">Delete Department</CardTitle>
              </div>
              <Button variant="ghost" size="icon-sm" onClick={() => setShowDeleteConfirm(false)}>
                <X className="w-4 h-4" />
              </Button>
            </CardHeader>
            <CardContent className="space-y-4 pt-4">
              <p className="text-sm text-foreground">
                Are you sure you want to delete <strong>{selectedDept.name}</strong>? This action cannot be undone.
              </p>
              <div className="flex gap-2 justify-end pt-2">
                <Button variant="outline" onClick={() => setShowDeleteConfirm(false)}>
                  Cancel
                </Button>
                <Button className="bg-accent text-accent-foreground hover:bg-accent/90" onClick={handleDeleteDept}>
                  Delete Department
                </Button>
              </div>
            </CardContent>
          </Card>
        </div>
      )}
    </div>
  )
}

