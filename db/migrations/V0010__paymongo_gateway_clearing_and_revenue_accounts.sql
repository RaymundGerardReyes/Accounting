BEGIN;

INSERT INTO gl_chart_of_accounts (
    account_code, account_name, account_type, normal_balance,
    default_tax_profile, opening_debit_centavos, opening_credit_centavos, description
)
VALUES
    ('1050', 'Payment Gateway Clearing - PayMongo', 'Asset', 'Debit', 'NONE', 0, 0, 'Unsettled customer collections captured by PayMongo awaiting bank payout'),
    ('2130', 'Output VAT Payable (12%)', 'Liability', 'Credit', 'VAT_12_INCL', 0, 0, 'BIR EOPT statutory 12% output VAT liability'),
    ('4010', 'Sales & Operating Revenue', 'Revenue', 'Credit', 'VAT_12_INCL', 0, 0, 'Recognized customer order revenue net of output VAT'),
    ('5310', 'Merchant Processing Fee Expense', 'Expense', 'Debit', 'NONE', 0, 0, 'PayMongo gateway collection and settlement processing fees')
ON CONFLICT (account_code) DO NOTHING;

COMMIT;
