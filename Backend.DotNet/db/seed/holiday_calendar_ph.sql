INSERT INTO ph_holidays (holiday_date, name, is_regular)
VALUES
    ('2026-01-01', 'New Year''s Day', TRUE),
    ('2026-04-02', 'Maundy Thursday', TRUE),
    ('2026-04-03', 'Good Friday', TRUE),
    ('2026-04-09', 'Araw ng Kagitingan', TRUE),
    ('2026-05-01', 'Labor Day', TRUE),
    ('2026-06-12', 'Philippine Independence Day', TRUE),
    ('2026-08-21', 'Ninoy Aquino Day', FALSE),
    ('2026-08-31', 'National Heroes Day', TRUE),
    ('2026-11-01', 'All Saints'' Day', FALSE),
    ('2026-11-30', 'Bonifacio Day', TRUE),
    ('2026-12-25', 'Christmas Day', TRUE),
    ('2026-12-30', 'Rizal Day', TRUE)
ON CONFLICT (holiday_date) DO NOTHING;

