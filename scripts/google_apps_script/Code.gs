/**
 * ==============================================================================
 * R.G.E ACCOUNTING FIRM INC. - HIGH-CONCURRENCY GOOGLE FORMS TO PAYMONGO
 * ==============================================================================
 * File: scripts/google_apps_script/Code.gs
 *
 * Squash Fix:
 *  - Eliminates "TypeError: Cannot read properties of null (reading 'getActiveSheet')"
 *
 * Supported Execution Environments:
 *  1. Google Sheet Container-Bound Trigger (From spreadsheet -> On form submit)
 *  2. Google Form Container-Bound Trigger (From form -> On form submit)
 *  3. Standalone Apps Script Project (Using CONFIG.SPREADSHEET_ID)
 *  4. Manual IDE "Run" execution (Toolbar Run button where e is undefined)
 *  5. Web App Reverse Webhook (doPost for real-time PayMongo sync back to Sheet)
 * ==============================================================================
 */

var CONFIG = {
  // 1. Authoritative Backend Intake Endpoint (NGINX / Cloudflare / PaaS)
  BACKEND_URL: "https://rge-accounting-3aa707d5f496.herokuapp.com/api/v1/integrations/google-forms/submit",

  // 2. Shared secret matching GOOGLE_FORMS_SECRET in root .env file
  GOOGLE_FORMS_SECRET: "rge_gforms_live_sec_89f3a21",
  SECRET_KEY: "rge_gforms_live_sec_89f3a21", // Backward-compatible alias

  // 3. Optional: Paste Google Sheet ID here if running script outside the Sheet
  // (From Sheet URL: /spreadsheets/d/THIS_ID/edit). Leave empty ("") if container-bound.
  SPREADSHEET_ID: "",

  // 4. Optional: Target tab name. Leave empty ("") to use active/first tab.
  SHEET_TAB_NAME: "",

  // 5. Branded company configuration and email notifications
  COMPANY_NAME: "R.G.E Accounting Firm Inc.",
  SUPPORT_EMAIL: "raymundgerardrestaca@gmail.com",
  SEND_CUSTOMER_EMAIL: true
};

/**
 * Main Trigger Function: Runs automatically when a Google Form is submitted,
 * or safely when clicking "Run" in the Apps Script editor.
 *
 * @param {Object} [e] - Form submit event object (may be undefined in manual IDE runs).
 */
function onFormSubmit(e) {
  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(30000); // Wait up to 30s for concurrent execution safety
  } catch (lockErr) {
    Logger.log("Lock acquisition notice: " + lockErr);
  }

  try {
    // Step A: Safely resolve target Google Sheet without throwing null pointer errors
    var sheetContext = resolveTargetSheet(e);
    var sheet = sheetContext.sheet;

    // Step B: Extract form fields safely from Sheet trigger, Form trigger, or Manual run
    var parsed = parseSubmissionData(e, sheetContext);
    Logger.log("Parsed submission payload: " + JSON.stringify(parsed));

    // Step C: Send authoritative intake request to Cloud FastAPI Backend
    var requestOptions = {
      method: "post",
      contentType: "application/json",
      headers: {
        "X-Google-Forms-Secret": CONFIG.GOOGLE_FORMS_SECRET,
        "Accept": "application/json"
      },
      payload: JSON.stringify(parsed.payload),
      muteHttpExceptions: true
    };

    var response = UrlFetchApp.fetch(CONFIG.BACKEND_URL, requestOptions);
    var statusCode = response.getResponseCode();
    var responseText = response.getContentText();

    if (statusCode < 200 || statusCode >= 300) {
      throw new Error("Backend API returned HTTP " + statusCode + ": " + responseText);
    }

    var result = JSON.parse(responseText);
    Logger.log("Order created successfully: " + JSON.stringify(result));

    // Step D: Write Order Number, PayMongo Link, Amount & Status back to Sheet
    if (sheet && parsed.rowNumber && parsed.rowNumber >= 2) {
      writeOrderResultToSheet(sheet, parsed.rowNumber, result);
    } else {
      Logger.log("Notice: Skipping sheet write-back because no active sheet row was bound.");
    }

    // Step E: Send branded HTML email with direct PayMongo link to customer
    if (CONFIG.SEND_CUSTOMER_EMAIL && parsed.payload.customer_email) {
      sendCustomerPaymentEmail(parsed.payload, result);
    }

  } catch (err) {
    Logger.log("Unhandled Exception in onFormSubmit: " + (err && err.stack ? err.stack : err));
  } finally {
    try {
      lock.releaseLock();
    } catch (_) {
      // ignore
    }
  }
}

/**
 * 5-Layer Sheet Resolution:
 * Safely locates the target Google Sheet across all trigger types and manual runs.
 * Prevents "TypeError: Cannot read properties of null (reading 'getActiveSheet')".
 *
 * @param {Object} [e] - Event object
 * @returns {Object} { spreadsheet, sheet, rowNumber }
 */
function resolveTargetSheet(e) {
  var spreadsheet = null;
  var sheet = null;
  var rowNumber = null;

  // Layer 1: Best case - Triggered from a Google Sheet trigger where e.range exists
  if (e && e.range && typeof e.range.getSheet === "function") {
    try {
      sheet = e.range.getSheet();
      spreadsheet = sheet.getParent();
      rowNumber = e.range.getRow();
      return { spreadsheet: spreadsheet, sheet: sheet, rowNumber: rowNumber };
    } catch (l1Err) {
      Logger.log("Layer 1 resolution warning: " + l1Err);
    }
  }

  // Layer 2: Check if e.source is a Spreadsheet instance
  if (e && e.source && typeof e.source.getActiveSheet === "function") {
    spreadsheet = e.source;
  }

  // Layer 3: Check container-bound active spreadsheet
  if (!spreadsheet) {
    try {
      spreadsheet = SpreadsheetApp.getActiveSpreadsheet();
    } catch (_) {
      spreadsheet = null;
    }
  }

  // Layer 4: Form linked destination detection (if script is attached to Google Form)
  if (!spreadsheet) {
    try {
      var form = null;
      if (e && e.source && typeof e.source.getDestinationId === "function") {
        form = e.source;
      } else if (typeof FormApp !== "undefined" && FormApp.getActiveForm) {
        form = FormApp.getActiveForm();
      }

      if (form && typeof form.getDestinationId === "function") {
        var destinationId = form.getDestinationId();
        if (destinationId) {
          spreadsheet = SpreadsheetApp.openById(destinationId);
        }
      }
    } catch (l4Err) {
      Logger.log("Layer 4 Form destination check: " + l4Err);
      spreadsheet = null;
    }
  }

  // Layer 5: Fallback to explicit SPREADSHEET_ID in CONFIG
  if (!spreadsheet && CONFIG.SPREADSHEET_ID && CONFIG.SPREADSHEET_ID.trim() !== "") {
    try {
      spreadsheet = SpreadsheetApp.openById(CONFIG.SPREADSHEET_ID.trim());
    } catch (openErr) {
      Logger.log("Warning: Could not open CONFIG.SPREADSHEET_ID: " + openErr);
    }
  }

  // Resolve specific tab within the spreadsheet
  if (spreadsheet) {
    try {
      if (CONFIG.SHEET_TAB_NAME && CONFIG.SHEET_TAB_NAME.trim() !== "") {
        sheet = spreadsheet.getSheetByName(CONFIG.SHEET_TAB_NAME.trim());
      }
      if (!sheet) {
        sheet = spreadsheet.getActiveSheet() || spreadsheet.getSheets()[0];
      }
      if (sheet && !rowNumber) {
        rowNumber = sheet.getLastRow();
      }
    } catch (tabErr) {
      Logger.log("Warning: Tab resolution notice: " + tabErr);
    }
  }

  return { spreadsheet: spreadsheet, sheet: sheet, rowNumber: rowNumber };
}

/**
 * Extracts and maps submission data across:
 *  - Google Sheets onFormSubmit(e) [e.namedValues]
 *  - Google Forms onFormSubmit(e)  [e.response]
 *  - Manual IDE execution          [e === undefined]
 *
 * @param {Object} [e]
 * @param {Object} sheetContext
 * @returns {Object} { rowNumber, payload }
 */
function parseSubmissionData(e, sheetContext) {
  var rawFields = {};
  var rowNumber = sheetContext.rowNumber || null;
  var responseId = "";

  // Case 1: Google Sheets Trigger (e.namedValues)
  if (e && e.namedValues) {
    for (var key in e.namedValues) {
      if (Object.prototype.hasOwnProperty.call(e.namedValues, key)) {
        var valArray = e.namedValues[key];
        var valStr = Array.isArray(valArray) ? (valArray[0] || "") : (valArray || "");
        rawFields[key.trim().toLowerCase()] = String(valStr).trim();
      }
    }
  }
  // Case 2: Google Forms Trigger (e.response)
  else if (e && e.response && typeof e.response.getItemResponses === "function") {
    responseId = String(e.response.getId ? e.response.getId() : "");
    if (typeof e.response.getRespondentEmail === "function" && e.response.getRespondentEmail()) {
      rawFields["email address"] = String(e.response.getRespondentEmail()).trim();
    }
    var itemResponses = e.response.getItemResponses();
    for (var i = 0; i < itemResponses.length; i++) {
      var itemTitle = itemResponses[i].getItem().getTitle().trim().toLowerCase();
      var answer = String(itemResponses[i].getResponse() || "").trim();
      rawFields[itemTitle] = answer;
    }
  }
  // Case 3: Manual Run in Apps Script Editor (reads the latest data row if sheet exists)
  else if (sheetContext.sheet && sheetContext.sheet.getLastRow() >= 2) {
    var s = sheetContext.sheet;
    rowNumber = s.getLastRow();
    var lastCol = Math.max(s.getLastColumn(), 1);
    var headers = s.getRange(1, 1, 1, lastCol).getValues()[0];
    var rowValues = s.getRange(rowNumber, 1, 1, lastCol).getValues()[0];

    for (var c = 0; c < headers.length; c++) {
      var headerName = String(headers[c] || "").trim().toLowerCase();
      if (headerName) {
        rawFields[headerName] = String(rowValues[c] || "").trim();
      }
    }
    Logger.log("Manual IDE run: Loaded row " + rowNumber + " from sheet '" + s.getName() + "'.");
  }
  // Case 4: Manual Run with no sheet bound -> Safe diagnostic test payload
  else {
    Logger.log("Manual IDE run with no active sheet: Using diagnostic test payload.");
    rawFields = {
      "timestamp": new Date().toISOString(),
      "email address": "customer.test@example.com",
      "full name": "Juan Dela Cruz",
      "contact number": "09171234567",
      "product": "Product A",
      "quantity": "1",
      "notes": "Automated IDE Diagnostic Test Run"
    };
  }

  // Flexible alias lookup helper
  function pickField(candidates, defaultValue) {
    // 1. Exact match
    for (var i = 0; i < candidates.length; i++) {
      var k = candidates[i].toLowerCase();
      if (rawFields[k] !== undefined && rawFields[k] !== "") {
        return rawFields[k];
      }
    }
    // 2. Substring match fallback
    var keys = Object.keys(rawFields);
    for (var j = 0; j < candidates.length; j++) {
      var needle = candidates[j].toLowerCase();
      for (var m = 0; m < keys.length; m++) {
        if (keys[m].indexOf(needle) !== -1 && rawFields[keys[m]] !== "") {
          return rawFields[keys[m]];
        }
      }
    }
    return defaultValue;
  }

  var timestamp = pickField(["timestamp", "date", "submission time"], new Date().toISOString());
  var customerEmail = pickField(["email address", "email", "customer email", "e-mail", "your email", "contact email"], "");
  var customerName = pickField(["full name", "name", "customer name", "requestor", "your name", "client name"], "Valued Customer");
  var customerPhone = pickField(["phone", "mobile", "mobile number", "contact number", "cellphone"], "");
  var productSelection = pickField(["product", "package", "reference_code", "item", "order item", "service", "item_reference"], "Product A");
  var quantityRaw = parseInt(pickField(["quantity", "qty", "units", "number of items"], "1"), 10);
  var quantity = isNaN(quantityRaw) || quantityRaw < 1 ? 1 : quantityRaw;
  var notes = pickField(["notes", "remarks", "instructions", "comments"], "");
  var title = pickField(["title", "order title", "purpose", "description"], productSelection + " (Qty: " + quantity + ") - " + customerName);

  // Deterministic response ID generation for atomic idempotency
  if (!responseId) {
    var rawKey = [timestamp, customerEmail, productSelection, rowNumber || "manual"].join("|");
    var digest = Utilities.computeDigest(Utilities.DigestAlgorithm.SHA_256, rawKey);
    responseId = "gforms_" + digest.map(function(b) {
      return ("0" + (b & 0xFF).toString(16)).slice(-2);
    }).join("").slice(0, 24);
  }

  return {
    rowNumber: rowNumber,
    payload: {
      response_id: responseId,
      sheet_row_index: rowNumber,
      sheet_row: rowNumber,
      customer_name: customerName,
      customer_email: customerEmail,
      customer_phone: customerPhone || null,
      reference_code: productSelection,
      item_reference: productSelection,
      title: title,
      category: "product_order",
      quantity: quantity,
      notes: notes,
      raw_answers: rawFields
    }
  };
}

/**
 * Writes Order Number, PayMongo Checkout URL, Amount, and Status back to the Sheet row.
 *
 * @param {Sheet} sheet
 * @param {number} rowNumber
 * @param {Object} result
 */
function writeOrderResultToSheet(sheet, rowNumber, result) {
  var headers = sheet.getRange(1, 1, 1, Math.max(sheet.getLastColumn(), 1)).getValues()[0];
  var columnsToEnsure = [
    { header: "Order Number", value: result.order_number || "" },
    { header: "PayMongo Checkout Link", value: result.checkout_url || "" },
    { header: "Amount (PHP)", value: result.amount_php || "" },
    { header: "Payment Status", value: (result.status || "pending").toUpperCase() },
    { header: "Payment Attempt ID", value: result.payment_attempt_id || "" },
    { header: "Synchronized At", value: new Date().toISOString() }
  ];

  columnsToEnsure.forEach(function(col) {
    var colIndex = -1;
    for (var i = 0; i < headers.length; i++) {
      if (String(headers[i] || "").trim().toLowerCase() === col.header.toLowerCase()) {
        colIndex = i;
        break;
      }
    }
    if (colIndex === -1) {
      colIndex = headers.length;
      headers.push(col.header);
      sheet.getRange(1, colIndex + 1).setValue(col.header).setFontWeight("bold");
    }
    sheet.getRange(rowNumber, colIndex + 1).setValue(col.value);
  });

  SpreadsheetApp.flush();
}

/**
 * Web App Reverse Webhook Endpoint (doPost):
 * Called automatically by the backend when PayMongo webhooks confirm payment
 * (PAID, FAILED, EXPIRED), updating the sheet row live.
 *
 * Deployment: Deploy -> New deployment -> Select type: Web app
 * Execute as: Me
 * Who has access: Anyone
 */
function doPost(e) {
  try {
    if (!e || !e.postData || !e.postData.contents) {
      return ContentService.createTextOutput(JSON.stringify({
        status: "error",
        message: "Empty postData contents."
      })).setMimeType(ContentService.MimeType.JSON);
    }

    var data = JSON.parse(e.postData.contents);

    // Validate shared secret
    var secret = data.secret || "";
    if (secret !== CONFIG.GOOGLE_FORMS_SECRET && secret !== CONFIG.SECRET_KEY) {
      return ContentService.createTextOutput(JSON.stringify({
        status: "unauthorized",
        message: "Invalid secret."
      })).setMimeType(ContentService.MimeType.JSON);
    }

    var sheetContext = resolveTargetSheet(null);
    var sheet = sheetContext.sheet;
    if (!sheet) {
      return ContentService.createTextOutput(JSON.stringify({
        status: "error",
        message: "No connected Google Sheet resolved."
      })).setMimeType(ContentService.MimeType.JSON);
    }

    // Locate target row by Order Number or Response ID
    var targetRow = findRowByReference(sheet, data.order_number, data.response_id);
    if (targetRow > 0) {
      var updates = {
        "Payment Status": (data.payment_status || "PAID").toUpperCase(),
        "Official Receipt No": data.official_receipt_no || "",
        "Failure Reason": data.failure_reason || "",
        "Synchronized At": data.updated_at || new Date().toISOString()
      };

      writeColumnsToRow(sheet, targetRow, updates);
      SpreadsheetApp.flush();

      return ContentService.createTextOutput(JSON.stringify({
        status: "success",
        updated_row: targetRow,
        order_number: data.order_number,
        payment_status: data.payment_status
      })).setMimeType(ContentService.MimeType.JSON);
    }

    return ContentService.createTextOutput(JSON.stringify({
      status: "not_found",
      message: "Order or response ID not found in sheet rows."
    })).setMimeType(ContentService.MimeType.JSON);

  } catch (err) {
    Logger.log("doPost exception: " + err);
    return ContentService.createTextOutput(JSON.stringify({
      status: "error",
      message: (err && err.message ? err.message : String(err))
    })).setMimeType(ContentService.MimeType.JSON);
  }
}

/**
 * Health check endpoint for Web App verification.
 */
function doGet(e) {
  return ContentService.createTextOutput(JSON.stringify({
    status: "ok",
    service: "R.G.E Google Forms to PayMongo Orchestrator",
    company: CONFIG.COMPANY_NAME,
    timestamp: new Date().toISOString()
  })).setMimeType(ContentService.MimeType.JSON);
}

/**
 * Helper to locate a sheet row matching either order_number or response_id.
 */
function findRowByReference(sheet, orderNumber, responseId) {
  var lastRow = sheet.getLastRow();
  var lastCol = Math.max(sheet.getLastColumn(), 1);
  if (lastRow < 2) return -1;

  var data = sheet.getRange(1, 1, lastRow, lastCol).getValues();
  var headers = data[0];

  var orderColIdx = -1;
  var responseIdColIdx = -1;

  for (var c = 0; c < headers.length; c++) {
    var h = String(headers[c] || "").trim().toLowerCase();
    if (h === "order number" || h === "order_number") orderColIdx = c;
    if (h === "response id" || h === "response_id" || h === "reference id") responseIdColIdx = c;
  }

  for (var r = 1; r < data.length; r++) {
    var rowValues = data[r];
    if (orderNumber && orderColIdx !== -1 && String(rowValues[orderColIdx]).trim() === String(orderNumber).trim()) {
      return r + 1; // 1-indexed
    }
    if (responseId && responseIdColIdx !== -1 && String(rowValues[responseIdColIdx]).trim() === String(responseId).trim()) {
      return r + 1;
    }
  }

  // Fallback: search all cells in the row for orderNumber
  if (orderNumber) {
    for (var r2 = 1; r2 < data.length; r2++) {
      for (var c2 = 0; c2 < data[r2].length; c2++) {
        if (String(data[r2][c2] || "").trim() === String(orderNumber).trim()) {
          return r2 + 1;
        }
      }
    }
  }

  return -1;
}

/**
 * Ensures columns exist in header and writes dictionary values to target row.
 */
function writeColumnsToRow(sheet, rowIndex, colValues) {
  var headers = sheet.getRange(1, 1, 1, Math.max(sheet.getLastColumn(), 1)).getValues()[0];
  var headerMap = {};
  for (var i = 0; i < headers.length; i++) {
    headerMap[String(headers[i] || "").trim().toLowerCase()] = i + 1;
  }

  for (var key in colValues) {
    var lowerKey = key.trim().toLowerCase();
    var colIdx = headerMap[lowerKey];
    if (!colIdx) {
      colIdx = sheet.getLastColumn() + 1;
      sheet.getRange(1, colIdx).setValue(key).setFontWeight("bold");
      headerMap[lowerKey] = colIdx;
    }
    sheet.getRange(rowIndex, colIdx).setValue(colValues[key]);
  }
}

/**
 * Sends a responsive HTML email with the PayMongo Checkout Link to the customer.
 */
function sendCustomerPaymentEmail(submissionPayload, result) {
  if (!result.checkout_url) return;

  var subject = "Invoice & Payment Instructions: Order " + (result.order_number || "") + " - " + CONFIG.COMPANY_NAME;
  var htmlBody =
    "<div style='font-family: -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, Helvetica, Arial, sans-serif; max-width: 580px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 8px; background-color: #ffffff; color: #1e293b;'>" +
      "<div style='border-bottom: 2px solid #0f172a; padding-bottom: 16px; margin-bottom: 20px;'>" +
        "<h2 style='margin: 0; color: #0f172a; font-size: 20px; font-weight: 700;'>" + CONFIG.COMPANY_NAME + "</h2>" +
        "<p style='margin: 4px 0 0 0; color: #64748b; font-size: 13px;'>Automated Payment & Order Orchestration</p>" +
      "</div>" +
      "<p style='font-size: 15px; margin-top: 0;'>Hello <strong>" + submissionPayload.customer_name + "</strong>,</p>" +
      "<p style='font-size: 14px; line-height: 1.6; color: #334155;'>" +
        "We have received your order submission for <strong>" + submissionPayload.reference_code + "</strong>. " +
        "Your order reference is <strong>" + (result.order_number || "") + "</strong>." +
      "</p>" +
      "<div style='background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; padding: 16px; margin: 20px 0;'>" +
        "<table style='width: 100%; border-collapse: collapse; font-size: 13px;'>" +
          "<tr><td style='color: #64748b; padding: 4px 0;'>Order Number:</td><td style='font-weight: 600; text-align: right; font-family: monospace;'>" + (result.order_number || "") + "</td></tr>" +
          "<tr><td style='color: #64748b; padding: 4px 0;'>Description:</td><td style='font-weight: 600; text-align: right;'>" + (result.order_title || submissionPayload.title) + "</td></tr>" +
          "<tr><td style='color: #64748b; padding: 4px 0;'>Total Amount Due:</td><td style='font-weight: 700; color: #0f172a; font-size: 16px; text-align: right;'>" + (result.amount_php || "PHP 0.00") + "</td></tr>" +
        "</table>" +
      "</div>" +
      "<div style='text-align: center; margin: 28px 0;'>" +
        "<a href='" + result.checkout_url + "' style='background-color: #dc2626; color: #ffffff; padding: 14px 28px; text-decoration: none; border-radius: 6px; font-weight: bold; font-size: 15px; display: inline-block; box-shadow: 0 2px 4px rgba(0,0,0,0.1);'>" +
          "Pay Now via PayMongo (QR Ph / GCash / Maya / Card)" +
        "</a>" +
      "</div>" +
      "<p style='font-size: 12px; color: #64748b; text-align: center; line-height: 1.5; margin-bottom: 24px;'>" +
        "Supported channels: <strong>QR Ph, GCash, Maya, Visa, Mastercard, BPI Online</strong>.<br/>" +
        "Upon settlement, your official receipt will be generated and synchronized automatically." +
      "</p>" +
      "<div style='border-top: 1px solid #e2e8f0; padding-top: 16px; font-size: 11px; color: #94a3b8; text-align: center;'>" +
        "Reference ID: " + submissionPayload.response_id + " &middot; Need help? Contact " + CONFIG.SUPPORT_EMAIL +
      "</div>" +
    "</div>";

  try {
    MailApp.sendEmail({
      to: submissionPayload.customer_email,
      subject: subject,
      htmlBody: htmlBody
    });
  } catch (mailErr) {
    Logger.log("MailApp notice: " + mailErr);
  }
}
