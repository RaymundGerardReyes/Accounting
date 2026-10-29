INSERT INTO approval_matrix (workflow, stage, role, min_amount_minor, max_amount_minor)
VALUES
    ('cash_advance',     'supervisor_approval',     'supervisor',         100,      5000000),
    ('cash_advance',     'finance_review',          'finance_reviewer',   100,     15000000),
    ('cash_advance',     'controller_ratification', 'finance_controller', 5000001, 50000000),
    ('purchase_request', 'supervisor_approval',     'supervisor',         100,     10000000),
    ('purchase_request', 'controller_ratification', 'finance_controller', 10000001, 999999999),
    ('expense_report',   'finance_review',          'finance_reviewer',   100,     50000000)
ON CONFLICT DO NOTHING;

