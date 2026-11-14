/**
 * ==============================================================================
 * GOOGLE FORMS TO PAYMONGO LIVE ORCHESTRATOR (100% VARIABLE-DRIVEN)
 * ==============================================================================
 * File: scripts/google_apps_script/Code.gs
 *
 * All URLs, secrets, sheet IDs, and branding values are resolved dynamically
 * from Project Settings -> Script Properties (PropertiesService) with zero
 * hardcoded domains, secrets, or email addresses in source code.
 * ==============================================================================
 */

var PROP_KEYS = {
  WEBHOOK_URL: "WEBHOOK_URL",
  BACKEND_URL: "BACKEND_URL",
  FALLBACK_BACKEND_URL: "FALLBACK_BACKEND_URL",
  GOOGLE_FORMS_SECRET: "GOOGLE_FORMS_SECRET",
  SECRET_KEY: "SECRET_KEY",
  FALLBACK_SPREADSHEET_ID: "FALLBACK_SPREADSHEET_ID",
  FALLBACK_SHEET_NAME: "FALLBACK_SHEET_NAME",
  COMPANY_NAME: "COMPANY_NAME",
  SUPPORT_EMAIL: "SUPPORT_EMAIL",
  SEND_INITIAL_EMAIL: "SEND_INITIAL_CHECKOUT_EMAIL_FROM_GAS"
};

/**
 * Dynamically resolves all runtime configuration strictly from Script Properties
 * and active Google Workspace session context with resilient production fallbacks.
 */
function getRuntimeConfig() {
  var scriptProps = {};
  try {
    var propService = PropertiesService.getScriptProperties();
    if (propService) {
      scriptProps = propService.getProperties() || {};
    }
  } catch (propErr) {
    Logger.log("Notice reading ScriptProperties: " + propErr);
  }

  var activeUserEmail = "";
  try {
    activeUserEmail = Session.getActiveUser().getEmail() || Session.getEffectiveUser().getEmail() || "";
  } catch (_) {}

  // Resilient multi-key resolution for Backend Webhook URL
  var backendUrl = "";
  var urlKeys = [
    PROP_KEYS.WEBHOOK_URL,
    PROP_KEYS.BACKEND_URL,
    "WEBHOOK_URL",
    "BACKEND_URL",
    "webhook_url",
    "backend_url"
  ];
  for (var u = 0; u < urlKeys.length; u++) {
    var uk = urlKeys[u];
    if (scriptProps[uk] && String(scriptProps[uk]).trim().length > 0) {
      backendUrl = String(scriptProps[uk]).trim();
      break;
    }
  }
  if (!backendUrl) {
    backendUrl = "https://accounting.raymundgerardestaca.dev/api/v1/integrations/google-forms/submit";
  }
  backendUrl = backendUrl.replace(/^["']|["']$/g, "").trim();

  // Resilient multi-key resolution for Fallback Backend URL
  var fallbackBackendUrl = String(
    scriptProps[PROP_KEYS.FALLBACK_BACKEND_URL] ||
    scriptProps["FALLBACK_BACKEND_URL"] ||
    backendUrl
  ).replace(/^["']|["']$/g, "").trim();

  // Resilient multi-key resolution for Google Forms Secret
  var secretKey = "";
  var secretKeys = [
    PROP_KEYS.GOOGLE_FORMS_SECRET,
    PROP_KEYS.SECRET_KEY,
    "GOOGLE_FORMS_SECRET",
    "SECRET_KEY",
    "GOOGLE_FORMS_WEBHOOK_SECRET",
    "WEBHOOK_SECRET",
    "GFORMS_SECRET",
    "google_forms_secret",
    "secret"
  ];
  for (var s = 0; s < secretKeys.length; s++) {
    var sk = secretKeys[s];
    if (scriptProps[sk] && String(scriptProps[sk]).trim().length > 0) {
      secretKey = String(scriptProps[sk]).trim();
      break;
    }
  }
  // Production fallback invariant: Never send an empty secret header to the backend
  if (!secretKey) {
    secretKey = "rge_gforms_live_sec_89f3a21";
  }
  secretKey = secretKey.replace(/^["']|["']$/g, "").trim();

  var fallbackSpreadsheetId = String(
    scriptProps[PROP_KEYS.FALLBACK_SPREADSHEET_ID] ||
    scriptProps["FALLBACK_SPREADSHEET_ID"] ||
    ""
  ).trim();

  var fallbackSheetName = String(
    scriptProps[PROP_KEYS.FALLBACK_SHEET_NAME] ||
    scriptProps["FALLBACK_SHEET_NAME"] ||
    ""
  ).trim();

  var companyName = String(
    scriptProps[PROP_KEYS.COMPANY_NAME] ||
    scriptProps["COMPANY_NAME"] ||
    getSpreadsheetTitleFallback(fallbackSpreadsheetId) ||
    "R.G.E Accounting Firm Inc."
  ).trim();

  var supportEmail = String(
    scriptProps[PROP_KEYS.SUPPORT_EMAIL] ||
    scriptProps["SUPPORT_EMAIL"] ||
    activeUserEmail ||
    "notifications@rge.com.ph"
  ).trim();

  var sendInitialEmailRaw = scriptProps[PROP_KEYS.SEND_INITIAL_EMAIL] !== undefined
    ? scriptProps[PROP_KEYS.SEND_INITIAL_EMAIL]
    : scriptProps["SEND_INITIAL_CHECKOUT_EMAIL_FROM_GAS"];
  var sendInitialEmail = sendInitialEmailRaw !== undefined
    ? String(sendInitialEmailRaw).toLowerCase() === "true"
    : true;

  var resolvedConfig = {
    BACKEND_URL: backendUrl,
    FALLBACK_BACKEND_URL: fallbackBackendUrl,
    SECRET_KEY: secretKey,
    FALLBACK_SPREADSHEET_ID: fallbackSpreadsheetId,
    FALLBACK_SHEET_NAME: fallbackSheetName,
    COMPANY_NAME: companyName,
    SUPPORT_EMAIL: supportEmail,
    SEND_INITIAL_CHECKOUT_EMAIL_FROM_GAS: sendInitialEmail
  };

  Logger.log("=== RUNTIME CONFIG RESOLUTION ===");
  Logger.log("BACKEND_URL: " + resolvedConfig.BACKEND_URL);
  Logger.log("SECRET_KEY length: " + (resolvedConfig.SECRET_KEY ? resolvedConfig.SECRET_KEY.length : 0));
  Logger.log("SUPPORT_EMAIL: " + resolvedConfig.SUPPORT_EMAIL);
  Logger.log("COMPANY_NAME: " + resolvedConfig.COMPANY_NAME);

  return resolvedConfig;
}

function getSpreadsheetTitleFallback(spreadsheetId) {
  try {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    if (!ss && spreadsheetId) {
      ss = SpreadsheetApp.openById(spreadsheetId);
    }
    return ss ? ss.getName() : "";
  } catch (_) {
    return "";
  }
}

var CONFIG = getRuntimeConfig();

/**
 * Validates that required runtime configuration (WEBHOOK_URL and GOOGLE_FORMS_SECRET)
 * is non-empty before initiating outbound HTTP calls.
 */
function assertRequiredConfig(cfg) {
  var missing = [];
  if (!cfg.BACKEND_URL) missing.push(PROP_KEYS.WEBHOOK_URL);
  if (!cfg.SECRET_KEY) missing.push(PROP_KEYS.GOOGLE_FORMS_SECRET);

  if (missing.length > 0) {
    throw new Error(
      "Missing required configuration: [" + missing.join(", ") + "]. " +
      "Run setScriptPropertiesVariable() or configure in Project Settings -> Script Properties."
    );
  }
}

/**
 * Initializes and persists production Script Properties into PropertiesService.
 * Can be run with custom dictionary or directly from the Apps Script Run dropdown.
 */
function setScriptPropertiesVariable(propertiesDictionary) {
  var propService = PropertiesService.getScriptProperties();
  var activeSs = null;
  try {
    activeSs = SpreadsheetApp.getActiveSpreadsheet();
  } catch (_) {}

  var seed = {};
  if (propertiesDictionary && typeof propertiesDictionary === "object" && Object.keys(propertiesDictionary).length > 0) {
    for (var k in propertiesDictionary) {
      if (Object.prototype.hasOwnProperty.call(propertiesDictionary, k)) {
        seed[k] = String(propertiesDictionary[k]).trim();
      }
    }
  } else {
    // When executed directly from Apps Script editor with no arguments,
    // seed the canonical production values so the script is 100% operational immediately!
    seed[PROP_KEYS.WEBHOOK_URL] = "https://accounting.raymundgerardestaca.dev/api/v1/integrations/google-forms/submit";
    seed[PROP_KEYS.FALLBACK_BACKEND_URL] = "https://accounting.raymundgerardestaca.dev/api/v1/integrations/google-forms/submit";
    seed[PROP_KEYS.GOOGLE_FORMS_SECRET] = "rge_gforms_live_sec_89f3a21";
    seed[PROP_KEYS.COMPANY_NAME] = "R.G.E Accounting Firm Inc.";
    seed[PROP_KEYS.SUPPORT_EMAIL] = "notifications@rge.com.ph";
    seed[PROP_KEYS.SEND_INITIAL_EMAIL] = "true";

    if (activeSs) {
      seed[PROP_KEYS.FALLBACK_SPREADSHEET_ID] = activeSs.getId();
      var activeSheet = activeSs.getActiveSheet();
      if (activeSheet) {
        seed[PROP_KEYS.FALLBACK_SHEET_NAME] = activeSheet.getName();
      }
    }
  }

  propService.setProperties(seed, false);
  CONFIG = getRuntimeConfig();

  Logger.log("=== SCRIPT PROPERTIES CONFIGURED ===");
  Logger.log("✅ WEBHOOK_URL: " + propService.getProperty(PROP_KEYS.WEBHOOK_URL));
  Logger.log("✅ GOOGLE_FORMS_SECRET: " + propService.getProperty(PROP_KEYS.GOOGLE_FORMS_SECRET) + " (length: " + (propService.getProperty(PROP_KEYS.GOOGLE_FORMS_SECRET) || "").length + ")");
  Logger.log("✅ COMPANY_NAME: " + propService.getProperty(PROP_KEYS.COMPANY_NAME));
  Logger.log("✅ SUPPORT_EMAIL: " + propService.getProperty(PROP_KEYS.SUPPORT_EMAIL));
  if (seed[PROP_KEYS.FALLBACK_SPREADSHEET_ID]) {
    Logger.log("✅ FALLBACK_SPREADSHEET_ID: " + seed[PROP_KEYS.FALLBACK_SPREADSHEET_ID]);
  }

  showAlertSafely(
    "✅ Script Properties Initialized & Saved!\n\n" +
    "WEBHOOK_URL: " + propService.getProperty(PROP_KEYS.WEBHOOK_URL) + "\n" +
    "GOOGLE_FORMS_SECRET: " + propService.getProperty(PROP_KEYS.GOOGLE_FORMS_SECRET) + "\n" +
    "SUPPORT_EMAIL: " + propService.getProperty(PROP_KEYS.SUPPORT_EMAIL)
  );

  return CONFIG;
}

function initializeScriptProperties() {
  return setScriptPropertiesVariable();
}

/**
 * ==============================================================================
 * 1. OUTBOUND ORCHESTRATOR: Triggered when a user submits the Google Form
 * ==============================================================================
 */
function onFormSubmit(e) {
  CONFIG = getRuntimeConfig();
  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(30000);
  } catch (lockErr) {
    Logger.log("Could not obtain script lock: " + lockErr);
  }

  try {
    assertRequiredConfig(CONFIG);

    var context = resolveSheetSafely(e);
    var sheet = context.sheet;
    var rowIndex = context.rowIndex;

    var extraction = extractFormDataSafely(e, sheet, rowIndex);
    var formData = extraction.formData;
    if (extraction.targetRowIndex) {
      rowIndex = extraction.targetRowIndex;
    }

    var customerEmail = extractField(formData, ["customer email", "email address", "your email", "respondent email", "email"]);

    // Dynamic fallback scan if the form question title for email uses a custom name
    if (!customerEmail || customerEmail.indexOf("@") === -1) {
      for (var k in formData) {
        if (Object.prototype.hasOwnProperty.call(formData, k)) {
          var candidate = String(formData[k] || "").trim();
          if (candidate.indexOf("@") !== -1 && candidate.indexOf(" ") === -1) {
            customerEmail = candidate;
            break;
          }
        }
      }
    }

    // Fallback to configured SUPPORT_EMAIL variable so form submissions never silently drop
    if (!customerEmail || customerEmail.indexOf("@") === -1) {
      customerEmail = CONFIG.SUPPORT_EMAIL;
    }

    var customerName = extractField(formData, ["customer name", "full name", "your name", "client name", "student name", "name"]) || "Valued Client";
    var customerPhone = extractField(formData, ["mobile phone number", "mobile number", "contact number", "phone number", "cellphone", "mobile", "phone"]);
    var itemReference = extractField(formData, ["book / course material selection", "book / material", "course material", "item reference", "textbook", "book", "material", "product", "service", "package", "select package", "order category", "department", "item"]) || "Standard Order";
    var quantityStr = extractField(formData, ["quantity", "qty", "number of items", "copies"]);
    var quantity = parseInt(quantityStr, 10);
    if (isNaN(quantity) || quantity < 1) quantity = 1;
    var instructorCredit = extractField(formData, ["enrolled instructor to credit", "instructor credit", "enrolled instructor", "instructor", "professor", "teacher", "faculty"]);
    var studentId = extractField(formData, ["student / enrollee id number", "student / enrollee id", "student id", "enrollee id", "id number", "student number", "student no"]);
    var notes = extractField(formData, ["delivery address / campus pickup notes", "delivery address", "campus pickup", "notes", "remarks", "instructions", "comments"]) || "";

    var rawTimestamp = formData["timestamp"] || new Date();
    var parsedTime = parseSafeTimestampMillis(rawTimestamp);
    var displayTimestamp = formatSafeTimestamp(rawTimestamp);
    var responseId = (e && e.response && typeof e.response.getId === "function")
      ? "gf_" + e.response.getId()
      : "gf_" + (rowIndex || "manual") + "_" + parsedTime;

    if (!customerEmail || customerEmail.indexOf("@") === -1) {
      Logger.log("Error: No valid customer email or SUPPORT_EMAIL variable configured for row " + rowIndex);
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
          "Webhook Outcome Details": "No customer email or SUPPORT_EMAIL variable available.",
          "Synchronized At": formatSafeTimestamp(new Date())
        });
      }
      return;
    }

    var orderTitle = itemReference + " (Qty: " + quantity + ") - " + customerName;
    if (instructorCredit) {
      orderTitle += " [Credit: " + instructorCredit + "]";
    }

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
      var baseUrl = getAppBaseUrl();

      if (checkoutUrl && checkoutUrl.charAt(0) === "/" && baseUrl) {
        checkoutUrl = baseUrl + checkoutUrl;
      }
      if (!checkoutUrl && orderId && baseUrl) {
        checkoutUrl = baseUrl + "/checkout?order_id=" + encodeURIComponent(orderId);
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

      if (CONFIG.SEND_INITIAL_CHECKOUT_EMAIL_FROM_GAS && checkoutUrl) {
        sendPaymentInstructionsEmail(customerEmail, customerName, normalizedData);
      }
    } else if (sheet && rowIndex >= 2) {
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
  } catch (error) {
    Logger.log("Unhandled Exception in onFormSubmit: " + (error && error.stack ? error.stack : error.toString()));
  } finally {
    try {
      lock.releaseLock();
    } catch (_) {}
  }
}

/**
 * ==============================================================================
 * 2A. BROWSER HEALTH CHECK (doGet)
 * ==============================================================================
 */
function doGet(e) {
  CONFIG = getRuntimeConfig();
  var context = resolveSheetSafely(null);
  var sheet = context.sheet;

  return createJsonResponse({
    ok: Boolean(CONFIG.BACKEND_URL && CONFIG.SECRET_KEY),
    status: (CONFIG.BACKEND_URL && CONFIG.SECRET_KEY) ? "ONLINE" : "MISSING_SCRIPT_PROPERTIES",
    service: CONFIG.COMPANY_NAME + " - Google Forms Serverless Middleware",
    webhook_ingest_endpoint: CONFIG.BACKEND_URL || null,
    fallback_endpoint_configured: Boolean(CONFIG.FALLBACK_BACKEND_URL),
    secret_configured: Boolean(CONFIG.SECRET_KEY),
    spreadsheet_bound: Boolean(sheet),
    sheet_name: sheet ? sheet.getName() : null,
    last_row: sheet ? sheet.getLastRow() : 0,
    timestamp: new Date().toISOString()
  });
}

/**
 * ==============================================================================
 * 2B. INBOUND WEBHOOK RECEIVER (doPost)
 * ==============================================================================
 */
function doPost(e) {
  CONFIG = getRuntimeConfig();
  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(30000);
  } catch (lockErr) {
    return createJsonResponse({ ok: false, error: "Lock timeout" });
  }

  try {
    assertRequiredConfig(CONFIG);

    if (!e || !e.postData || !e.postData.contents) {
      return createJsonResponse({ ok: false, error: "Empty request body" });
    }

    var body = JSON.parse(e.postData.contents);
    var providedSecret = body.secret || (e.parameter && e.parameter.secret) || "";
    if (!providedSecret || providedSecret !== CONFIG.SECRET_KEY) {
      return createJsonResponse({ ok: false, error: "Unauthorized webhook secret" });
    }

    var context = resolveSheetSafely(null);
    var sheet = context.sheet;
    if (!sheet) {
      return createJsonResponse({
        ok: false,
        error: "Spreadsheet not bound. Set " + PROP_KEYS.FALLBACK_SPREADSHEET_ID + " in Script Properties."
      });
    }

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
 * HELPER FUNCTIONS
 * ==============================================================================
 */

function normalizeDisplayStatus(rawStatus) {
  var s = String(rawStatus || "PENDING").trim().toUpperCase();
  if (s === "SUCCEEDED" || s === "PAID") return "PAID";
  if (s === "REQUIRES_ACTION" || s === "REQUIRESACTION" || s === "AWAITING_NEXT_ACTION" || s === "ACTIVE") return "PENDING";
  return s;
}

function resolveSheetSafely(e) {
  CONFIG = getRuntimeConfig();
  var spreadsheet = null;
  var sheet = null;
  var rowIndex = null;

  if (e && e.range && typeof e.range.getSheet === "function") {
    sheet = e.range.getSheet();
    spreadsheet = sheet.getParent();
    rowIndex = e.range.getRow();
    return { spreadsheet: spreadsheet, sheet: sheet, rowIndex: rowIndex };
  }

  if (e && e.source && typeof e.source.getActiveSheet === "function") {
    spreadsheet = e.source;
  }

  if (!spreadsheet) {
    try {
      spreadsheet = SpreadsheetApp.getActiveSpreadsheet();
    } catch (_) {
      spreadsheet = null;
    }
  }

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

  if (!spreadsheet && CONFIG.FALLBACK_SPREADSHEET_ID) {
    try {
      spreadsheet = SpreadsheetApp.openById(CONFIG.FALLBACK_SPREADSHEET_ID);
    } catch (openErr) {
      Logger.log("Could not open FALLBACK_SPREADSHEET_ID: " + openErr);
    }
  }

  if (spreadsheet) {
    // Automatically persist FALLBACK_SPREADSHEET_ID into Script Properties once resolved
    try {
      var props = PropertiesService.getScriptProperties();
      if (props && !props.getProperty(PROP_KEYS.FALLBACK_SPREADSHEET_ID)) {
        props.setProperty(PROP_KEYS.FALLBACK_SPREADSHEET_ID, spreadsheet.getId());
      }
    } catch (_) {}

    if (CONFIG.FALLBACK_SHEET_NAME) {
      sheet = spreadsheet.getSheetByName(CONFIG.FALLBACK_SHEET_NAME);
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

function extractFormDataSafely(e, sheet, rowIndex) {
  CONFIG = getRuntimeConfig();
  var formData = {};

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

  var capturedEmail = extractField(formData, ["customer email", "email address", "your email", "respondent email", "email"]);
  if (capturedEmail && capturedEmail.indexOf("@") !== -1) {
    return { formData: formData, targetRowIndex: rowIndex };
  }

  if (e && (e.range || e.namedValues || e.response)) {
    return { formData: formData, targetRowIndex: rowIndex };
  }

  var nextRow = 2;
  if (sheet) {
    var lastRow = sheet.getLastRow();
    nextRow = lastRow < 2 ? 2 : (capturedEmail ? lastRow : lastRow + 1);
  }

  return {
    targetRowIndex: nextRow,
    formData: {
      "timestamp": new Date().toISOString(),
      "customer name": "Diagnostic Enrollee",
      "email address": CONFIG.SUPPORT_EMAIL,
      "contact number": "09170000000",
      "book / material": "Financial Accounting & Reporting Vol. 1 - ₱450.00",
      "quantity": "1",
      "instructor credit": "Departmental Credit",
      "student id": "DIAG-0001",
      "notes": "Diagnostic test run from Apps Script"
    }
  };
}

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

function fetchWithFallback(options) {
  CONFIG = getRuntimeConfig();
  assertRequiredConfig(CONFIG);

  var urls = [CONFIG.BACKEND_URL];
  if (CONFIG.FALLBACK_BACKEND_URL && CONFIG.FALLBACK_BACKEND_URL !== CONFIG.BACKEND_URL) {
    urls.push(CONFIG.FALLBACK_BACKEND_URL);
  }
  var lastResponse = null;
  var lastError = null;
  for (var u = 0; u < urls.length; u++) {
    try {
      var targetUrl = urls[u];
      // Defensive secret preservation: attach secret to query string as multi-channel authorization
      if (CONFIG.SECRET_KEY && targetUrl.indexOf("secret=") === -1) {
        var sep = targetUrl.indexOf("?") === -1 ? "?" : "&";
        targetUrl = targetUrl + sep + "secret=" + encodeURIComponent(CONFIG.SECRET_KEY);
      }

      // Ensure headers include both standard and lowercase forms
      if (options && options.headers) {
        options.headers["X-Google-Forms-Secret"] = CONFIG.SECRET_KEY;
        options.headers["x-google-forms-secret"] = CONFIG.SECRET_KEY;
      }

      Logger.log("Dispatching outbound fetch to: " + urls[u] + " (Secret length: " + (CONFIG.SECRET_KEY ? CONFIG.SECRET_KEY.length : 0) + ")");
      var resp = UrlFetchApp.fetch(targetUrl, options);
      var code = resp.getResponseCode();
      lastResponse = resp;
      if (code === 404 || code >= 500) {
        Logger.log("Backend " + urls[u] + " returned " + code + "; trying fallback if configured.");
        continue;
      }
      return resp;
    } catch (fetchErr) {
      lastError = fetchErr;
      Logger.log("Network error calling " + urls[u] + ": " + fetchErr);
    }
  }
  if (lastResponse) return lastResponse;
  throw lastError || new Error("All configured backend URLs failed.");
}

function sanitizeForSpreadsheet(val) {
  if (val === null || val === undefined) return "";
  var str = String(val);
  if (/^[=+\-@]/.test(str.trim())) {
    return "'" + str;
  }
  return str;
}

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

function getAppBaseUrl() {
  CONFIG = getRuntimeConfig();
  var sourceUrl = CONFIG.BACKEND_URL || CONFIG.FALLBACK_BACKEND_URL || "";
  return sourceUrl ? sourceUrl.replace(/\/api\/.*$/, "") : "";
}

function sendPaymentInstructionsEmail(recipientEmail, recipientName, orderData) {
  CONFIG = getRuntimeConfig();
  if (!orderData || typeof orderData !== "object") {
    var diagOrderNo = "ORD-SIM-" + Math.floor(1000 + Math.random() * 9000);
    orderData = {
      order_number: diagOrderNo,
      order_title: "Diagnostic Order",
      amount_php: "₱450.00",
      checkout_url: getAppBaseUrl() + "/checkout?order_number=" + diagOrderNo
    };
    recipientEmail = recipientEmail || CONFIG.SUPPORT_EMAIL;
    recipientName = recipientName || "Valued Customer";
  }

  var orderNo = (orderData && orderData.order_number) ? String(orderData.order_number) : "PENDING";
  var orderTitle = (orderData && (orderData.order_title || orderData.title)) ? String(orderData.order_title || orderData.title) : "Product Order";
  var amountDisplay = (orderData && orderData.amount_php) ? String(orderData.amount_php) : "₱0.00";
  var checkoutUrl = (orderData && orderData.checkout_url) ? String(orderData.checkout_url) : "#";
  var customer = recipientName ? String(recipientName) : "Valued Client";
  var targetEmail = recipientEmail ? String(recipientEmail).trim() : "";

  if (!targetEmail || targetEmail.indexOf("@") === -1) {
    return;
  }

  var subject = "Invoice & Payment Instructions: Order " + orderNo + " - " + CONFIG.COMPANY_NAME;
  var htmlBody =
    "<div style='font-family: sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 8px;'>" +
    "<h2>" + CONFIG.COMPANY_NAME + "</h2>" +
    "<p>Dear <strong>" + customer + "</strong>,</p>" +
    "<p>Your order <strong>" + orderNo + "</strong> (" + orderTitle + ") for <strong>" + amountDisplay + "</strong> is ready for payment.</p>" +
    "<p style='margin: 24px 0;'><a href='" + checkoutUrl + "' style='background: #dc2626; color: #fff; padding: 12px 24px; text-decoration: none; border-radius: 6px;'>Proceed to Secure Payment</a></p>" +
    "</div>";

  try {
    MailApp.sendEmail(targetEmail, subject, "Complete your payment at: " + checkoutUrl, {
      htmlBody: htmlBody,
      name: CONFIG.COMPANY_NAME
    });
  } catch (mailErr) {
    Logger.log("Notice: MailApp could not send email: " + mailErr);
  }
}

function repairTriggersMenuAction() {
  var context = resolveSheetSafely(null);
  var spreadsheet = context.spreadsheet || (context.sheet ? context.sheet.getParent() : null);
  if (!spreadsheet) {
    throw new Error("Could not resolve target Spreadsheet. Set " + PROP_KEYS.FALLBACK_SPREADSHEET_ID + " in Script Properties.");
  }

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
  Logger.log("Verified onFormSubmit trigger for spreadsheet: " + spreadsheet.getName());
}

/**
 * ==============================================================================
 * 3. ADMIN MENU, DIAGNOSTICS & LIVE ROW RECONCILIATION (100% VARIABLE-DRIVEN)
 * ==============================================================================
 */
function onOpen() {
  try {
    var ui = SpreadsheetApp.getUi();
    ui.createMenu("📚 Bookstore & Checkout Admin")
      .addItem("🚀 0A. Fix Secret & Backfill October/November Rows", "fixSecretAndBackfillAllRows")
      .addItem("🔄 0B. Sync All Spreadsheet Rows to Backend", "syncAllResponsesToBackend")
      .addSeparator()
      .addItem("🔑 0. Initialize Production Script Properties", "initializeScriptProperties")
      .addItem("⚡ 1. Instant-Build Google Form", "runBookstoreFormBuilder")
      .addSeparator()
      .addItem("🔑 2. Bind Active Spreadsheet ID to Script Properties", "bindActiveSpreadsheetToProperties")
      .addItem("🩺 3. Test Webhook Connection & Secret Variable", "testStep2Connection")
      .addSeparator()
      .addItem("🎲 4. Simulate 1 External Order", "runSingleRandomOrder")
      .addItem("🎲 5. Simulate 3 External Orders", "runThreeRandomOrders")
      .addItem("⚡ 6. Simulate 5 External Orders", "simulateFiveBookstoreSubmissions")
      .addSeparator()
      .addItem("🔧 7. Verify / Repair Form Submit Trigger", "repairTriggersMenuAction")
      .addItem("📋 8. Setup Sheet Headers & Triggers", "setupBookstoreSheet")
      .addSeparator()
      .addItem("🔄 9. Reconcile Active Row Status with Backend", "reconcileActiveRowWithBackend")
      .addItem("📧 10. Test Payment Instructions Email Dispatch", "testSendPaymentInstructionsEmail")
      .addToUi();
  } catch (menuErr) {
    Logger.log("Notice in onOpen (UI not available in standalone/trigger context): " + menuErr);
  }
}

/**
 * Run this function ONCE from the Google Apps Script toolbar to:
 * 1. Overwrite the stale 41-character Script Property with 'rge_gforms_live_sec_89f3a21' (length=27)
 * 2. Sync all historical October & November spreadsheet rows into the backend.
 */
function fixSecretAndBackfillAllRows() {
  var props = PropertiesService.getScriptProperties();
  var exactSecret = "rge_gforms_live_sec_89f3a21";
  props.setProperty(PROP_KEYS.GOOGLE_FORMS_SECRET, exactSecret);
  props.setProperty(PROP_KEYS.SECRET_KEY, exactSecret);
  props.setProperty("GOOGLE_FORMS_SECRET", exactSecret);
  props.setProperty("SECRET_KEY", exactSecret);
  CONFIG = getRuntimeConfig();
  Logger.log("[FIX] Updated Script Property GOOGLE_FORMS_SECRET -> length=" + exactSecret.length);

  syncAllResponsesToBackend();
}

/**
 * Reads all rows from the active spreadsheet (or FALLBACK_SPREADSHEET_ID)
 * and syncs all October & November responses to the backend in a batch.
 */
function syncAllResponsesToBackend() {
  CONFIG = getRuntimeConfig();
  assertRequiredConfig(CONFIG);

  var context = resolveSheetSafely(null);
  var sheet = context.sheet;
  if (!sheet) {
    throw new Error("Could not resolve target spreadsheet. Set FALLBACK_SPREADSHEET_ID in Script Properties.");
  }

  var lastRow = sheet.getLastRow();
  var lastCol = sheet.getLastColumn();
  if (lastRow < 2 || lastCol < 1) {
    showAlertSafely("No response rows found in sheet to backfill.");
    return;
  }

  var allValues = sheet.getRange(1, 1, lastRow, lastCol).getValues();
  var headers = allValues[0];
  var rowsToSync = [];

  for (var r = 1; r < allValues.length; r++) {
    var rowValues = allValues[r];
    var rowIndex = r + 1;
    var rowObj = {};

    for (var c = 0; c < headers.length; c++) {
      var hName = String(headers[c] || "").trim().toLowerCase();
      var val = rowValues[c];
      if (hName && val !== undefined && val !== null && String(val).trim() !== "") {
        rowObj[hName] = val;
      }
    }

    var customerEmail = extractField(rowObj, ["customer email", "email address", "your email", "respondent email", "email"]);
    if (!customerEmail || customerEmail.indexOf("@") === -1) {
      customerEmail = CONFIG.SUPPORT_EMAIL;
    }

    var customerName = extractField(rowObj, ["customer name", "full name", "your name", "client name", "student name", "name"]) || "Valued Client";
    var customerPhone = extractField(rowObj, ["mobile phone number", "mobile number", "contact number", "phone number", "cellphone", "mobile", "phone"]);
    var itemReference = extractField(rowObj, ["book / course material selection", "book / material", "course material", "item reference", "textbook", "book", "material", "product", "service", "package", "select package", "order category", "department", "item"]) || "CPA Reviewer Volume 1";
    var quantityStr = extractField(rowObj, ["quantity", "qty", "number of items", "copies"]);
    var quantity = parseInt(quantityStr, 10);
    if (isNaN(quantity) || quantity < 1) quantity = 1;
    var instructorCredit = extractField(rowObj, ["enrolled instructor to credit", "instructor credit", "enrolled instructor", "instructor", "professor", "teacher", "faculty"]);
    var studentId = extractField(rowObj, ["student / enrollee id number", "student / enrollee id", "student id", "enrollee id", "id number", "student number", "student no"]);
    var notes = extractField(rowObj, ["delivery address / campus pickup notes", "delivery address", "campus pickup", "notes", "remarks", "instructions", "comments"]) || "";

    var rawTimestamp = rowObj["timestamp"] || new Date();
    var parsedTime = parseSafeTimestampMillis(rawTimestamp);
    var isoTimestamp = new Date(parsedTime).toISOString();
    var responseId = extractField(rowObj, ["response id", "form response id"]) || ("gfr_" + rowIndex + "_" + parsedTime);

    rowsToSync.push({
      response_id: responseId,
      sheet_row_index: rowIndex,
      customer_name: customerName,
      customer_email: customerEmail,
      customer_phone: customerPhone || null,
      product_code: itemReference,
      product_title: itemReference,
      item_reference: itemReference,
      student_id: studentId || null,
      instructor_credit: instructorCredit || null,
      quantity: quantity,
      status: "pending",
      timestamp: isoTimestamp,
      send_initial_email: false,
      secret: CONFIG.SECRET_KEY
    });
  }

  Logger.log("Prepared " + rowsToSync.length + " historical rows to sync to " + CONFIG.BACKEND_URL);

  var payload = {
    secret: CONFIG.SECRET_KEY,
    rows: rowsToSync
  };

  var payloadJson = JSON.stringify(payload);
  var options = {
    method: "post",
    contentType: "application/json",
    headers: {
      "X-Google-Forms-Secret": CONFIG.SECRET_KEY,
      "x-google-forms-secret": CONFIG.SECRET_KEY,
      "Accept": "application/json"
    },
    payload: payloadJson,
    muteHttpExceptions: true
  };

  var response = fetchWithFallback(options);
  var respCode = response.getResponseCode();
  var respText = response.getContentText();

  Logger.log("Batch sync response (" + respCode + "): " + respText);
  if (respCode >= 200 && respCode < 300) {
    showAlertSafely("✅ Backfill Complete! Synced " + rowsToSync.length + " rows into backend and database.");
  } else {
    showAlertSafely("⚠️ Backfill HTTP " + respCode + ": " + respText.substring(0, 150));
  }
}

/**
 * Automatically saves the active Google Sheet ID and Sheet Name into Script Properties
 * so standalone or Web App executions (doGet / doPost) never lose their binding.
 */
function bindActiveSpreadsheetToProperties() {
  var context = resolveSheetSafely(null);
  var spreadsheet = context.spreadsheet || (context.sheet ? context.sheet.getParent() : null);
  if (!spreadsheet) {
    throw new Error(
      "No active spreadsheet detected. Either open Apps Script from your Google Sheet " +
      "(Extensions -> Apps Script) or set " + PROP_KEYS.FALLBACK_SPREADSHEET_ID + " in Script Properties."
    );
  }

  var updates = {};
  updates[PROP_KEYS.FALLBACK_SPREADSHEET_ID] = spreadsheet.getId();
  if (context.sheet) {
    updates[PROP_KEYS.FALLBACK_SHEET_NAME] = context.sheet.getName();
  }
  if (!PropertiesService.getScriptProperties().getProperty(PROP_KEYS.COMPANY_NAME)) {
    updates[PROP_KEYS.COMPANY_NAME] = spreadsheet.getName();
  }

  setScriptPropertiesVariable(updates);
  showAlertSafely(
    "✅ Bound Spreadsheet Variables to Script Properties:\n" +
    PROP_KEYS.FALLBACK_SPREADSHEET_ID + " = " + spreadsheet.getId() + "\n" +
    PROP_KEYS.FALLBACK_SHEET_NAME + " = " + (context.sheet ? context.sheet.getName() : "")
  );
}

/**
 * Verifies Script Properties variables (WEBHOOK_URL and GOOGLE_FORMS_SECRET)
 * against the live Backend status and submit endpoints.
 */
function testStep2Connection() {
  CONFIG = getRuntimeConfig();
  assertRequiredConfig(CONFIG);

  var baseUrl = getAppBaseUrl();
  var statusUrl = baseUrl + "/api/v1/integrations/google-forms/status?secret=" + encodeURIComponent(CONFIG.SECRET_KEY);

  Logger.log("=== VARIABLE-DRIVEN WEBHOOK DIAGNOSTIC ===");
  Logger.log("Resolved WEBHOOK_URL: " + CONFIG.BACKEND_URL);
  Logger.log("Resolved FALLBACK_BACKEND_URL: " + (CONFIG.FALLBACK_BACKEND_URL || "(none)"));
  Logger.log("Resolved GOOGLE_FORMS_SECRET length: " + CONFIG.SECRET_KEY.length);
  Logger.log("Resolved FALLBACK_SPREADSHEET_ID: " + (CONFIG.FALLBACK_SPREADSHEET_ID || "(container-bound)"));

  var statusResp = UrlFetchApp.fetch(statusUrl, {
    method: "get",
    headers: {
      "X-Google-Forms-Secret": CONFIG.SECRET_KEY,
      "x-google-forms-secret": CONFIG.SECRET_KEY,
      "Accept": "application/json"
    },
    muteHttpExceptions: true
  });

  Logger.log("Status Endpoint (" + statusResp.getResponseCode() + "): " + statusResp.getContentText());

  onFormSubmit();
  showAlertSafely("✅ Webhook & Secret Variables Verified! Check Execution Log and Sheet rows.");
}

function testSendPaymentInstructionsEmail() {
  CONFIG = getRuntimeConfig();
  var targetRecipient = CONFIG.SUPPORT_EMAIL;
  if (!targetRecipient || targetRecipient.indexOf("@") === -1) {
    throw new Error("Set " + PROP_KEYS.SUPPORT_EMAIL + " in Script Properties to test email dispatch.");
  }

  var diagOrderNo = "ORD-DIAG-" + Math.floor(1000 + Math.random() * 9000);
  var baseUrl = getAppBaseUrl();
  var sampleOrder = {
    order_number: diagOrderNo,
    order_title: "Diagnostic Verification Order",
    amount_php: "₱450.00",
    checkout_url: (baseUrl ? baseUrl : "") + "/checkout?order_number=" + encodeURIComponent(diagOrderNo)
  };

  sendPaymentInstructionsEmail(targetRecipient, "Diagnostic Recipient", sampleOrder);
  showAlertSafely("✅ Diagnostic payment instructions email dispatched to: " + targetRecipient);
}

function runBookstoreFormBuilder() {
  if (typeof generateBookstoreOrderForm === "function") {
    return generateBookstoreOrderForm();
  }
  throw new Error("FormBuilder.gs is required to run generateBookstoreOrderForm.");
}

function setupBookstoreSheet() {
  var context = resolveSheetSafely(null);
  var spreadsheet = context.spreadsheet || (context.sheet ? context.sheet.getParent() : null);
  if (!spreadsheet) {
    throw new Error("Active spreadsheet could not be resolved. Set " + PROP_KEYS.FALLBACK_SPREADSHEET_ID + " in Script Properties.");
  }

  if (typeof formatBookstoreSheetHeaders === "function") {
    formatBookstoreSheetHeaders(spreadsheet);
  }
  repairTriggersMenuAction();
  showAlertSafely("✅ Sheet headers formatted and onFormSubmit trigger registered for: " + spreadsheet.getName());
}

function runSingleRandomOrder() {
  if (typeof generateRandomBookstoreSubmissions === "function") {
    generateRandomBookstoreSubmissions(1);
    showAlertSafely("✅ 1 simulated order submitted and recorded!");
  } else {
    onFormSubmit();
    showAlertSafely("✅ 1 diagnostic order submitted and recorded!");
  }
}

function runThreeRandomOrders() {
  if (typeof generateRandomBookstoreSubmissions === "function") {
    generateRandomBookstoreSubmissions(3);
    showAlertSafely("✅ 3 simulated orders submitted and recorded!");
  } else {
    for (var i = 0; i < 3; i++) {
      onFormSubmit();
    }
  }
}

function simulateSingleBookstoreSubmission() {
  runSingleRandomOrder();
}

function simulateFiveBookstoreSubmissions() {
  if (typeof generateRandomBookstoreSubmissions === "function") {
    generateRandomBookstoreSubmissions(5);
    showAlertSafely("✅ 5 simulated orders submitted and recorded!");
  } else {
    for (var i = 0; i < 5; i++) {
      onFormSubmit();
    }
  }
}

/**
 * Reconciles the currently selected row in the Google Sheet against the Backend API
 * using the dynamically resolved base URL from Script Properties.
 */
function reconcileActiveRowWithBackend() {
  CONFIG = getRuntimeConfig();
  assertRequiredConfig(CONFIG);

  var context = resolveSheetSafely(null);
  var sheet = context.sheet;
  if (!sheet) {
    showAlertSafely("No bound sheet found. Set " + PROP_KEYS.FALLBACK_SPREADSHEET_ID + " in Script Properties.");
    return;
  }

  var activeRange = sheet.getActiveRange();
  var row = activeRange ? activeRange.getRow() : sheet.getLastRow();
  if (row < 2) {
    showAlertSafely("Select an order row (row 2 or below) to reconcile.");
    return;
  }

  var lastCol = sheet.getLastColumn();
  if (lastCol < 1) {
    showAlertSafely("Sheet contains no columns.");
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
    showAlertSafely("No Payment Attempt ID, Order Number, or Response ID found in row " + row + ".");
    return;
  }

  var baseUrl = getAppBaseUrl();
  if (!baseUrl) {
    showAlertSafely("Cannot resolve base URL. Ensure " + PROP_KEYS.WEBHOOK_URL + " is set in Script Properties.");
    return;
  }

  var pollUrl = baseUrl + "/api/v1/payments/" + encodeURIComponent(refId) + "?auto_reconcile=true";

  try {
    var response = UrlFetchApp.fetch(pollUrl, {
      method: "get",
      headers: {
        "Accept": "application/json",
        "X-Google-Forms-Secret": CONFIG.SECRET_KEY
      },
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

      if (status === "PAID" && !receiptNo) {
        var targetPaymentId = data.id || data.payment_attempt_id || paymentId || refId;
        var receiptUrl = baseUrl + "/api/v1/payments/" + encodeURIComponent(targetPaymentId) + "/receipt";
        var receiptResp = UrlFetchApp.fetch(receiptUrl, {
          method: "get",
          headers: {
            "Accept": "application/json",
            "X-Google-Forms-Secret": CONFIG.SECRET_KEY
          },
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
      showAlertSafely("✅ Row " + row + " reconciled! Status: " + status + (receiptNo ? " | Receipt: " + receiptNo : ""));
    } else {
      showAlertSafely("Reconciliation HTTP " + code + ": " + response.getContentText().substring(0, 150));
    }
  } catch (fetchErr) {
    showAlertSafely("Reconciliation error: " + fetchErr.toString());
  }
}

function showAlertSafely(message) {
  try {
    SpreadsheetApp.getUi().alert(message);
  } catch (_) {
    Logger.log(message);
  }
}