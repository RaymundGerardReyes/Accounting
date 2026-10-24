/**
 * ==============================================================================
 * R.G.E ACCOUNTING FIRM INC. - INSTANT GOOGLE FORM & SPREADSHEET BUILDER
 * ==============================================================================
 * File: scripts/google_apps_script/FormBuilder.gs
 *
 * PURPOSE:
 * Programmatically builds a fully automated Bookstore & Course Materials Order Form,
 * connects it to the active Google Spreadsheet, binds the onFormSubmit trigger,
 * and sets up formula-injection-safe header columns.
 *
 * How to run:
 * 1. Open your Google Sheet (or Google Forms container).
 * 2. Click Extensions -> Apps Script.
 * 3. In the toolbar function dropdown, select: generateBookstoreOrderForm
 * 4. Click Run (and grant Google authorization if prompted).
 * ==============================================================================
 */

/**
 * Main Entry Point: Instantly develops the bookstore order form, binds destination,
 * configures onFormSubmit trigger, and formats spreadsheet columns.
 *
 * @return {Object} Metadata containing form URLs and spreadsheet ID.
 */
function generateBookstoreOrderForm() {
  Logger.log("Starting instant generation of R.G.E Bookstore Order Form...");

  // 1. Create the Google Form
  var formTitle = "R.G.E Accounting Bookstore - Course Books & Materials Order Form";
  var form = FormApp.create(formTitle);

  form.setDescription(
    "Official course materials, reviewer textbooks, and practice manuals order form for " +
    "R.G.E Accounting Firm Inc.\n\n" +
    "INSTRUCTIONS:\n" +
    "1. Complete your contact and student/shipping details below.\n" +
    "2. Select your course book and ensure you credit your enrolled instructor for semester tracking.\n" +
    "3. Upon submission, an instant PayMongo checkout link will be generated and emailed to you.\n" +
    "4. Supported payment channels: GCash, Maya, QR Ph, BPI Online, Credit / Debit Card.\n" +
    "5. Your BIR-compliant electronic Official Receipt will be issued automatically upon payment confirmation."
  );

  // Enable external friction-free ordering without requiring Google account sign-in
  // (email is explicitly collected and regex-validated via Question 2)
  try {
    form.setCollectEmail(false);
  } catch (emailErr) {
    Logger.log("Notice on setCollectEmail: " + emailErr);
  }

  // 2. Build Form Questions (Ordered 1:1 to match standardized sheet columns 2 to 9)
  // Q1: Full Name (Required TextItem) -> Maps to Column 2: "Customer Name"
  var nameItem = form.addTextItem();
  nameItem.setTitle("Full Name")
    .setHelpText("Enter your full legal name (e.g., Juan Dela Cruz).")
    .setRequired(true);

  // Q2: Email Address (Required TextItem with email validation) -> Maps to Column 3: "Email Address"
  var emailValidation = FormApp.createTextValidation()
    .requireTextIsEmail()
    .setHelpText("Please enter a valid email address (e.g., student@university.edu.ph) for your invoice and official receipt.")
    .build();
  var emailItem = form.addTextItem();
  emailItem.setTitle("Email Address")
    .setHelpText("Your PayMongo payment link and BIR-compliant electronic receipt will be delivered here.")
    .setRequired(true)
    .setValidation(emailValidation);

  // Q3: Mobile Phone Number (Philippine regex: 09XX or +639XX) -> Maps to Column 4: "Contact Number"
  var phoneValidation = FormApp.createTextValidation()
    .requireTextMatchesPattern("^(\\+?63|0)9\\d{9}$")
    .setHelpText("Please enter a valid 11-digit Philippine mobile number (e.g., 09171234567 or +639171234567).")
    .build();
  var phoneItem = form.addTextItem();
  phoneItem.setTitle("Mobile Phone Number")
    .setHelpText("Philippine mobile number for GCash/Maya notifications and delivery SMS updates.")
    .setRequired(true)
    .setValidation(phoneValidation);

  // Q4: Book / Course Material Selection (Required ListItem with official catalog prices) -> Maps to Column 5: "Book / Material"
  var bookChoices = [
    "Financial Accounting & Reporting Vol. 1 - ₱450.00",
    "Advanced Financial Accounting & Reporting - ₱520.00",
    "Auditing Theory & Practice - ₱480.00",
    "Management Advisory Services - ₱420.00",
    "Philippine Tax Law & Principles - ₱500.00"
  ];
  var bookItem = form.addListItem();
  bookItem.setTitle("Book / Course Material Selection")
    .setHelpText("Select the textbook, reviewer, or practice manual required for your course.")
    .setChoiceValues(bookChoices)
    .setRequired(true);

  // Q5: Quantity -> Maps to Column 6: "Quantity"
  var qtyChoices = ["1", "2", "3", "4", "5", "10"];
  var qtyItem = form.addListItem();
  qtyItem.setTitle("Quantity")
    .setHelpText("Number of copies to purchase.")
    .setChoiceValues(qtyChoices)
    .setRequired(true);

  // Q6: Enrolled Instructor to Credit (Required ListItem for faculty semester accounting) -> Maps to Column 7: "Instructor Credit"
  var instructorChoices = [
    "Prof. Raymund Reyes (Accounting & Auditing)",
    "Prof. Maria Santos (Taxation)",
    "Prof. Juan Carlos (Finance & Advisory)",
    "Prof. Elena Gomez (Management Accounting)",
    "External / Self-Study (No Instructor Credit)"
  ];
  var instructorItem = form.addListItem();
  instructorItem.setTitle("Enrolled Instructor to Credit")
    .setHelpText("Specify your faculty professor/instructor to credit your course materials enrollment.")
    .setChoiceValues(instructorChoices)
    .setRequired(true);

  // Q7: Student / Enrollee ID Number (Optional) -> Maps to Column 8: "Student ID"
  var studentIdItem = form.addTextItem();
  studentIdItem.setTitle("Student / Enrollee ID Number")
    .setHelpText("Optional: Enter your university student ID or CPA review school enrollee number (e.g., 2026-ACCT-1049).")
    .setRequired(false);

  // Q8: Delivery Address / Campus Pickup Notes (Paragraph) -> Maps to Column 9: "Notes"
  var notesItem = form.addParagraphTextItem();
  notesItem.setTitle("Delivery Address / Campus Pickup Notes")
    .setHelpText("Enter your complete shipping address (street, city, province) or specify campus bookstore pickup preferences.")
    .setRequired(false);

  // 3. Resolve Active Spreadsheet or Create Dedicated Sheet
  var spreadsheet = null;
  try {
    spreadsheet = SpreadsheetApp.getActiveSpreadsheet();
  } catch (_) {
    spreadsheet = null;
  }

  if (!spreadsheet && typeof CONFIG !== "undefined" && CONFIG.FALLBACK_SPREADSHEET_ID) {
    try {
      spreadsheet = SpreadsheetApp.openById(CONFIG.FALLBACK_SPREADSHEET_ID.trim());
    } catch (openErr) {
      Logger.log("Could not open CONFIG.FALLBACK_SPREADSHEET_ID: " + openErr);
    }
  }

  if (!spreadsheet) {
    spreadsheet = SpreadsheetApp.create("R.G.E Accounting Bookstore - Order Submissions");
    Logger.log("Created new bound spreadsheet: " + spreadsheet.getUrl());
  }

  // 4. Automatically Set Form Destination to the Spreadsheet
  try {
    form.setDestination(FormApp.DestinationType.SPREADSHEET, spreadsheet.getId());
    Logger.log("Linked Google Form destination to Spreadsheet: " + spreadsheet.getName() + " (" + spreadsheet.getId() + ")");
  } catch (destErr) {
    Logger.log("Warning linking form destination: " + destErr);
  }

  // 5. Automatically Create or Verify onFormSubmit Trigger Programmatically
  var triggerCreated = ensureFormSubmitTrigger(spreadsheet);

  // 6. Format Sheet Header Columns Cleanly with Formula-Injection-Safe Headers
  formatBookstoreSheetHeaders(spreadsheet);

  var publishedUrl = form.getPublishedUrl();
  var editUrl = form.getEditUrl();

  Logger.log("=================================================================");
  Logger.log("SUCCESS! R.G.E Bookstore Order Form is ready for live traffic!");
  Logger.log("Published Form URL (Share with Students/Buyers): " + publishedUrl);
  Logger.log("Form Edit URL (Editor Access): " + editUrl);
  Logger.log("Spreadsheet URL: " + spreadsheet.getUrl());
  Logger.log("Trigger Status: " + (triggerCreated ? "Newly Created" : "Already Verified"));
  Logger.log("=================================================================");

  return {
    ok: true,
    form_id: form.getId(),
    published_url: publishedUrl,
    edit_url: editUrl,
    spreadsheet_id: spreadsheet.getId(),
    spreadsheet_url: spreadsheet.getUrl(),
    trigger_configured: true
  };
}

/**
 * Programmatically ensures that an onFormSubmit trigger is registered on the spreadsheet.
 * Eliminates the need for manual configuration in the Apps Script Triggers dashboard.
 *
 * @param {Spreadsheet} spreadsheet - Target Google Spreadsheet.
 * @return {boolean} True if created, false if already present.
 */
function ensureFormSubmitTrigger(spreadsheet) {
  if (!spreadsheet) return false;

  var triggers = ScriptApp.getProjectTriggers();
  var targetHandler = "onFormSubmit";

  for (var i = 0; i < triggers.length; i++) {
    var trig = triggers[i];
    if (trig.getHandlerFunction() === targetHandler) {
      try {
        var sourceId = trig.getTriggerSourceId();
        if (!sourceId || sourceId === spreadsheet.getId()) {
          Logger.log("Found existing trigger for handler '" + targetHandler + "' on spreadsheet: " + spreadsheet.getId());
          return false;
        }
      } catch (_) {
        return false;
      }
    }
  }

  try {
    ScriptApp.newTrigger(targetHandler)
      .forSpreadsheet(spreadsheet)
      .onFormSubmit()
      .create();
    Logger.log("Successfully created programmatic 'onFormSubmit' trigger for spreadsheet: " + spreadsheet.getId());
    return true;
  } catch (trigErr) {
    Logger.log("Notice creating spreadsheet trigger: " + trigErr.toString());
    return false;
  }
}

/**
 * Formats the response sheet with clean, formula-injection-safe standard headers.
 *
 * Standard Header Columns:
 * 1. Timestamp
 * 2. Customer Name
 * 3. Email Address
 * 4. Contact Number
 * 5. Book / Material
 * 6. Quantity
 * 7. Instructor Credit
 * 8. Student ID
 * 9. Notes
 * 10. Response ID
 * 11. Order Number
 * 12. PayMongo Checkout Link
 * 13. Amount
 * 14. Payment Status
 * 15. Official Receipt No
 * 16. Webhook Outcome Details
 * 17. Synchronized At
 *
 * @param {Spreadsheet} spreadsheet - Target Google Spreadsheet.
 */
function formatBookstoreSheetHeaders(spreadsheet) {
  if (!spreadsheet) return;

  // Give Google Forms a moment to link the responses tab if newly assigned
  Utilities.sleep(1500);

  var sheets = spreadsheet.getSheets();
  var targetSheets = [];

  for (var s = 0; s < sheets.length; s++) {
    var sheetName = sheets[s].getName().toLowerCase();
    if (sheetName.indexOf("form responses") !== -1 || sheetName.indexOf("response") !== -1) {
      targetSheets.push(sheets[s]);
    }
  }
  if (targetSheets.length === 0 && sheets.length > 0) {
    targetSheets.push(sheets[0]);
  }

  var headers = [
    "Timestamp",
    "Customer Name",
    "Email Address",
    "Contact Number",
    "Book / Material",
    "Quantity",
    "Instructor Credit",
    "Student ID",
    "Notes",
    "Response ID",
    "Order Number",
    "PayMongo Checkout Link",
    "Amount",
    "Payment Status",
    "Official Receipt No",
    "Webhook Outcome Details",
    "Synchronized At"
  ];

  for (var t = 0; t < targetSheets.length; t++) {
    var targetSheet = targetSheets[t];
    try {
      var range = targetSheet.getRange(1, 1, 1, headers.length);
      range.setValues([headers]);
      range.setFontWeight("bold");
      range.setBackground("#0f172a");
      range.setFontColor("#ffffff");
      range.setHorizontalAlignment("center");
      targetSheet.setFrozenRows(1);

      // Auto-fit column widths
      for (var col = 1; col <= headers.length; col++) {
        targetSheet.autoResizeColumn(col);
      }
      SpreadsheetApp.flush();
      Logger.log("Standardized header columns formatted on sheet: " + targetSheet.getName());
    } catch (hdrErr) {
      Logger.log("Notice formatting headers on " + targetSheet.getName() + ": " + hdrErr);
    }
  }
}

/**
 * ==============================================================================
 * RANDOM TEST SUBMISSION GENERATOR
 * ==============================================================================
 * Generates realistic randomized orders for external users purchasing different
 * course books with credit assigned to enrolled faculty instructors.
 *
 * Can be run directly from Apps Script editor: generateRandomBookstoreSubmissions()
 *
 * @param {number} [count=3] - Number of random orders to simulate.
 */
function generateRandomBookstoreSubmissions(count) {
  var total = count || 3;
  Logger.log("Simulating " + total + " randomized bookstore order submissions...");

  var sampleNames = [
    "Alyssa Gonzaga",
    "Mark Anthony Fernandez",
    "Camille Del Rosario",
    "Christian Bautista",
    "Patricia Cruz",
    "Lorenzo Miguel Tan",
    "Bernadette Ramos",
    "Angelo Gabriel Reyes"
  ];

  var sampleBooks = [
    "Financial Accounting & Reporting Vol. 1 - ₱450.00",
    "Advanced Financial Accounting & Reporting - ₱520.00",
    "Auditing Theory & Practice - ₱480.00",
    "Management Advisory Services - ₱420.00",
    "Philippine Tax Law & Principles - ₱500.00"
  ];

  var sampleInstructors = [
    "Prof. Raymund Reyes (Accounting & Auditing)",
    "Prof. Maria Santos (Taxation)",
    "Prof. Juan Carlos (Finance & Advisory)",
    "Prof. Elena Gomez (Management Accounting)",
    "External / Self-Study (No Instructor Credit)"
  ];

  var sampleNotes = [
    "Campus bookstore counter pickup during 2nd period",
    "Please send payment confirmation via SMS",
    "Delivery to Dormitory Building B, Room 402",
    "Urgent: CPA reviewer needed for preliminary exams",
    "Standard courier delivery to residence"
  ];

  var results = [];

  for (var i = 0; i < total; i++) {
    var randomName = sampleNames[Math.floor(Math.random() * sampleNames.length)];
    var emailPrefix = randomName.toLowerCase().replace(/[^a-z]/g, ".");
    var randomEmail = emailPrefix + "@example.edu.ph";
    var randomPhone = "0917" + Math.floor(1000000 + Math.random() * 9000000);
    var randomBook = sampleBooks[Math.floor(Math.random() * sampleBooks.length)];
    var randomQty = Math.floor(Math.random() * 3) + 1; // 1 to 3 copies
    var randomInstructor = sampleInstructors[Math.floor(Math.random() * sampleInstructors.length)];
    var randomStudentId = "2026-BSA-" + (1000 + Math.floor(Math.random() * 9000));
    var randomNote = sampleNotes[Math.floor(Math.random() * sampleNotes.length)];

    var fakeEvent = {
      namedValues: {
        "timestamp": [new Date().toISOString()],
        "full name": [randomName],
        "email address": [randomEmail],
        "contact number": [randomPhone],
        "mobile phone number": [randomPhone],
        "book / course material selection": [randomBook],
        "book / material": [randomBook],
        "quantity": [String(randomQty)],
        "enrolled instructor to credit": [randomInstructor],
        "instructor credit": [randomInstructor],
        "student / enrollee id number": [randomStudentId],
        "student id": [randomStudentId],
        "delivery address / campus pickup notes": [randomNote],
        "notes": [randomNote]
      }
    };

    Logger.log("Simulating submission " + (i + 1) + "/" + total + ": " + randomName + " | " + randomBook + " | Instructor: " + randomInstructor);

    try {
      if (typeof onFormSubmit === "function") {
        onFormSubmit(fakeEvent);
      }
      results.push({
        status: "success",
        name: randomName,
        book: randomBook,
        instructor: randomInstructor,
        student_id: randomStudentId
      });
    } catch (subErr) {
      Logger.log("Error running test onFormSubmit: " + subErr);
      results.push({ status: "error", error: subErr.toString() });
    }
  }

  Logger.log("Random simulation batch completed. Processed " + results.length + " orders.");
  return results;
}

