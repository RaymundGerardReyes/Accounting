BEGIN;

CREATE TABLE IF NOT EXISTS cost_allocation_rule_versions (
    rule_version_id VARCHAR(64) PRIMARY KEY,
    effective_from_date DATE NOT NULL,
    effective_to_date DATE,
    version_status VARCHAR(24) NOT NULL DEFAULT 'Active',
    authored_by_actor_id VARCHAR(128) NOT NULL,
    approved_by_actor_id VARCHAR(128) NOT NULL,
    audit_hash VARCHAR(128) NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_cost_rule_version_status CHECK (version_status IN ('Draft', 'Active', 'Retired')),
    CONSTRAINT chk_cost_rule_version_sod CHECK (authored_by_actor_id <> approved_by_actor_id)
);

CREATE TABLE IF NOT EXISTS cost_allocation_departments (
    department_id VARCHAR(64) PRIMARY KEY,
    rule_version_id VARCHAR(64) NOT NULL REFERENCES cost_allocation_rule_versions(rule_version_id),
    department_name VARCHAR(160) NOT NULL,
    cost_center_id VARCHAR(64) NOT NULL,
    direct_cost_centavos BIGINT NOT NULL DEFAULT 0,
    budget_cap_centavos BIGINT NOT NULL DEFAULT 0,
    headcount_driver_weight INT NOT NULL DEFAULT 1,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_cost_dept_non_negative CHECK (
        direct_cost_centavos >= 0 AND budget_cap_centavos >= 0 AND headcount_driver_weight >= 0)
);

CREATE TABLE IF NOT EXISTS cost_allocation_activities (
    activity_id VARCHAR(64) PRIMARY KEY,
    department_id VARCHAR(64) NOT NULL REFERENCES cost_allocation_departments(department_id),
    activity_name VARCHAR(160) NOT NULL,
    cost_driver_code VARCHAR(64) NOT NULL,
    allocated_centavos BIGINT NOT NULL DEFAULT 0,
    allocation_basis_points INT NOT NULL DEFAULT 0,
    CONSTRAINT chk_cost_activity_bps CHECK (
        allocation_basis_points BETWEEN 0 AND 10000 AND allocated_centavos >= 0)
);

INSERT INTO cost_allocation_rule_versions (
    rule_version_id, effective_from_date, version_status,
    authored_by_actor_id, approved_by_actor_id, audit_hash)
VALUES (
    'COST-RULE-2026.1', '2026-01-01', 'Active',
    'usr-cost-analyst-01', 'usr-controller-01',
    '7f83b1657ff1fc53b92dc18148a1d65dfc2d4b1fa3d677284addd200126d9069')
ON CONFLICT (rule_version_id) DO NOTHING;

INSERT INTO cost_allocation_departments (
    department_id, rule_version_id, department_name, cost_center_id,
    direct_cost_centavos, budget_cap_centavos, headcount_driver_weight)
VALUES
    ('DEPT-FIN', 'COST-RULE-2026.1', 'Finance & Accounting', 'CC-CORP-00', 45000000, 60000000, 12),
    ('DEPT-TREASURY', 'COST-RULE-2026.1', 'Treasury Operations', 'CC-CORP-00', 32000000, 45000000, 8),
    ('DEPT-OPS', 'COST-RULE-2026.1', 'Field Operations', 'CC-FIELD-01', 125000000, 150000000, 45)
ON CONFLICT (department_id) DO NOTHING;

COMMIT;
