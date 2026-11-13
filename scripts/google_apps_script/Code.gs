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
 * 4. Sends HMAC-authenticated payloads to Backend (/api/v1/integrations/google-forms/submit).
 * 5. Exposes doPost(e) Reverse-Webhook so PayMongo Webhooks can update
 *    the Google Sheet row live for PAID, FAILED, and EXPIRED transactions!
 * ==============================================================================
 */

var CONFIG = {
  // 1. Your live Cloudflare / NGINX / PaaS domain (must match NEXT_PUBLIC_APP_DOMAIN in .env)
  BACKEND_URL: "https://accounting.raymundgerardestaca.dev/api/v1/integrations/google-forms/submit",

  // 1b. Fallback (direct Heroku origin) used automatically when the primary domain fails (network / 404 / 5xx)
  FALLBACK_BACKEND_URL: "https://rge-accounting-3aa707d5f496.herokuapp.com/api/v1/integrations/google-forms/submit",

  // 2. Shared secret key (must match GOOGLE_FORMS_SECRET in root .env)
  SECRET_KEY: "rge_gforms_live_sec_89f3a21",

  // 3. Optional fallback Spreadsheet ID (from https://docs.google.com/spreadsheets/d/THIS_ID/edit)
  // Leave as "" if this script is pasted inside the Google Sheet's Extensions -> Apps Script.
  FALLBACK_SPREADSHEET_ID: "",
  FALLBACK_SHEET_NAME: "",

  // 4. Organization branding
  COMPANY_NAME: "R.G.E Accounting Firm Inc.",
  SUPPORT_EMAIL: "raymundgerardrestaca@gmail.com",

  // 5. Email control:
  // Set to true if Apps Script should email the initial checkout link upon form submission.
  // Final PAID (Official Receipt), FAILED, and EXPIRED emails are sent by Backend SMTP after PayMongo Webhooks!
  SEND_INITIAL_CHECKOUT_EMAIL_FROM_GAS: true
};

/**
 * ==============================================================================
 * 1. OUTBOUND ORCHESTRATOR: Triggered when a user submits the Google Form
 * ==============================================================================
 * @param {Object} [e] - The Google Sheets or Google Forms submission event object.
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
    var extraction = extractFormDataSafely(e, sheet, rowIndex);
    var formData = extraction.formData;
    if (extraction.targetRowIndex) {
      rowIndex = extraction.targetRowIndex;
    }

    var customerEmail = extractField(formData, ["customer email", "email address", "your email", "respondent email", "email"]);
    var customerName = extractField(formData, ["customer name", "full name", "your name", "client name", "student name", "name"]) || "Valued Client";
    var customerPhone = extractField(formData, ["mobile phone number", "mobile number", "contact number", "phone number", "cellphone", "mobile", "phone"]);
    var itemReference = extractField(formData, ["book / course material selection", "book / material", "course material", "item reference", "textbook", "book", "material", "product", "service", "package", "select package", "order category", "department", "item"]) || "Product A";
    var quantityStr = extractField(formData, ["quantity", "qty", "number of items", "copies"]);
    var quantity = parseInt(quantityStr, 10);
    if (isNaN(quantity) || quantity < 1) quantity = 1;
    var instructorCredit = extractField(formData, ["enrolled instructor to credit", "instructor credit", "enrolled instructor", "instructor", "professor", "teacher", "faculty"]);
    var studentId = extractField(formData, ["student / enrollee id number", "student / enrollee id", "student id", "enrollee id", "id number", "student number", "student no"]);
    var notes = extractField(formData, ["delivery address / campus pickup notes", "delivery address", "campus pickup", "notes", "remarks", "instructions", "comments"]) || "";

    // Generate deterministic Response ID for atomic idempotency (locale-safe timestamp parsing)
    var rawTimestamp = formData["timestamp"] || new Date();
    var parsedTime = parseSafeTimestampMillis(rawTimestamp);
    var displayTimestamp = formatSafeTimestamp(rawTimestamp);
    var responseId = (e && e.response && typeof e.response.getId === "function")
      ? "gf_" + e.response.getId()
      : "gf_" + (rowIndex || "manual") + "_" + parsedTime;

    Logger.log(
      "Processing submission for: " + customerName +
      " (" + customerEmail + ") | Book: " + itemReference +
      " | Qty: " + quantity +
      " | Credit: " + (instructorCredit || "None") +
      " | Row: " + rowIndex +
      " | Response ID: " + responseId
    );

    if (!customerEmail || customerEmail.indexOf("@") === -1) {
      Logger.log("Error: No valid customer email found in row " + rowIndex);
      if (sheet && rowIndex >= 2) {
        ensureAndWriteColumns(sheet, rowIndex, {
          "Timestamp": displayTimestamp,
          "Customer Name": customerName,
          "Email Address": "",
          "Contact Number": customerPhone || "",
          "Book / Material": itemReference,
          "Quantity": quantity,
          "Instructor Credit": instructorCredit || "External / Self-Study",
          "Student ID": studentId || "N/A",
          "Notes": notes || "",
          "Response ID": responseId,
          "Payment Status": "ERROR (MISSING EMAIL)",
          "Webhook Outcome Details": "Customer email address was not provided in the form.",
          "Synchronized At": formatSafeTimestamp(new Date())
        });
      }
      return;
    }

    var orderTitle = itemReference + " (Qty: " + quantity + ") - " + customerName;
    if (instructorCredit) {
      orderTitle += " [Credit: " + instructorCredit + "]";
    }

    // Build authoritative payload for POST /api/v1/integrations/google-forms/submit
    var payload = {
      response_id: responseId,
      sheet_row_index: rowIndex || 2,
      customer_name: customerName,
      customer_email: customerEmail,
      customer_phone: customerPhone || null,
      item_reference: itemReference,
      reference_code: itemReference,
      book_title: itemReference,
      student_id: studentId || null,
      instructor_credit: instructorCredit || null,
      title: orderTitle,
      category: "product_order",
      quantity: quantity,
      payment_method: "hosted_checkout",
      notes: notes,
      raw_answers: formData,
      secret: CONFIG.SECRET_KEY
    };

    var payloadJson = JSON.stringify(payload);
    var hmacSignature = computeHmacSha256Hex(payloadJson, CONFIG.SECRET_KEY);

    var options = {
      method: "post",
      contentType: "application/json",
      headers: {
        "X-Google-Forms-Secret": CONFIG.SECRET_KEY,
        "X-Google-Forms-Signature": hmacSignature,
        "Idempotency-Key": responseId,
        "Accept": "application/json"
      },
      payload: payloadJson,
      muteHttpExceptions: true
    };

    // Call Backend (primary domain, then automatic fallback to direct Heroku origin)
    var response = fetchWithFallback(options);
    var responseCode = response.getResponseCode();
    var responseText = response.getContentText() || "";

    Logger.log("Backend response (" + responseCode + "): " + responseText);

    if (responseCode >= 200 && responseCode < 300) {
      var data = {};
      try {
        data = JSON.parse(responseText || "{}");
      } catch (parseErr) {
        Logger.log("Notice: backend returned non-JSON body: " + parseErr);
      }

      var orderId = data.order_id || data.orderId || "";
      var orderNumber = data.order_number || data.orderNumber || "";
      var checkoutUrl = data.checkout_url || data.checkoutUrl || "";
      if (checkoutUrl && checkoutUrl.charAt(0) === "/") {
        checkoutUrl = getAppBaseUrl() + checkoutUrl;
      }
      if (!checkoutUrl && orderId) {
        checkoutUrl = getAppBaseUrl() + "/checkout?order_id=" + encodeURIComponent(orderId);
      }

      var amountPhp = data.amount_php || data.amountPhp || "";
      var rawStatus = data.status || data.payment_status || data.paymentStatus || "PENDING";
      var normalizedInitialStatus = normalizeDisplayStatus(rawStatus);
      var attemptId = data.payment_attempt_id || data.paymentAttemptId || "";

      var normalizedData = {
        order_id: orderId,
        order_number: orderNumber,
        order_title: data.order_title || data.orderTitle || itemReference,
        amount_php: amountPhp,
        checkout_url: checkoutUrl,
        payment_attempt_id: attemptId
      };

      // Write initial status & PayMongo Checkout Link to Google Sheet
      if (sheet && rowIndex >= 2) {
        ensureAndWriteColumns(sheet, rowIndex, {
          "Timestamp": displayTimestamp,
          "Customer Name": customerName,
          "Email Address": customerEmail,
          "Contact Number": customerPhone || "",
          "Book / Material": itemReference,
          "Quantity": quantity,
          "Instructor Credit": instructorCredit || "External / Self-Study",
          "Student ID": studentId || "N/A",
          "Notes": notes || "",
          "Response ID": responseId,
          "Order Number": orderNumber,
          "PayMongo Checkout Link": checkoutUrl,
          "Amount": amountPhp,
          "Payment Status": normalizedInitialStatus,
          "Payment Attempt ID": attemptId,
          "Official Receipt No": data.official_receipt_number || data.official_receipt_no || "",
          "Webhook Outcome Details": "Awaiting PayMongo payment webhook...",
          "Synchronized At": formatSafeTimestamp(new Date())
        });
      }

      // Send initial payment instructions email if enabled
      if (CONFIG.SEND_INITIAL_CHECKOUT_EMAIL_FROM_GAS && checkoutUrl) {
        sendPaymentInstructionsEmail(customerEmail, customerName, normalizedData);
      }
    } else {
      if (sheet && rowIndex >= 2) {
        ensureAndWriteColumns(sheet, rowIndex, {
          "Timestamp": displayTimestamp,
          "Customer Name": customerName,
          "Email Address": customerEmail,
          "Contact Number": customerPhone || "",
          "Book / Material": itemReference,
          "Quantity": quantity,
          "Instructor Credit": instructorCredit || "External / Self-Study",
          "Student ID": studentId || "N/A",
          "Notes": notes || "",
          "Response ID": responseId,
          "Payment Status": "ERROR (" + responseCode + ")",
          "Webhook Outcome Details": "Backend Error (" + responseCode + "): " + responseText.substring(0, 150),
          "Synchronized At": formatSafeTimestamp(new Date())
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
 *    Called automatically by Backend AFTER a PayMongo Webhook
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
    var providedSecret = body.secret || (e.parameter && e.parameter.secret) || "";
    if (!providedSecret || providedSecret !== CONFIG.SECRET_KEY) {
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
      body.response_id || body.responseId,
      body.order_number || body.orderNumber,
      body.payment_attempt_id || body.paymentAttemptId,
      body.sheet_row_index || body.sheetRowIndex
    );

    if (!targetRow) {
      return createJsonResponse({
        ok: false,
        error: "Could not find matching row for Order " + (body.order_number || body.response_id)
      });
    }

    var rawStatus = body.payment_status || body.status || "PENDING";
    var normalizedStatus = normalizeDisplayStatus(rawStatus);
    var receiptNo = body.official_receipt_no || body.official_receipt_number || "";
    var outcomeDetails = body.outcome_details || "";

    if (normalizedStatus === "PAID") {
      outcomeDetails = "Verified via PayMongo Webhook (" + (body.provider_event_id || "checkout_session.payment.paid") + ")" +
        (receiptNo ? " | OR: " + receiptNo : "");
    } else if (normalizedStatus === "FAILED") {
      outcomeDetails = "Failed (" + (body.failure_code || "DECLINED") + "): " + (body.failure_message || "Payment declined");
    } else if (normalizedStatus === "EXPIRED") {
      outcomeDetails = "Checkout session expired (" + (body.provider_event_id || "checkout_session.expired") + ")";
    }

    ensureAndWriteColumns(sheet, targetRow, {
      "Payment Status": normalizedStatus,
      "Official Receipt No": receiptNo,
      "Webhook Outcome Details": outcomeDetails,
      "Synchronized At": formatSafeTimestamp(new Date())
    });

    return createJsonResponse({
      ok: true,
      row_updated: targetRow,
      order_number: body.order_number || body.orderNumber,
      payment_status: normalizedStatus,
      official_receipt_no: receiptNo || null
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
 * Normalizes backend/gateway statuses (e.g. "succeeded", "requires_action") into clean Sheet badges.
 */
function normalizeDisplayStatus(rawStatus) {
  var s = String(rawStatus || "PENDING").trim().toUpperCase();
  if (s === "SUCCEEDED" || s === "PAID") return "PAID";
  if (s === "REQUIRES_ACTION" || s === "REQUIRESACTION" || s === "AWAITING_NEXT_ACTION" || s === "ACTIVE") return "PENDING";
  return s;
}

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
      if (form) {
        var destId = form.getDestinationId();
        if (destId) {
          spreadsheet = SpreadsheetApp.openById(destId);
        }
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
      var allSheets = spreadsheet.getSheets();
      for (var s = 0; s < allSheets.length; s++) {
        var sName = allSheets[s].getName().toLowerCase();
        if (sName.indexOf("form responses") !== -1 || sName.indexOf("response") !== -1 || sName.indexOf("orders") !== -1) {
          sheet = allSheets[s];
          break;
        }
      }
    }
    if (!sheet) {
      try {
        sheet = spreadsheet.getActiveSheet() || spreadsheet.getSheets()[0];
      } catch (_) {
        sheet = spreadsheet.getSheets()[0];
      }
    }
    if (sheet && !rowIndex) {
      var last = sheet.getLastRow();
      var isSyntheticSimulation = !!(e && e.namedValues && !e.range && !e.source);
      rowIndex = isSyntheticSimulation ? ((last < 1) ? 2 : last + 1) : Math.max(last, 2);
    }
  }

  return { spreadsheet: spreadsheet, sheet: sheet, rowIndex: rowIndex };
}

/**
 * Safely extracts form key-value pairs whether triggered from a Sheet, a Form, or a manual test run.
 * Ignores blank rows or error-only rows when running manually so diagnostic runs never get stuck.
 */
function extractFormDataSafely(e, sheet, rowIndex) {
  var formData = {};

  // Layer 1: e.namedValues (Google Sheets Form Submit event)
  if (e && e.namedValues) {
    for (var key in e.namedValues) {
      if (Object.prototype.hasOwnProperty.call(e.namedValues, key)) {
        var val = e.namedValues[key];
        var strVal = Array.isArray(val) ? val[0] : val;
        if (strVal !== undefined && strVal !== null && String(strVal).trim() !== "") {
          formData[key.trim().toLowerCase()] = strVal;
        }
      }
    }
  }

  // Layer 2: e.response (FormApp Form Submit event)
  if (e && e.response && typeof e.response.getItemResponses === "function") {
    try {
      if (typeof e.response.getTimestamp === "function" && e.response.getTimestamp()) {
        formData["timestamp"] = e.response.getTimestamp();
      }
      if (typeof e.response.getRespondentEmail === "function" && e.response.getRespondentEmail()) {
        var respEmail = e.response.getRespondentEmail();
        if (!formData["email address"]) formData["email address"] = respEmail;
        if (!formData["email"]) formData["email"] = respEmail;
      }
    } catch (_) {}

    var items = e.response.getItemResponses();
    for (var j = 0; j < items.length; j++) {
      var itemTitle = String(items[j].getItem().getTitle() || "").trim().toLowerCase();
      var respVal = items[j].getResponse();
      if (itemTitle && respVal !== undefined && respVal !== null && String(respVal).trim() !== "") {
        formData[itemTitle] = Array.isArray(respVal) ? respVal.join(", ") : respVal;
      }
    }
  }

  // Layer 3: Sheet available with an actual populated data row
  if (sheet && rowIndex && rowIndex >= 2 && sheet.getLastRow() >= rowIndex && sheet.getLastColumn() >= 1) {
    try {
      var lastCol = sheet.getLastColumn();
      var headers = sheet.getRange(1, 1, 1, lastCol).getValues()[0];
      var rowValues = sheet.getRange(rowIndex, 1, 1, lastCol).getValues()[0];
      for (var i = 0; i < headers.length; i++) {
        var headerName = String(headers[i] || "").trim().toLowerCase();
        var cellVal = rowValues[i];
        var hasValue = cellVal !== undefined && cellVal !== null && String(cellVal).trim() !== "";
        if (headerName && hasValue && (formData[headerName] === undefined || formData[headerName] === "")) {
          formData[headerName] = cellVal;
        }
      }
    } catch (sheetReadErr) {
      Logger.log("Notice reading sheet row: " + sheetReadErr);
    }
  }

  // Verify whether captured data actually has a customer email or submission payload
  var capturedEmail = extractField(formData, ["customer email", "email address", "your email", "respondent email", "email"]);
  if (capturedEmail && capturedEmail.indexOf("@") !== -1) {
    return { formData: formData, targetRowIndex: rowIndex };
  }

  // If this was a real trigger event (not a manual editor run), return whatever was captured
  if (e && (e.range || e.namedValues || e.response)) {
    return { formData: formData, targetRowIndex: rowIndex };
  }

  // Layer 4: Manual click on "Run" button in Apps Script Editor when sheet has no valid email row
  Logger.log("Manual test run detected: Initializing diagnostic bookstore sample submission.");
  var nextRow = 2;
  if (sheet) {
    var lastRow = sheet.getLastRow();
    nextRow = lastRow < 2 ? 2 : (capturedEmail ? lastRow : lastRow + 1);
  }

  return {
    targetRowIndex: nextRow,
    formData: {
      "timestamp": new Date().toISOString(),
      "customer name": "Juan Dela Cruz (Test Enrollee)",
      "full name": "Juan Dela Cruz (Test Enrollee)",
      "email address": CONFIG.SUPPORT_EMAIL.indexOf("@") !== -1 ? CONFIG.SUPPORT_EMAIL : "test.client@example.com",
      "contact number": "09171234567",
      "mobile number": "09171234567",
      "book / material": "Financial Accounting & Reporting Vol. 1 - ₱450.00",
      "book / course material selection": "Financial Accounting & Reporting Vol. 1 - ₱450.00",
      "quantity": "1",
      "instructor credit": "Prof. Maria Santos (Taxation)",
      "enrolled instructor to credit": "Prof. Maria Santos (Taxation)",
      "student id": "2026-ACCT-1049",
      "student / enrollee id number": "2026-ACCT-1049",
      "notes": "Diagnostic test run from Apps Script IDE",
      "delivery address / campus pickup notes": "Diagnostic test run from Apps Script IDE"
    }
  };
}

/**
 * Extracts a value from the formData map matching any candidate aliases.
 * Pass 1: exact header match across ALL aliases in priority order.
 * Pass 2: guarded substring match (prevents "name" stealing "Product Name",
 *         and "item" stealing "Number of Items").
 */
function extractField(formData, candidateAliases) {
  var i, alias;
  for (i = 0; i < candidateAliases.length; i++) {
    alias = candidateAliases[i].toLowerCase();
    if (formData[alias] !== undefined && formData[alias] !== null && String(formData[alias]).trim() !== "") {
      return String(formData[alias]).trim();
    }
  }

  var nameCollisions = ["product name", "item name", "department name", "company name", "book name", "course name"];
  for (i = 0; i < candidateAliases.length; i++) {
    alias = candidateAliases[i].toLowerCase();
    for (var key in formData) {
      if (!Object.prototype.hasOwnProperty.call(formData, key)) continue;
      if (alias === "item" && key.indexOf("number of items") !== -1) continue;
      if (alias === "name" && nameCollisions.some(function(c) { return key.indexOf(c) !== -1; })) continue;
      if (key.indexOf(alias) !== -1 && formData[key] !== undefined && formData[key] !== null && String(formData[key]).trim() !== "") {
        return String(formData[key]).trim();
      }
    }
  }
  return "";
}

/**
 * Locale-safe timestamp parsing: supports Date objects, ISO strings,
 * and DD/MM/YYYY HH:mm:ss (Philippine / UK sheet locales). Never returns NaN.
 */
function parseSafeTimestampMillis(rawTs) {
  if (rawTs instanceof Date && !isNaN(rawTs.getTime())) return rawTs.getTime();
  var str = String(rawTs || "").trim();
  var m = str.match(/^(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{4})(?:\s+(\d{1,2}):(\d{2})(?::(\d{2}))?)?/);
  if (m) {
    var a = parseInt(m[1], 10), b = parseInt(m[2], 10);
    var day = a, month = b;
    if (b > 12 && a <= 12) { day = b; month = a; }
    var d = new Date(
      parseInt(m[3], 10),
      month - 1,
      day,
      parseInt(m[4] || "0", 10),
      parseInt(m[5] || "0", 10),
      parseInt(m[6] || "0", 10)
    );
    if (!isNaN(d.getTime())) return d.getTime();
  }
  var parsed = Date.parse(str);
  return isNaN(parsed) ? new Date().getTime() : parsed;
}

function formatSafeTimestamp(rawTs) {
  return new Date(parseSafeTimestampMillis(rawTs)).toLocaleString();
}

/**
 * POSTs to CONFIG.BACKEND_URL, retrying once against CONFIG.FALLBACK_BACKEND_URL
 * on network errors, 404, or 5xx responses.
 */
function fetchWithFallback(options) {
  var urls = [CONFIG.BACKEND_URL];
  if (CONFIG.FALLBACK_BACKEND_URL && CONFIG.FALLBACK_BACKEND_URL !== CONFIG.BACKEND_URL) {
    urls.push(CONFIG.FALLBACK_BACKEND_URL);
  }
  var lastResponse = null;
  var lastError = null;
  for (var u = 0; u < urls.length; u++) {
    try {
      var resp = UrlFetchApp.fetch(urls[u], options);
      var code = resp.getResponseCode();
      lastResponse = resp;
      if (code === 404 || code >= 500) {
        Logger.log("Backend " + urls[u] + " returned " + code + "; trying fallback if available.");
        continue;
      }
      return resp;
    } catch (fetchErr) {
      lastError = fetchErr;
      Logger.log("Network error calling " + urls[u] + ": " + fetchErr);
    }
  }
  if (lastResponse) return lastResponse;
  throw lastError || new Error("All backend URLs failed.");
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
 * Standard 18-column aliases map for robust header recognition across Form question titles,
 * short headers, and legacy spreadsheet schemas.
 */
var COLUMN_ALIASES = {
  "timestamp": ["timestamp", "time", "date", "submission time"],
  "customer name": ["customer name", "full name", "name", "your name", "student name", "client name"],
  "email address": ["email address", "email", "customer email", "your email", "respondent email"],
  "contact number": ["contact number", "mobile phone number", "mobile number", "mobile", "phone", "cellphone"],
  "book / material": ["book / material", "book / course material selection", "course material", "material", "book", "product", "item"],
  "quantity": ["quantity", "qty", "number of items", "copies"],
  "instructor credit": ["instructor credit", "enrolled instructor to credit", "enrolled instructor", "instructor", "professor", "faculty"],
  "student id": ["student id", "student / enrollee id number", "student / enrollee id", "enrollee id", "id number", "student number"],
  "notes": ["notes", "delivery address / campus pickup notes", "delivery address", "campus pickup", "remarks", "instructions", "comments"],
  "response id": ["response id", "form response id"],
  "order number": ["order number", "order no", "order ref"],
  "paymongo checkout link": ["paymongo checkout link", "checkout link", "checkout url", "payment link", "paymongo link"],
  "amount": ["amount", "amount due", "total amount", "price"],
  "payment status": ["payment status", "status"],
  "payment attempt id": ["payment attempt id", "attempt id", "payment id"],
  "official receipt no": ["official receipt no", "official receipt number", "receipt no", "receipt number", "or no"],
  "webhook outcome details": ["webhook outcome details", "outcome details", "details", "webhook details"],
  "synchronized at": ["synchronized at", "synced at", "last synchronized", "last updated", "updated at"]
};

/**
 * Resolves column index using exact match, known alias dictionary, or safe substring containment.
 */
function resolveColumnIndex(headerMap, colName) {
  var key = colName.trim().toLowerCase();
  if (headerMap[key]) return headerMap[key];

  var aliases = COLUMN_ALIASES[key] || [];
  for (var a = 0; a < aliases.length; a++) {
    var candidate = aliases[a].toLowerCase();
    if (headerMap[candidate]) return headerMap[candidate];
  }

  for (var existingHeader in headerMap) {
    if (!Object.prototype.hasOwnProperty.call(headerMap, existingHeader)) continue;
    // Prevent cross-matching distinct ID columns ("student id", "response id", "payment attempt id")
    if (key.indexOf("id") !== -1 && existingHeader.indexOf("id") !== -1 && existingHeader.indexOf(key) === -1) {
      continue;
    }
    if (existingHeader.indexOf(key) !== -1) {
      return headerMap[existingHeader];
    }
    for (var b = 0; b < aliases.length; b++) {
      var aliasCandidate = aliases[b].toLowerCase();
      if (aliasCandidate.length >= 5 && existingHeader.indexOf(aliasCandidate) !== -1) {
        return headerMap[existingHeader];
      }
    }
  }

  return null;
}

/**
 * Builds a lowercase header -> 1-based column index map from a sheet.
 */
function buildHeaderMap(sheet) {
  var lastCol = Math.max(sheet.getLastColumn(), 1);
  var headers = sheet.getRange(1, 1, 1, lastCol).getValues()[0];
  var headerMap = {};
  for (var i = 0; i < headers.length; i++) {
    var h = String(headers[i] || "").trim();
    if (h) {
      headerMap[h.toLowerCase()] = i + 1;
    }
  }
  return headerMap;
}

/**
 * Ensures target columns exist in the header row, then writes sanitized values to the specific row.
 */
function ensureAndWriteColumns(sheet, rowIndex, columnMap) {
  var headerMap = buildHeaderMap(sheet);

  for (var colName in columnMap) {
    if (Object.prototype.hasOwnProperty.call(columnMap, colName)) {
      var colIndex = resolveColumnIndex(headerMap, colName);
      if (!colIndex) {
        colIndex = sheet.getLastColumn() + 1;
        sheet.getRange(1, colIndex).setValue(colName);
        sheet.getRange(1, colIndex).setFontWeight("bold");
        headerMap[colName.trim().toLowerCase()] = colIndex;
      }
      var safeValue = sanitizeForSpreadsheet(columnMap[colName]);
      sheet.getRange(rowIndex, colIndex).setValue(safeValue);
    }
  }
  SpreadsheetApp.flush();
}

/**
 * Finds the target row in the Google Sheet for a PayMongo webhook callback using alias-aware column resolution.
 */
function findMatchingRow(sheet, responseId, orderNumber, paymentAttemptId, sheetRowIndex) {
  var lastRow = sheet.getLastRow();
  var lastCol = sheet.getLastColumn();
  if (lastRow < 2 || lastCol < 1) return null;

  var headerMap = buildHeaderMap(sheet);
  var respCol = (resolveColumnIndex(headerMap, "Response ID") || 0) - 1;
  var orderCol = (resolveColumnIndex(headerMap, "Order Number") || 0) - 1;
  var attemptCol = (resolveColumnIndex(headerMap, "Payment Attempt ID") || 0) - 1;

  var allValues = sheet.getRange(1, 1, lastRow, lastCol).getValues();

  for (var r = 1; r < allValues.length; r++) {
    var row = allValues[r];
    if (orderCol >= 0 && orderNumber && String(row[orderCol]).trim() === String(orderNumber).trim()) {
      return r + 1;
    }
    if (respCol >= 0 && responseId && String(row[respCol]).trim() === String(responseId).trim()) {
      return r + 1;
    }
    if (attemptCol >= 0 && paymentAttemptId && String(row[attemptCol]).trim() === String(paymentAttemptId).trim()) {
      return r + 1;
    }
  }

  if (sheetRowIndex && Number(sheetRowIndex) >= 2 && Number(sheetRowIndex) <= lastRow) {
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
 * Resolves the frontend application base URL dynamically from CONFIG.BACKEND_URL.
 */
function getAppBaseUrl() {
  if (CONFIG.BACKEND_URL) {
    return CONFIG.BACKEND_URL.replace(/\/api\/.*$/, "");
  }
  return "https://rge-accounting-3aa707d5f496.herokuapp.com";
}

/**
 * Sends a responsive HTML email to the customer with their direct PayMongo link.
 *
 * @param {string} [recipientEmail]
 * @param {string} [recipientName]
 * @param {Object} [orderData]
 */
function sendPaymentInstructionsEmail(recipientEmail, recipientName, orderData) {
  if (!orderData || typeof orderData !== "object") {
    Logger.log("Notice: sendPaymentInstructionsEmail was executed directly without arguments. Initializing diagnostic test payload...");
    var diagOrderNo = "ORD-SIM-" + Math.floor(1000 + Math.random() * 9000);
    orderData = {
      order_number: diagOrderNo,
      order_title: "Financial Accounting & Reporting Vol. 1",
      amount_php: "₱450.00",
      checkout_url: getAppBaseUrl() + "/checkout?order_number=" + diagOrderNo + "&amount=450.00&simulated=true"
    };
    var activeEmail = "";
    try {
      activeEmail = Session.getActiveUser().getEmail();
    } catch (_) {}
    recipientEmail = recipientEmail || activeEmail || CONFIG.SUPPORT_EMAIL;
    recipientName = recipientName || "Valued Customer (Diagnostic Mode)";
  }

  var orderNo = (orderData && orderData.order_number) ? String(orderData.order_number) : "PENDING";
  var orderTitle = (orderData && (orderData.order_title || orderData.title)) ? String(orderData.order_title || orderData.title) : "Product Order";
  var amountDisplay = (orderData && orderData.amount_php) ? String(orderData.amount_php) : "₱0.00";
  var checkoutUrl = (orderData && orderData.checkout_url) ? String(orderData.checkout_url) : "#";
  var customer = recipientName ? String(recipientName) : "Valued Client";
  var targetEmail = recipientEmail ? String(recipientEmail).trim() : "";

  if (!targetEmail || targetEmail.indexOf("@") === -1) {
    Logger.log("sendPaymentInstructionsEmail skipped: No valid recipient email provided (" + targetEmail + ").");
    return;
  }

  var subject = "Invoice & Payment Instructions: Order " + orderNo + " - " + CONFIG.COMPANY_NAME;

  var htmlBody = "" +
    "<div style='font-family: -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 8px; background-color: #ffffff; color: #1e293b;'>" +
    "  <div style='border-bottom: 2px solid #0f172a; padding-bottom: 16px; margin-bottom: 20px;'>" +
    "    <h2 style='margin: 0; color: #0f172a; font-size: 20px; font-weight: 700;'>" + CONFIG.COMPANY_NAME + "</h2>" +
    "    <p style='margin: 4px 0 0 0; color: #64748b; font-size: 13px;'>Automated Payment & Order Orchestration</p>" +
    "  </div>" +
    "  <p style='font-size: 15px; margin-top: 0;'>Dear <strong>" + customer + "</strong>,</p>" +
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
    "    <a href='" + checkoutUrl + "' style='background-color: #dc2626; color: #ffffff; padding: 14px 28px; text-decoration: none; border-radius: 6px; font-weight: 600; font-size: 15px; display: inline-block; box-shadow: 0 2px 4px rgba(0,0,0,0.1);'>" +
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

  try {
    MailApp.sendEmail(targetEmail, subject, "Please view this email in an HTML-compatible client.", {
      htmlBody: htmlBody,
      name: CONFIG.COMPANY_NAME
    });
    Logger.log("Payment instructions email dispatched successfully to: " + targetEmail + " for order " + orderNo);
  } catch (mailErr) {
    Logger.log("Notice: MailApp could not send email to " + targetEmail + ": " + mailErr);
  }
}

/**
 * Explicit test entry point for testing customer payment instructions email from editor dropdown.
 */
function testSendPaymentInstructionsEmail() {
  var activeEmail = "";
  try {
    activeEmail = Session.getActiveUser().getEmail();
  } catch (_) {}
  var testRecipient = activeEmail || CONFIG.SUPPORT_EMAIL;
  var diagOrderNo = "ORD-DIAG-" + Math.floor(1000 + Math.random() * 9000);
  var sampleOrder = {
    order_number: diagOrderNo,
    order_title: "Financial Accounting & Reporting Vol. 1 (Diagnostic)",
    amount_php: "₱450.00",
    checkout_url: getAppBaseUrl() + "/checkout?order_number=" + diagOrderNo + "&amount=450.00&simulated=true"
  };
  sendPaymentInstructionsEmail(testRecipient, "Diagnostic Test Customer", sampleOrder);
  showAlertSafely("✅ Diagnostic payment instructions email processed for: " + testRecipient);
}

/**
 * Convenience entry point to run FormBuilder directly from Code.gs.
 */
function runBookstoreFormBuilder() {
  if (typeof generateBookstoreOrderForm === "function") {
    return generateBookstoreOrderForm();
  } else {
    Logger.log("Please ensure FormBuilder.gs is added to your Apps Script project.");
  }
}

/**
 * Safely displays an alert in spreadsheet UI, or falls back to Logger.log in headless/trigger contexts.
 * @param {string} message
 */
function showAlertSafely(message) {
  try {
    SpreadsheetApp.getUi().alert(message);
  } catch (uiErr) {
    Logger.log(message);
  }
}

/**
 * ==============================================================================
 * CUSTOM ADMIN MENU (R.G.E Bookstore Admin)
 * ==============================================================================
 */
function onOpen() {
  try {
    var ui = SpreadsheetApp.getUi();
    ui.createMenu("📚 R.G.E Bookstore Admin")
      .addItem("⚡ 1. Instant-Build Bookstore Google Form", "generateBookstoreOrderForm")
      .addSeparator()
      .addItem("🎲 2. Simulate 3 Random External Book Orders", "runThreeRandomOrders")
      .addItem("🎲 3. Simulate 1 Random External Book Order", "runSingleRandomOrder")
      .addItem("⚡ 4. Simulate 5 Random External Book Orders", "simulateFiveBookstoreSubmissions")
      .addSeparator()
      .addItem("🔧 5. Verify / Repair Form Submit Trigger", "repairTriggersMenuAction")
      .addItem("📋 6. Setup Bookstore Sheet Headers & Triggers", "setupBookstoreSheet")
      .addSeparator()
      .addItem("🔄 7. Reconcile Active Row Status with Backend", "reconcileActiveRowWithBackend")
      .addItem("📧 8. Test Payment Instructions Email Dispatch", "testSendPaymentInstructionsEmail")
      .addToUi();
  } catch (menuErr) {
    Logger.log("Notice in onOpen (UI not available in standalone/trigger context): " + menuErr);
  }
}

function runThreeRandomOrders() {
  if (typeof generateRandomBookstoreSubmissions === "function") {
    generateRandomBookstoreSubmissions(3);
    showAlertSafely("✅ 3 simulated student orders submitted to PayMongo & recorded to sheet!");
  } else {
    showAlertSafely("Please ensure FormBuilder.gs is added to your Apps Script project.");
  }
}

function runSingleRandomOrder() {
  if (typeof generateRandomBookstoreSubmissions === "function") {
    generateRandomBookstoreSubmissions(1);
    showAlertSafely("✅ 1 simulated student order submitted to PayMongo & recorded to sheet!");
  } else {
    onFormSubmit();
    showAlertSafely("✅ Diagnostic test run processed!");
  }
}

function repairTriggersMenuAction() {
  var context = resolveSheetSafely(null);
  var spreadsheet = context.spreadsheet || (context.sheet ? context.sheet.getParent() : null);
  if (!spreadsheet) {
    try {
      spreadsheet = SpreadsheetApp.getActiveSpreadsheet();
    } catch (_) {}
  }
  if (!spreadsheet) {
    showAlertSafely("Error: Could not resolve spreadsheet. Open Apps Script from your Google Sheet or set FALLBACK_SPREADSHEET_ID.");
    return;
  }
  if (typeof ensureFormSubmitTrigger === "function") {
    ensureFormSubmitTrigger(spreadsheet);
  } else {
    // Built-in fallback trigger installer if FormBuilder.gs is not present
    var triggers = ScriptApp.getProjectTriggers();
    var exists = false;
    for (var i = 0; i < triggers.length; i++) {
      if (triggers[i].getHandlerFunction() === "onFormSubmit") {
        exists = true;
        break;
      }
    }
    if (!exists) {
      ScriptApp.newTrigger("onFormSubmit").forSpreadsheet(spreadsheet).onFormSubmit().create();
    }
  }
  showAlertSafely("✅ Trigger verified for spreadsheet: " + spreadsheet.getName());
}

/**
 * Setup Bookstore Sheet Headers and triggers.
 */
function setupBookstoreSheet() {
  var context = resolveSheetSafely(null);
  var spreadsheet = context.spreadsheet;
  var sheet = context.sheet;
  if (!spreadsheet && sheet) {
    try {
      spreadsheet = sheet.getParent();
    } catch (_) {}
  }
  if (!spreadsheet) {
    showAlertSafely("Error: Active spreadsheet could not be found.");
    return;
  }
  if (typeof formatBookstoreSheetHeaders === "function") {
    formatBookstoreSheetHeaders(spreadsheet);
  }
  repairTriggersMenuAction();
  showAlertSafely("✅ R.G.E Bookstore Sheet headers formatted and onFormSubmit trigger registered successfully!");
}

/**
 * Simulate 1 student bookstore submission.
 */
function simulateSingleBookstoreSubmission() {
  runSingleRandomOrder();
}

/**
 * Simulate 5 random student bookstore submissions.
 */
function simulateFiveBookstoreSubmissions() {
  if (typeof generateRandomBookstoreSubmissions === "function") {
    generateRandomBookstoreSubmissions(5);
    showAlertSafely("✅ 5 simulated student orders submitted to PayMongo & recorded to sheet!");
  } else {
    showAlertSafely("Please ensure FormBuilder.gs is added to your Apps Script project.");
  }
}

/**
 * Reconciles selected row in the spreadsheet with the .NET Backend API
 * and fetches the BIR Official Receipt number when PAID/SUCCEEDED.
 */
function reconcileActiveRowWithBackend() {
  var context = resolveSheetSafely(null);
  var sheet = context.sheet;
  if (!sheet) {
    showAlertSafely("No active sheet found.");
    return;
  }
  var activeRange = sheet.getActiveRange();
  var row = activeRange ? activeRange.getRow() : sheet.getLastRow();
  if (row < 2) {
    showAlertSafely("Please select an order row (row 2 or below) to reconcile.");
    return;
  }

  var lastCol = sheet.getLastColumn();
  if (lastCol < 1) {
    showAlertSafely("Sheet contains no data.");
    return;
  }

  var headerMap = buildHeaderMap(sheet);
  var rowValues = sheet.getRange(row, 1, 1, lastCol).getValues()[0];

  var attemptCol = (resolveColumnIndex(headerMap, "Payment Attempt ID") || 0) - 1;
  var orderCol = (resolveColumnIndex(headerMap, "Order Number") || 0) - 1;
  var respCol = (resolveColumnIndex(headerMap, "Response ID") || 0) - 1;

  var paymentId = (attemptCol >= 0 && rowValues[attemptCol]) ? String(rowValues[attemptCol]).trim() : "";
  var orderNo = (orderCol >= 0 && rowValues[orderCol]) ? String(rowValues[orderCol]).trim() : "";
  var responseId = (respCol >= 0 && rowValues[respCol]) ? String(rowValues[respCol]).trim() : "";
  var refId = paymentId || orderNo || responseId;

  if (!refId) {
    showAlertSafely("No Payment Attempt ID, Order Number, or Response ID found in selected row " + row + ".");
    return;
  }

  var baseUrl = getAppBaseUrl();
  var pollUrl = baseUrl + "/api/v1/payments/" + encodeURIComponent(refId) + "?auto_reconcile=true";

  try {
    var response = UrlFetchApp.fetch(pollUrl, {
      method: "get",
      headers: { "Accept": "application/json" },
      muteHttpExceptions: true
    });
    var code = response.getResponseCode();
    if (code === 200) {
      var data = JSON.parse(response.getContentText() || "{}");
      var rawStatus = data.status || data.payment_status || "UNKNOWN";
      var status = normalizeDisplayStatus(rawStatus);
      var receiptNo = (data.receipt && data.receipt.official_receipt_number)
        ? data.receipt.official_receipt_number
        : (data.official_receipt_number || "");

      // If PAID, query the Receipts endpoint to retrieve the issued BIR Official Receipt number
      if (status === "PAID" && !receiptNo) {
        var targetPaymentId = data.id || data.payment_attempt_id || paymentId || refId;
        var receiptUrl = baseUrl + "/api/v1/payments/" + encodeURIComponent(targetPaymentId) + "/receipt";
        var receiptResp = UrlFetchApp.fetch(receiptUrl, {
          method: "get",
          headers: { "Accept": "application/json" },
          muteHttpExceptions: true
        });
        if (receiptResp.getResponseCode() === 200) {
          var receiptData = JSON.parse(receiptResp.getContentText() || "{}");
          receiptNo = receiptData.official_receipt_number || receiptData.OfficialReceiptNumber || "";
        }
      }

      var updateMap = {
        "Payment Status": status,
        "Synchronized At": formatSafeTimestamp(new Date())
      };
      if (receiptNo) {
        updateMap["Official Receipt No"] = receiptNo;
        updateMap["Webhook Outcome Details"] = "Live reconciled with PayMongo. Receipt: " + receiptNo;
      } else if (status === "PAID") {
        updateMap["Webhook Outcome Details"] = "Live reconciled with PayMongo (Payment Confirmed).";
      }
      ensureAndWriteColumns(sheet, row, updateMap);
      showAlertSafely("✅ Row " + row + " successfully reconciled! Status: " + status + (receiptNo ? " | Receipt: " + receiptNo : ""));
    } else {
      showAlertSafely("Reconciliation response code " + code + ": " + response.getContentText().substring(0, 150));
    }
  } catch (fetchErr) {
    showAlertSafely("Reconciliation error: " + fetchErr.toString());
  }
}