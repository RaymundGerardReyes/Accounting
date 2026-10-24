# R.G.E Google Forms & Apps Script PayMongo Orchestrator

## Overview
This middleware transforms Google Forms into a resilient, high-concurrency shock absorber for up to 5,000+ simultaneous users. Google absorbs submission spikes at zero server load, while Google Apps Script orchestrates orders into your FastAPI backend, pairs them with live PayMongo checkout sessions (QR Ph, GCash, Maya, Card), writes references back to Google Sheets, and receives real-time reverse webhooks upon payment settlement.

---

## Why the `TypeError: Cannot read properties of null (reading 'getActiveSheet')` Bug Happened

In Google Apps Script, `SpreadsheetApp.getActiveSpreadsheet()` returns `null` in three common scenarios:
1. **Script Opened from Google Forms or as Standalone**: When opened from Google Forms (`form.google.com`) or standalone (`script.google.com`) instead of the linked Google Sheet (**Extensions $\rightarrow$ Apps Script**), the script is not container-bound to a spreadsheet.
2. **Trigger Event Source is Form Trigger**: A Form trigger passes `e.response` with no active spreadsheet context attached (unlike a Sheet trigger which passes `e.range` and `e.namedValues`).
3. **Clicking the "Run" Button in the Apps Script Editor**: Testing `onFormSubmit` by clicking **Run** executes the function without an event object (`e === undefined`). Calling `.getActiveSheet()` on a null spreadsheet crashed with an unhandled TypeError.

---

## How the Permanent Fix Works

`scripts/google_apps_script/Code.gs` implements a **5-Layer Sheet Resolution** engine (`resolveTargetSheet`) that guarantees the script never crashes:

1. **Layer 1: Google Sheet Event Range (`e.range.getSheet()`)**: The most reliable path when triggered from a Google Sheet `onFormSubmit` trigger.
2. **Layer 2: Event Source Active Sheet (`e.source.getActiveSheet()`)**: Catches instances where the event source is an active Spreadsheet object.
3. **Layer 3: Container-Bound Active Spreadsheet (`SpreadsheetApp.getActiveSpreadsheet()`)**: Safely wrapped in `try / catch` to prevent uncaught null reference errors.
4. **Layer 4: Linked Destination Detection (`FormApp.getActiveForm().getDestinationId()`)**: If attached to a Google Form, inspects the form's destination ID and opens the linked Google Sheet with `SpreadsheetApp.openById(...)`.
5. **Layer 5: Standalone Project Fallback (`CONFIG.SPREADSHEET_ID`)**: Allows specifying an explicit spreadsheet ID if running as a standalone project.

### Non-Blocking Cloud API Execution
Even if no Google Sheet is connected or active, the script continues non-blockingly:
- Calls `POST /api/v1/integrations/google-forms/submit` with `X-Google-Forms-Secret`.
- Receives the live PayMongo checkout link.
- Emails the payment link directly to the customer.

---

## 4-Step Setup & Deployment Guide

### Step 1: Create or Link Your Google Form
1. Open your Google Form with the recommended question titles:
   - **Full Name** (Short answer, Required)
   - **Email Address** (Short answer, Required)
   - **Mobile Number** (Short answer, Optional)
   - **Package / Product** (Dropdown / Radio, e.g. `Product A`, `Product B`, `Product C`)
   - **Quantity** (Short answer / Number, Default: `1`)
   - **Notes / Instructions** (Paragraph, Optional)
2. Go to the **Responses** tab and click **Link to Sheets** (or **View in Sheets**).

### Step 2: Paste the Hardened Code
1. Inside the linked Google Sheet, click **Extensions $\rightarrow$ Apps Script**.
2. Replace all existing code with [`scripts/google_apps_script/Code.gs`](./Code.gs).
3. Review the `CONFIG` block:
   ```javascript
   var CONFIG = {
     BACKEND_URL: "https://rge-accounting-3aa707d5f496.herokuapp.com/api/v1/integrations/google-forms/submit",
     GOOGLE_FORMS_SECRET: "rge_gforms_live_sec_89f3a21",
     SPREADSHEET_ID: "", // Optional: only needed if script is standalone
     SHEET_TAB_NAME: "", // Optional: tab name, leave empty for first tab
     COMPANY_NAME: "R.G.E Accounting Firm Inc.",
     SUPPORT_EMAIL: "raymundgerardrestaca@gmail.com",
     SEND_CUSTOMER_EMAIL: true
   };
   ```
4. Click **Save** (floppy disk icon or `Ctrl + S`).

### Step 3: Test Directly in the Editor
- With `onFormSubmit` selected in the toolbar, click **Run**.
- Because of `resolveTargetSheet(e)` and safe diagnostic payload fallback, clicking **Run** executes cleanly without throwing `TypeError: Cannot read properties of null (reading 'getActiveSheet')`.
- Check **Execution log** at the bottom to confirm successful processing.

### Step 4: Configure Live Triggers & Reverse Webhook (doPost)

#### A. Ingest Trigger (Google Sheet $\rightarrow$ Backend):
1. In the Apps Script left sidebar, click **Triggers** (alarm clock icon).
2. Click **+ Add Trigger** (bottom right):
   - **Choose which function to run**: `onFormSubmit`
   - **Choose which deployment should run**: `Head`
   - **Select event source**: `From spreadsheet`
   - **Select event type**: `On form submit`
3. Click **Save** and grant Google OAuth authorization permissions.

#### B. Reverse Webhook Deployment (Backend $\rightarrow$ Google Sheet `doPost`):
To enable the backend to automatically update the Google Sheet row to `PAID`, `FAILED`, or `EXPIRED` when PayMongo webhooks fire:
1. In Apps Script, click **Deploy $\rightarrow$ New deployment**.
2. Select type: **Web app**.
3. Set:
   - **Description**: `PayMongo Reverse Webhook Sync`
   - **Execute as**: `Me`
   - **Who has access**: `Anyone`
4. Click **Deploy** and copy the **Web App URL** (e.g. `https://script.google.com/macros/s/.../exec`).
5. Add this URL to your root `.env` file:
   ```env
   GOOGLE_APPS_SCRIPT_WEBHOOK_URL="https://script.google.com/macros/s/YOUR_DEPLOYMENT_ID/exec"
   ```

---

## Bi-Directional Synchronization Architecture

```
[Customer Form Submission]
          │
          ▼
   [Google Forms]
          │ (Auto-records row)
          ▼
   [Google Sheets]
          │ (onFormSubmit Trigger)
          ▼
[Apps Script Code.gs]
          │ (POST /api/v1/integrations/google-forms/submit)
          │ (Header: X-Google-Forms-Secret)
          ▼
[FastAPI Backend + PayMongo API]
          │
          ├── Creates Order & PayMongo Checkout URL
          ├── Writes back: Order No, PayMongo Link, PHP Amount, Status to Google Sheet
          └── Dispatches initial payment email to customer
                   │
                   ▼ (Customer pays via QR Ph / GCash / Maya / Card)
          [PayMongo Gateway Webhook]
                   │ (POST /api/v1/webhooks/paymongo)
                   │ (Cryptographic HMAC-SHA256 Signature Verification)
                   ▼
          [FastAPI Backend Orchestrator]
                   ├── Marks order 'PAID' (Terminal State Protected)
                   ├── Dispatches Official BIR Receipt via SMTP (PDF attached)
                   ├── Updates Payments Hub UI (Frontend/components/payments-hub.tsx)
                   └── Calls Reverse Webhook (doPost) on Apps Script Web App
                                 │
                                 ▼
                   [Google Sheet Row Updated to PAID with Official Receipt No]
```

---

## Reverse Proxy Header Forwarding (NGINX)
Ensure your `/etc/nginx/nginx.conf` passes the `X-Google-Forms-Secret` and `Paymongo-Signature` headers to FastAPI:
```nginx
location /api/ {
    proxy_pass http://127.0.0.1:5000;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Google-Forms-Secret $http_x_google_forms_secret;
    proxy_set_header Paymongo-Signature $http_paymongo_signature;
}
```
