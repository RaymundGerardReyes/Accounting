"use client"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { AlertCircle, ArrowLeft, Bell, CreditCard, CheckCircle2, Database, Edit2, Plus, Save, Shield, Trash2, X } from "lucide-react"
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
  config: any;
  departments: Department[];
  users: User[];
  auditLogs: any[];
  integrations: any[];
}

export function Configuration({ onBack }: ConfigurationProps) {
  const [data, setData] = useState<ConfigData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saveStatus, setSaveStatus] = useState<string | null>(null);

  // States for interactive elements that can be modified by the user
  const [config, setConfig] = useState<any>(null);
  const [departments, setDepartments] = useState<Department[]>([]);

  const [activeTab, setActiveTab] = useState<"general" | "departments" | "users" | "audit" | "integrations">("general")
  const [showAddDeptModal, setShowAddDeptModal] = useState(false)
  const [showEditDeptModal, setShowEditDeptModal] = useState(false)
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false)
  const [selectedDept, setSelectedDept] = useState<Department | null>(null)
  const [deptForm, setDeptForm] = useState({ name: "", costCenter: "", manager: "", budget: "" })

  useEffect(() => {
    const fetchConfigData = async () => {
      try {
        const response = await fetch("http://localhost:5000/api/configuration");
        if (!response.ok) {
          throw new Error(`HTTP error! status: ${response.status}`);
        }
        const result = await response.json();
        setData(result);
        // Initialize the editable states with data from the backend
        setConfig(result.config);
        setDepartments(result.departments);
      } catch (e: any) {
        setError(e.message);
      } finally {
        setLoading(false);
      }
    };

    fetchConfigData();
  }, []);

  const handleSaveConfig = async () => {
    try {
      const response = await fetch("/api/configuration", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ config }),
      })
      if (!response.ok) throw new Error("Failed to save configuration")
      setSaveStatus("Settings saved successfully to server!")
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
          : d,
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
    return <div className="p-8 text-center">Loading configuration...</div>;
  }

  if (error) {
    return <div className="p-8 text-center text-red-500">Failed to load data: {error}</div>;
  }

  if (!data || !config) {
    return <div className="p-8 text-center">No configuration data available.</div>;
  }

  // Use read-only data for parts of the UI that don't change
  const { users, auditLogs, integrations } = data;

  // A helper to render integration icons dynamically
  const getIntegrationIcon = (name: string) => {
    switch (name) {
      case "PayMongo Payment Gateway": return <CreditCard className="w-6 h-6 text-accent" />;
      case "Database": return <Database className="w-6 h-6 text-accent" />;
      case "Email Notifications": return <Bell className="w-6 h-6 text-accent" />;
      case "API Access": return <Shield className="w-6 h-6 text-accent" />;
      default: return null;
    }
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
            <h1 className="text-4xl font-bold text-foreground">Configuration & Admin</h1>
            <p className="text-muted-foreground mt-2">System settings and administration panel</p>
          </div>
        </div>
      </div>

      {/* Tab Navigation */}
      <div className="flex gap-2 border-b border-border">
        {(["general", "departments", "users", "audit", "integrations"] as const).map((tab) => (
          <button
            key={tab}
            onClick={() => setActiveTab(tab)}
            className={`px-4 py-2 font-medium text-sm transition-colors ${activeTab === tab ? "text-accent border-b-2 border-accent" : "text-muted-foreground hover:text-foreground"
              }`}
          >
            {tab.charAt(0).toUpperCase() + tab.slice(1)}
          </button>
        ))}
      </div>

      {/* General Settings Tab */}
      {activeTab === "general" && (
        <Card>
          <CardHeader>
            <CardTitle>System Settings</CardTitle>
            <CardDescription>Configure general system parameters</CardDescription>
          </CardHeader>
          <CardContent className="space-y-6">
            <div className="grid grid-cols-2 gap-6">
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Company Name</label>
                <input
                  type="text"
                  value={config.companyName}
                  onChange={(e) => setConfig({ ...config, companyName: e.target.value })}
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Fiscal Year</label>
                <input
                  type="text"
                  value={config.fiscalYear}
                  onChange={(e) => setConfig({ ...config, fiscalYear: e.target.value })}
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Allocation Method</label>
                <select
                  value={config.allocationMethod}
                  onChange={(e) => setConfig({ ...config, allocationMethod: e.target.value })}
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                >
                  <option value="activity-based">Activity-Based</option>
                  <option value="volume-based">Volume-Based</option>
                  <option value="direct">Direct</option>
                  <option value="machine-hours">Machine Hours</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Reporting Currency</label>
                <select
                  value={config.reportingCurrency}
                  onChange={(e) => setConfig({ ...config, reportingCurrency: e.target.value })}
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                >
                  <option value="PHP">PHP (₱)</option>
                  <option value="USD">USD ($)</option>
                  <option value="EUR">EUR (€)</option>
                  <option value="GBP">GBP (£)</option>
                  <option value="JPY">JPY (¥)</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Decimal Places</label>
                <input
                  type="number"
                  min="0"
                  max="4"
                  value={config.decimalPlaces}
                  onChange={(e) => setConfig({ ...config, decimalPlaces: Number.parseInt(e.target.value) })}
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Notification Email</label>
                <input
                  type="email"
                  value={config.notificationEmail}
                  onChange={(e) => setConfig({ ...config, notificationEmail: e.target.value })}
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
            </div>

            <div className="space-y-3">
              <label className="flex items-center gap-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={config.autoBackup}
                  onChange={(e) => setConfig({ ...config, autoBackup: e.target.checked })}
                  className="w-4 h-4 rounded border-border"
                />
                <span className="text-sm font-medium text-foreground">Enable automatic daily backups</span>
              </label>
            </div>

            {saveStatus && (
              <div className="p-3 rounded-lg bg-accent/10 border border-accent/20 text-xs text-foreground flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 text-accent shrink-0" />
                <span>{saveStatus}</span>
              </div>
            )}

            <Button
              onClick={handleSaveConfig}
              className="bg-accent text-accent-foreground hover:bg-accent/90"
            >
              <Save className="w-4 h-4 mr-2" />
              Save Settings
            </Button>
          </CardContent>
        </Card>
      )}

      {/* Departments Tab */}
      {activeTab === "departments" && (
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <div>
              <CardTitle>Departments</CardTitle>
              <CardDescription>Manage cost centers and departments</CardDescription>
            </div>
            <Button
              size="sm"
              className="gap-2 bg-accent text-accent-foreground hover:bg-accent/90 transition-all duration-200 shadow-sm hover:shadow-md"
              onClick={() => {
                setDeptForm({ name: "", costCenter: "", manager: "", budget: "" })
                setShowAddDeptModal(true)
              }}
            >
              <Plus className="w-4 h-4" />
              Add Department
            </Button>
          </CardHeader>
          <CardContent>
            <div className="space-y-3">
              {departments.map((dept) => (
                <div
                  key={dept.id}
                  className="flex items-center justify-between p-4 border border-border rounded-lg hover:bg-muted transition-colors group"
                >
                  <div className="flex-1">
                    <h3 className="font-semibold text-foreground">{dept.name}</h3>
                    <div className="flex gap-6 mt-1 text-sm text-muted-foreground">
                      <span>Cost Center: {dept.costCenter}</span>
                      <span>Manager: {dept.manager}</span>
                      <span>Budget: ₱{(dept.budget / 1000).toFixed(0)}K</span>
                    </div>
                  </div>
                  <div className="flex gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
                    <Button
                      variant="outline"
                      size="sm"
                      className="gap-2 hover:bg-accent/10 hover:text-accent hover:border-accent transition-colors bg-transparent"
                      onClick={() => openEditModal(dept)}
                      title="Edit department"
                    >
                      <Edit2 className="w-4 h-4" />
                      <span className="text-xs font-medium">Edit</span>
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      className="gap-2 hover:bg-red-500/10 hover:text-red-600 hover:border-red-500 transition-colors bg-transparent"
                      onClick={() => openDeleteConfirm(dept)}
                      title="Delete department"
                    >
                      <Trash2 className="w-4 h-4" />
                      <span className="text-xs font-medium">Delete</span>
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
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <div>
              <CardTitle>User Management</CardTitle>
              <CardDescription>Manage system users and permissions</CardDescription>
            </div>
            <Button
              size="sm"
              className="gap-2 bg-accent text-accent-foreground hover:bg-accent/90 transition-all duration-200 shadow-sm hover:shadow-md"
            >
              <Plus className="w-4 h-4" />
              Add User
            </Button>
          </CardHeader>
          <CardContent>
            <div className="space-y-3">
              {users.map((user) => (
                <div
                  key={user.id}
                  className="flex items-center justify-between p-4 border border-border rounded-lg hover:bg-muted transition-colors group"
                >
                  <div className="flex-1">
                    <h3 className="font-semibold text-foreground">{user.name}</h3>
                    <div className="flex gap-6 mt-1 text-sm text-muted-foreground">
                      <span>{user.email}</span>
                      <span>Role: {user.role}</span>
                      <span className={user.status === "Active" ? "text-accent" : "text-muted-foreground"}>
                        {user.status}
                      </span>
                    </div>
                  </div>
                  <div className="flex gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
                    <Button
                      variant="outline"
                      size="sm"
                      className="gap-2 hover:bg-accent/10 hover:text-accent hover:border-accent transition-colors bg-transparent"
                      title="Edit user"
                    >
                      <Edit2 className="w-4 h-4" />
                      <span className="text-xs font-medium">Edit</span>
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      className="gap-2 hover:bg-red-500/10 hover:text-red-600 hover:border-red-500 transition-colors bg-transparent"
                      title="Delete user"
                    >
                      <Trash2 className="w-4 h-4" />
                      <span className="text-xs font-medium">Delete</span>
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
        <Card>
          <CardHeader>
            <CardTitle>Audit Trail</CardTitle>
            <CardDescription>System activity and change log</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="space-y-3">
              {auditLogs.map((log) => (
                <div
                  key={log.id}
                  className="flex items-center justify-between p-4 border border-border rounded-lg hover:bg-muted transition-colors"
                >
                  <div className="flex-1">
                    <div className="flex items-center gap-2">
                      <h3 className="font-semibold text-foreground">{log.action}</h3>
                      <span
                        className={`text-xs px-2 py-1 rounded ${log.severity === "success"
                            ? "bg-accent/10 text-accent"
                            : log.severity === "warning"
                              ? "bg-primary/10 text-primary"
                              : "bg-muted text-muted-foreground"
                          }`}
                      >
                        {log.severity}
                      </span>
                    </div>
                    <p className="text-sm text-muted-foreground mt-1">By {log.user}</p>
                  </div>
                  <span className="text-sm text-muted-foreground">{log.time}</span>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Integrations Tab */}
      {activeTab === "integrations" && (
        <Card>
          <CardHeader>
            <CardTitle>Integrations</CardTitle>
            <CardDescription>Manage system integrations and connections</CardDescription>
          </CardHeader>
          <CardContent>
            <div className="space-y-3">
              {integrations.map((integration) => (
                <div
                  key={integration.name}
                  className="flex items-center justify-between p-4 border border-border rounded-lg hover:bg-muted transition-colors"
                >
                  <div className="flex items-center gap-4 flex-1">
                    {getIntegrationIcon(integration.name)}
                    <div>
                      <h3 className="font-semibold text-foreground">{integration.name}</h3>
                      <p className="text-sm text-muted-foreground">
                        {integration.lastSync !== "N/A" ? `Last sync: ${integration.lastSync}` : ""}
                      </p>
                    </div>
                  </div>
                  <div className="flex items-center gap-4">
                    <span className="text-sm font-medium text-accent">{integration.status}</span>
                    <Button variant="outline" size="sm">
                      Configure
                    </Button>
                  </div>
                </div>
              )
              )}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Add Department Modal */}
      {showAddDeptModal && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <Card className="w-full max-w-md">
            <CardHeader className="flex flex-row items-center justify-between">
              <CardTitle>Add New Department</CardTitle>
              <Button variant="ghost" size="icon" onClick={() => setShowAddDeptModal(false)}>
                <X className="w-4 h-4" />
              </Button>
            </CardHeader>
            <CardContent className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Department Name</label>
                <input
                  type="text"
                  value={deptForm.name}
                  onChange={(e) => setDeptForm({ ...deptForm, name: e.target.value })}
                  placeholder="e.g., Manufacturing"
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Cost Center</label>
                <input
                  type="text"
                  value={deptForm.costCenter}
                  onChange={(e) => setDeptForm({ ...deptForm, costCenter: e.target.value })}
                  placeholder="e.g., CC-001"
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Manager</label>
                <input
                  type="text"
                  value={deptForm.manager}
                  onChange={(e) => setDeptForm({ ...deptForm, manager: e.target.value })}
                  placeholder="e.g., John Smith"
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Budget</label>
                <input
                  type="number"
                  value={deptForm.budget}
                  onChange={(e) => setDeptForm({ ...deptForm, budget: e.target.value })}
                  placeholder="e.g., 450000"
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
              <div className="flex gap-2 justify-end pt-4">
                <Button variant="outline" onClick={() => setShowAddDeptModal(false)}>
                  Cancel
                </Button>
                <Button className="bg-accent text-accent-foreground hover:bg-accent/90" onClick={handleAddDept}>
                  Add Department
                </Button>
              </div>
            </CardContent>
          </Card>
        </div>
      )}

      {/* Edit Department Modal */}
      {showEditDeptModal && selectedDept && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <Card className="w-full max-w-md">
            <CardHeader className="flex flex-row items-center justify-between">
              <CardTitle>Edit Department</CardTitle>
              <Button variant="ghost" size="icon" onClick={() => setShowEditDeptModal(false)}>
                <X className="w-4 h-4" />
              </Button>
            </CardHeader>
            <CardContent className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Department Name</label>
                <input
                  type="text"
                  value={deptForm.name}
                  onChange={(e) => setDeptForm({ ...deptForm, name: e.target.value })}
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Cost Center</label>
                <input
                  type="text"
                  value={deptForm.costCenter}
                  onChange={(e) => setDeptForm({ ...deptForm, costCenter: e.target.value })}
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Manager</label>
                <input
                  type="text"
                  value={deptForm.manager}
                  onChange={(e) => setDeptForm({ ...deptForm, manager: e.target.value })}
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Budget</label>
                <input
                  type="number"
                  value={deptForm.budget}
                  onChange={(e) => setDeptForm({ ...deptForm, budget: e.target.value })}
                  className="w-full px-4 py-2 border border-border rounded-md bg-card text-foreground focus:outline-none focus:ring-2 focus:ring-accent"
                />
              </div>
              <div className="flex gap-2 justify-end pt-4">
                <Button variant="outline" onClick={() => setShowEditDeptModal(false)}>
                  Cancel
                </Button>
                <Button className="bg-accent text-accent-foreground hover:bg-accent/90" onClick={handleEditDept}>
                  Save Changes
                </Button>
              </div>
            </CardContent>
          </Card>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm && selectedDept && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <Card className="w-full max-w-md">
            <CardHeader className="flex flex-row items-center justify-between">
              <div className="flex items-center gap-2">
                <AlertCircle className="w-5 h-5 text-red-600" />
                <CardTitle>Delete Department</CardTitle>
              </div>
              <Button variant="ghost" size="icon" onClick={() => setShowDeleteConfirm(false)}>
                <X className="w-4 h-4" />
              </Button>
            </CardHeader>
            <CardContent className="space-y-4">
              <p className="text-foreground">
                Are you sure you want to delete <strong>{selectedDept.name}</strong>? This action cannot be undone.
              </p>
              <div className="flex gap-2 justify-end pt-4">
                <Button variant="outline" onClick={() => setShowDeleteConfirm(false)}>
                  Cancel
                </Button>
                <Button className="bg-red-600 text-white hover:bg-red-700" onClick={handleDeleteDept}>
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
