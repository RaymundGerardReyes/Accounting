INSERT INTO tax_codes (code, description, rate_bps, requires_bir_2307)
VALUES
    ('VAT_12',  'Philippine 12% Value-Added Tax (RMC No. 77-2024)',             1200, FALSE),
    ('NON_VAT', 'VAT-Exempt / Non-VAT Transaction',                                0, FALSE),
    ('ZERO_RT', 'Zero-Rated Export / PEZA Transaction',                            0, FALSE),
    ('EWT_1',   'Expanded Withholding Tax - Goods Purchase 1% (BIR Form 2307)',  100, TRUE),
    ('EWT_2',   'Expanded Withholding Tax - Services 2% (BIR Form 2307)',        200, TRUE),
    ('EWT_5',   'Expanded Withholding Tax - Rentals / Prof. 5% (BIR Form 2307)', 500, TRUE),
    ('EWT_10',  'Expanded Withholding Tax - Consultants 10% (BIR Form 2307)',   1000, TRUE)
ON CONFLICT (code) DO NOTHING;

