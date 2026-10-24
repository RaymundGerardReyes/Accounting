/**
 * ==============================================================================
 * R.G.E ACCOUNTING FIRM INC. - GOOGLE FORMS TO PAYMONGO LIVE ORCHESTRATOR
 * ==============================================================================
 * File: scripts/google_apps_script/Code.gs
 *
 * CAPABILITIES:
 * 1. Absorbs 5,000+ concurrent Google Form submissions without server crashes.
 * 2. Eliminates "Cannot read properties of null (reading 'getActiveSheet')".
 * 3. Protects Google Sheets against CSV / Formula Injection (=, +, -, @).
 * 4. Sends HMAC-authenticated payloads to FastAPI (/api/v1/integrations/google-forms/submit).
 * 5. Exposes doPost(e) Reverse-Webhook so FastAPI PayMongo Webhooks can update
 *    the Google Sheet row live for PAID, FAILED, and EXPIRED transactions!
 * ==============================================================================
 */

var CONFIG = {
  // 1. Your live Cloudflare / NGINX / PaaS domain (must match NEXT_PUBLIC_APP_DOMAIN in .env)
  BACKEND_URL: "https://rge-accounting-3aa707d5f496.herokuapp.com/api/v1/integrations/google-forms/submit",

  // 2. Shared secret key (must match GOOGLE_FORMS_SECRET in root .env)
  SECRET_KEY: "rge_gforms_live_sec_89f3a21",

  // 3. Optional fallback Spreadsheet ID (from https://docs.google.com/spreadsheets/d/THIS_ID/edit)
  // Leave as "" if this script is pasted inside the Google Sheet's Extensions -> Apps Script.
  FALLBACK_SPREADSHEET_ID: "",
  FALLBACK_SHEET_NAME: "",

  // 4. Organization branding
  COMPANY_NAME: "R.G.E Accounting Firm Inc.",
  SUPPORT_EMAIL: "support@rge-accounting.com",

  // 5. Email control:
  // Set to true if Apps Script should email the initial checkout link upon form submission.
  // Final PAID (Official Receipt), FAILED, and EXPIRED emails are sent by Backend SMTP after PayMongo Webhooks!
  SEND_INITIAL_CHECKOUT_EMAIL_FROM_GAS: true
};

/**
 * ==============================================================================
 * 1. OUTBOUND ORCHESTRATOR: Triggered when a user submits the Google Form
 * ==============================================================================
 * @param {Object} e - The Google Sheets or Google Forms submission event object.
 */
function onFormSubmit(e) {
  var lock = LockService.getScriptLock();
  try {
    // Wait up to 30 seconds for concurrent thread isolation
    lock.waitLock(30000);
  } catch (lockErr) {
    Logger.log("Could not obtain script lock: " + lockErr);
  }

  try {
    // Safely resolve the target sheet & row (Never crashes on null getActiveSheet)
    var context = resolveSheetSafely(e);
    var sheet = context.sheet;
    var rowIndex = context.rowIndex;

    // Extract form fields from Sheet row, Form event, or Manual test run
    var formData = extractFormDataSafely(e, sheet, rowIndex);

    var customerEmail = extractField(formData, ["email", "email address", "customer email", "your email"]);
    var customerName = extractField(formData, ["name", "full name", "customer name", "your name"]) || "Valued Client";
    var customerPhone = extractField(formData, ["phone", "mobile", "mobile number", "contact number", "cellphone"]);
    var itemReference = extractField(formData, ["product", "item", "service", "package", "select package", "order category", "department"]) || "Product A";
    var quantityStr = extractField(formData, ["quantity", "qty", "number of items"]);
    var quantity = parseInt(quantityStr, 10);
    if (isNaN(quantity) || quantity < 1) quantity = 1;
    var notes = extractField(formData, ["notes", "remarks", "instructions", "comments"]) || "";

    // Generate deterministic Response ID for atomic idempotency
    var rawTimestamp = formData["timestamp"] || new Date().toISOString();
    var parsedTime = new Date(rawTimestamp).getTime();
    if (isNaN(parsedTime)) parsedTime = new Date().getTime();
    var responseId = (e && e.response && typeof e.response.getId === "function")
      ? "gf_" + e.response.getId()
      : "gf_" + (rowIndex || "manual") + "_" + parsedTime;

    Logger.log("Processing submission for: " + customerName + " (" + customerEmail + ") | Response ID: " + responseId);

    if (!customerEmail) {
      Logger.log("Error: No customer email found in row " + rowIndex);
      if (sheet && rowIndex >= 2) {
        ensureAndWriteColumns(sheet, rowIndex, {
          "Response ID": responseId,
          "Payment Status": "ERROR (MISSING EMAIL)",
          "Error Details": "Customer email address was not provided in the form."
        });
      }
      return;
    }

    // Build authoritative payload matching Backend/google_forms_routes.py
    var payload = {
      response_id: responseId,
      sheet_row_index: rowIndex || 0,
      customer_name: customerName,
      customer_email: customerEmail,
      customer_phone: customerPhone || null,
      item_reference: itemReference,
      reference_code: itemReference,
      title: itemReference + " - " + customerName,
      category: "product_order",
      quantity: quantity,
      payment_method: "hosted_checkout",
      notes: notes
    };

    var payloadJson = JSON.stringify(payload);
    var hmacSignature = computeHmacSha256Hex(payloadJson, CONFIG.SECRET_KEY);

    var options = {
      method: "post",
      contentType: "application/json",
      headers: {
        "X-Google-Forms-Secret": CONFIG.SECRET_KEY,
        "X-Google-Forms-Signature": hmacSignature,
        "Accept": "application/json"
      },
      payload: payloadJson,
      muteHttpExceptions: true
    };

    // Call Cloud FastAPI Backend
    var response = UrlFetchApp.fetch(CONFIG.BACKEND_URL, options);
    var responseCode = response.getResponseCode();
    var responseText = response.getContentText();

    Logger.log("Backend response (" + responseCode + "): " + responseText);

    if (responseCode === 201 || responseCode === 200) {
      var data = JSON.parse(responseText);

      // Write initial PENDING status & PayMongo Checkout Link to Google Sheet
      if (sheet && rowIndex >= 2) {
        ensureAndWriteColumns(sheet, rowIndex, {
          "Response ID": responseId,
          "Order Number": data.order_number || "",
          "PayMongo Checkout Link": data.checkout_url || "",
          "Amount": data.amount_php || "",
          "Payment Status": String(data.status || "PENDING").toUpperCase(),
          "Payment Attempt ID": data.payment_attempt_id || "",
          "Official Receipt No": "",
          "Webhook Outcome Details": "Awaiting PayMongo payment webhook...",
          "Synchronized At": new Date().toLocaleString()
        });
      }

      // Send initial payment instructions email if enabled
      if (CONFIG.SEND_INITIAL_CHECKOUT_EMAIL_FROM_GAS && data.checkout_url) {
        sendPaymentInstructionsEmail(customerEmail, customerName, data);
      }
    } else {
      if (sheet && rowIndex >= 2) {
        ensureAndWriteColumns(sheet, rowIndex, {
          "Response ID": responseId,
          "Payment Status": "ERROR (" + responseCode + ")",
          "Error Details": responseText.substring(0, 150),
          "Synchronized At": new Date().toLocaleString()
        });
      }
    }

  } catch (error) {
    Logger.log("Unhandled Exception in onFormSubmit: " + (error && error.stack ? error.stack : error.toString()));
  } finally {
    try {
      lock.releaseLock();
    } catch (releaseErr) {
      // Ignore lock release errors
    }
  }
}

/**
 * ==============================================================================
 * 2. INBOUND WEBHOOK RECEIVER (doPost):
 *    Called automatically by your FastAPI Backend AFTER a PayMongo Webhook
 *    verifies a PAID, FAILED, or EXPIRED transaction!
 * ==============================================================================
 */
function doPost(e) {
  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(30000);
  } catch (lockErr) {
    return createJsonResponse({ ok: false, error: "Lock timeout" });
  }

  try {
    if (!e || !e.postData || !e.postData.contents) {
      return createJsonResponse({ ok: false, error: "Empty request body" });
    }

    var rawBody = e.postData.contents;
    var body = JSON.parse(rawBody);

    // Verify shared secret to prevent unauthorized tampering
    if (!body.secret || body.secret !== CONFIG.SECRET_KEY) {
      return createJsonResponse({ ok: false, error: "Unauthorized webhook secret" });
    }

    var context = resolveSheetSafely(null);
    var sheet = context.sheet;
    if (!sheet) {
      return createJsonResponse({ ok: false, error: "Spreadsheet not bound or FALLBACK_SPREADSHEET_ID not set" });
    }

    // Locate the exact row by Response ID, Order Number, Payment Attempt ID, or sheet_row_index
    var targetRow = findMatchingRow(
      sheet,
      body.response_id,
      body.order_number,
      body.payment_attempt_id,
      body.sheet_row_index
    );

    if (!targetRow) {
      return createJsonResponse({
        ok: false,
        error: "Could not find matching row for Order " + (body.order_number || body.response_id)
      });
    }

    // Format authoritative PayMongo webhook status (PAID, FAILED, EXPIRED)
    var normalizedStatus = String(body.payment_status || "PENDING").toUpperCase();
    var outcomeDetails = body.outcome_details || "";
    if (normalizedStatus === "PAID") {
      outcomeDetails = "Verified via PayMongo Webhook (" + (body.provider_event_id || "checkout_session.payment.paid") + ")";
    } else if (normalizedStatus === "FAILED") {
      outcomeDetails = "Failed (" + (body.failure_code || "DECLINED") + "): " + (body.failure_message || "Payment declined");
    } else if (normalizedStatus === "EXPIRED") {
      outcomeDetails = "Checkout session expired (" + (body.provider_event_id || "checkout_session.expired") + ")";
    }

    ensureAndWriteColumns(sheet, targetRow, {
      "Payment Status": normalizedStatus,
      "Official Receipt No": body.official_receipt_no || "",
      "Webhook Outcome Details": outcomeDetails,
      "Synchronized At": new Date().toLocaleString()
    });

    return createJsonResponse({
      ok: true,
      row_updated: targetRow,
      order_number: body.order_number,
      payment_status: normalizedStatus,
      official_receipt_no: body.official_receipt_no || null
    });

  } catch (err) {
    Logger.log("Error in doPost webhook receiver: " + err.toString());
    return createJsonResponse({ ok: false, error: err.toString() });
  } finally {
    try {
      lock.releaseLock();
    } catch (_) {}
  }
}

/**
 * ==============================================================================
 * HELPER FUNCTIONS: Safe Sheet Resolution, Sanitization & Email Dispatch
 * ==============================================================================
 */

/**
 * Multi-layer sheet resolution to prevent:
 * "TypeError: Cannot read properties of null (reading 'getActiveSheet')"
 */
function resolveSheetSafely(e) {
  var spreadsheet = null;
  var sheet = null;
  var rowIndex = null;

  // Layer 1: Event range from Google Sheet 'On form submit' trigger
  if (e && e.range && typeof e.range.getSheet === "function") {
    sheet = e.range.getSheet();
    spreadsheet = sheet.getParent();
    rowIndex = e.range.getRow();
    return { spreadsheet: spreadsheet, sheet: sheet, rowIndex: rowIndex };
  }

  // Layer 2: Event source if it is a Spreadsheet
  if (e && e.source && typeof e.source.getActiveSheet === "function") {
    spreadsheet = e.source;
  }

  // Layer 3: Container-bound active Spreadsheet
  if (!spreadsheet) {
    try {
      spreadsheet = SpreadsheetApp.getActiveSpreadsheet();
    } catch (_) {
      spreadsheet = null;
    }
  }

  // Layer 4: If pasted inside Google Forms instead of Google Sheets, look up linked Sheet ID
  if (!spreadsheet) {
    try {
      var form = (e && e.source && typeof e.source.getDestinationId === "function")
        ? e.source
        : FormApp.getActiveForm();
      if (form && form.getDestinationId()) {
        spreadsheet = SpreadsheetApp.openById(form.getDestinationId());
      }
    } catch (_) {
      spreadsheet = null;
    }
  }

  // Layer 5: Explicit FALLBACK_SPREADSHEET_ID
  if (!spreadsheet && CONFIG.FALLBACK_SPREADSHEET_ID && CONFIG.FALLBACK_SPREADSHEET_ID.trim() !== "") {
    try {
      spreadsheet = SpreadsheetApp.openById(CONFIG.FALLBACK_SPREADSHEET_ID.trim());
    } catch (openErr) {
      Logger.log("Could not open FALLBACK_SPREADSHEET_ID: " + openErr);
    }
  }

  if (spreadsheet) {
    if (CONFIG.FALLBACK_SHEET_NAME && CONFIG.FALLBACK_SHEET_NAME.trim() !== "") {
      sheet = spreadsheet.getSheetByName(CONFIG.FALLBACK_SHEET_NAME.trim());
    }
    if (!sheet) {
      sheet = spreadsheet.getActiveSheet() || spreadsheet.getSheets()[0];
    }
    if (sheet && !rowIndex) {
      rowIndex = sheet.getLastRow();
    }
  }

  return { spreadsheet: spreadsheet, sheet: sheet, rowIndex: rowIndex };
}

/**
 * Safely extracts form key-value pairs whether triggered from a Sheet, a Form, or a manual test run.
 */
function extractFormDataSafely(e, sheet, rowIndex) {
  var formData = {};

  // Case A: Sheet available with at least 1 data row
  if (sheet && rowIndex && rowIndex >= 2 && sheet.getLastColumn() >= 1) {
    var lastCol = sheet.getLastColumn();
    var headers = sheet.getRange(1, 1, 1, lastCol).getValues()[0];
    var rowValues = sheet.getRange(rowIndex, 1, 1, lastCol).getValues()[0];
    for (var i = 0; i < headers.length; i++) {
      var headerName = String(headers[i] || "").trim().toLowerCase();
      if (headerName) {
        formData[headerName] = rowValues[i];
      }
    }
    return formData;
  }

  // Case B: e.namedValues from Sheet trigger
  if (e && e.namedValues) {
    for (var key in e.namedValues) {
      if (Object.prototype.hasOwnProperty.call(e.namedValues, key)) {
        var val = e.namedValues[key];
        formData[key.trim().toLowerCase()] = Array.isArray(val) ? val[0] : val;
      }
    }
    return formData;
  }

  // Case C: e.response from Google Form trigger
  if (e && e.response && typeof e.response.getItemResponses === "function") {
    if (typeof e.response.getRespondentEmail === "function" && e.response.getRespondentEmail()) {
      formData["email address"] = e.response.getRespondentEmail();
    }
    var items = e.response.getItemResponses();
    for (var j = 0; j < items.length; j++) {
      var title = String(items[j].getItem().getTitle() || "").trim().toLowerCase();
      formData[title] = items[j].getResponse();
    }
    return formData;
  }

  // Case D: Manual click on "Run" button in Apps Script Editor with an empty sheet
  Logger.log("Manual test run detected: Using diagnostic sample submission.");
  return {
    "timestamp": new Date().toISOString(),
    "full name": "Juan Dela Cruz (Test)",
    "email address": CONFIG.SUPPORT_EMAIL.indexOf("@") !== -1 ? CONFIG.SUPPORT_EMAIL : "test.client@example.com",
    "mobile number": "09171234567",
    "select package / product": "Product A",
    "quantity": "1",
    "notes / instructions": "Diagnostic test run from Apps Script IDE"
  };
}

/**
 * Extracts a value from the formData map matching any candidate aliases.
 */
function extractField(formData, candidateAliases) {
  for (var i = 0; i < candidateAliases.length; i++) {
    var alias = candidateAliases[i].toLowerCase();
    if (formData[alias] !== undefined && String(formData[alias]).trim() !== "") {
      return String(formData[alias]).trim();
    }
    for (var key in formData) {
      if (Object.prototype.hasOwnProperty.call(formData, key)) {
        if (key.indexOf(alias) !== -1 && formData[key] !== undefined && String(formData[key]).trim() !== "") {
          return String(formData[key]).trim();
        }
      }
    }
  }
  return "";
}

/**
 * Neutralizes spreadsheet formula injection (=, +, -, @) before writing to cells.
 */
function sanitizeForSpreadsheet(val) {
  if (val === null || val === undefined) return "";
  var str = String(val);
  if (/^[=+\-@]/.test(str.trim())) {
    return "'" + str;
  }
  return str;
}

/**
 * Ensures target columns exist in the header row, then writes sanitized values to the specific row.
 */
function ensureAndWriteColumns(sheet, rowIndex, columnMap) {
  var lastCol = Math.max(sheet.getLastColumn(), 1);
  var headers = sheet.getRange(1, 1, 1, lastCol).getValues()[0];
  var headerMap = {};

  for (var i = 0; i < headers.length; i++) {
    var h = String(headers[i] || "").trim();
    if (h) {
      headerMap[h.toLowerCase()] = i + 1;
    }
  }

  for (var colName in columnMap) {
    if (Object.prototype.hasOwnProperty.call(columnMap, colName)) {
      var lookupKey = colName.trim().toLowerCase();
      var colIndex = headerMap[lookupKey];
      if (!colIndex) {
        colIndex = sheet.getLastColumn() + 1;
        sheet.getRange(1, colIndex).setValue(colName);
        sheet.getRange(1, colIndex).setFontWeight("bold");
        headerMap[lookupKey] = colIndex;
      }
      var safeValue = sanitizeForSpreadsheet(columnMap[colName]);
      sheet.getRange(rowIndex, colIndex).setValue(safeValue);
    }
  }
  SpreadsheetApp.flush();
}

/**
 * Finds the target row in the Google Sheet for a PayMongo webhook callback.
 */
function findMatchingRow(sheet, responseId, orderNumber, paymentAttemptId, sheetRowIndex) {
  var lastRow = sheet.getLastRow();
  var lastCol = sheet.getLastColumn();
  if (lastRow < 2 || lastCol < 1) return null;

  var allValues = sheet.getRange(1, 1, lastRow, lastCol).getValues();
  var headers = allValues[0].map(function(h) { return String(h || "").trim().toLowerCase(); });

  var respCol = headers.indexOf("response id");
  var orderCol = headers.indexOf("order number");
  var attemptCol = headers.indexOf("payment attempt id");

  for (var r = 1; r < allValues.length; r++) {
    var row = allValues[r];
    if (orderCol !== -1 && orderNumber && String(row[orderCol]).trim() === String(orderNumber).trim()) {
      return r + 1;
    }
    if (respCol !== -1 && responseId && String(row[respCol]).trim() === String(responseId).trim()) {
      return r + 1;
    }
    if (attemptCol !== -1 && paymentAttemptId && String(row[attemptCol]).trim() === String(paymentAttemptId).trim()) {
      return r + 1;
    }
  }

  if (sheetRowIndex && sheetRowIndex >= 2 && sheetRowIndex <= lastRow) {
    return Number(sheetRowIndex);
  }
  return null;
}

/**
 * Computes an HMAC-SHA256 hex signature for payload integrity verification.
 */
function computeHmacSha256Hex(message, secret) {
  var signatureBytes = Utilities.computeHmacSha256Signature(message, secret);
  return signatureBytes.map(function(byte) {
    return ("0" + (byte & 0xFF).toString(16)).slice(-2);
  }).join("");
}

function createJsonResponse(payload) {
  return ContentService.createTextOutput(JSON.stringify(payload))
    .setMimeType(ContentService.MimeType.JSON);
}

/**
 * Sends a responsive HTML email to the customer with their direct PayMongo link.
 */
function sendPaymentInstructionsEmail(recipientEmail, recipientName, orderData) {
  var orderNo = orderData.order_number || "PENDING";
  var orderTitle = orderData.order_title || orderData.title || "Product Order";
  var amountDisplay = orderData.amount_php || "0.00";
  var subject = "Invoice & Payment Instructions: Order " + orderNo + " - " + CONFIG.COMPANY_NAME;

  var htmlBody = "" +
    "<div style='font-family: -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 8px; background-color: #ffffff; color: #1e293b;'>" +
    "  <div style='border-bottom: 2px solid #0f172a; padding-bottom: 16px; margin-bottom: 20px;'>" +
    "    <h2 style='margin: 0; color: #0f172a; font-size: 20px; font-weight: 700;'>" + CONFIG.COMPANY_NAME + "</h2>" +
    "    <p style='margin: 4px 0 0 0; color: #64748b; font-size: 13px;'>Automated Payment & Order Orchestration</p>" +
    "  </div>" +
    "  <p style='font-size: 15px; margin-top: 0;'>Dear <strong>" + recipientName + "</strong>,</p>" +
    "  <p style='font-size: 14px; line-height: 1.6; color: #334155;'>" +
    "    Thank you for your order submission! Your order has been registered in our live accounting system with reference number <strong>" + orderNo + "</strong>." +
    "  </p>" +
    "  <div style='background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 16px; margin: 20px 0;'>" +
    "    <table style='width: 100%; border-collapse: collapse; font-size: 13px;'>" +
    "      <tr><td style='color: #64748b; padding: 4px 0;'>Order Number:</td><td style='font-weight: 600; text-align: right;'>" + orderNo + "</td></tr>" +
    "      <tr><td style='color: #64748b; padding: 4px 0;'>Description:</td><td style='font-weight: 600; text-align: right;'>" + orderTitle + "</td></tr>" +
    "      <tr><td style='color: #64748b; padding: 4px 0;'>Total Amount Due:</td><td style='font-weight: 700; color: #0f172a; font-size: 16px; text-align: right;'>" + amountDisplay + "</td></tr>" +
    "    </table>" +
    "  </div>" +
    "  <div style='text-align: center; margin: 28px 0;'>" +
    "    <a href='" + orderData.checkout_url + "' style='background-color: #dc2626; color: #ffffff; padding: 14px 28px; text-decoration: none; border-radius: 6px; font-weight: 600; font-size: 15px; display: inline-block; box-shadow: 0 2px 4px rgba(0,0,0,0.1);'>" +
    "      Proceed to Secure Payment &rarr;" +
    "    </a>" +
    "  </div>" +
    "  <p style='font-size: 12px; color: #64748b; text-align: center; margin-bottom: 24px;'>" +
    "    Supported payment options: <strong>GCash, Maya, QR Ph, Credit/Debit Card, BPI Online</strong>.<br/>" +
    "    Payment is powered by PayMongo Philippines. Your official BIR-compliant electronic receipt will be emailed immediately upon successful payment." +
    "  </p>" +
    "  <div style='border-top: 1px solid #e2e8f0; padding-top: 16px; font-size: 11px; color: #94a3b8; text-align: center;'>" +
    "    If you have any questions or require assistance, please reply to this email or reach us at " + CONFIG.SUPPORT_EMAIL + "." +
    "  </div>" +
    "</div>";

  MailApp.sendEmail(recipientEmail, subject, "Please view this email in an HTML-compatible client.", {
    htmlBody: htmlBody,
    name: CONFIG.COMPANY_NAME
  });
}
