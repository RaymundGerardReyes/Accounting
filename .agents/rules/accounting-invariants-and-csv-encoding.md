# Accounting Domain Invariants & CSV Encoding Standards

## 1. Character Encoding for Spreadsheet Reports
- Any client-side or server-side CSV file containing non-ASCII symbols (such as the Philippine Peso `₱` Unicode code point `U+20B1`) MUST prepend the UTF-8 Byte Order Mark (`\uFEFF` / `0xEF 0xBB 0xBF`) to the Blob or output stream.
- **Why**: In the absence of a UTF-8 BOM, Microsoft Excel for Windows defaults to legacy Windows ANSI / Windows-1252 (CP-1252), converting the UTF-8 bytes `0xE2 0x82 0xB1` into Mojibake (`â,±`).
- **Standard TypeScript Pattern**:
  ```typescript
  const blob = new Blob(["\uFEFF", csvContent], { type: "text/csv;charset=utf-8;" })
  ```

## 2. Cost Accounting Variance Sign Conventions
- Cost Variance across all accounting views must strictly adhere to:
  $$\text{Cost Variance} = \text{Budget (Allocated)} - \text{Actual}$$
- **Interpretation**:
  - $\text{Variance} \ge 0$: **Favorable** (within budget, cost savings). Styled in foreground/neutral.
  - $\text{Variance} < 0$: **Unfavorable** (over budget, cost overrun). Styled in `text-accent` (Red) with alert indicators.
- Summary total rows must equal the mathematical sum of the individual line item variances.

## 3. Numeric Cell Integrity in Exports
- CSV columns with currency indicators in the header (e.g. `Budget (PHP)`) must output raw numeric values (`180000`, `-5000`) rather than currency-prefixed strings (`₱180000`).
- Text headers and category strings containing commas or special characters must be properly quoted per RFC 4180.

## 4. Next.js Windows Dev Server Optimization
- Next.js dev scripts on Windows must bind explicitly to `localhost` (`-H localhost`) to prevent link-local APIPA (`169.254.x.x`) adapter resolution delays (~9.8s).
- Non-initial tab components must use `next/dynamic` to avoid compiling massive unused module trees on cold startup.

