using Microsoft.AspNetCore.Mvc;
using PettyCashOCR.Models;
using Microsoft.AspNetCore.Hosting;
using Tesseract;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;
using Microsoft.AspNetCore.Http;
using PettyCashOCR.Data;

namespace PettyCashOCR.Controllers
{
    public class InvoiceController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public InvoiceController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        public IActionResult Upload() => View();

        [HttpPost]
        public IActionResult Upload(IFormFile invoiceImage)
        {
            if (invoiceImage != null && invoiceImage.Length > 0)
            {
                try
                {
                    var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    var filePath = Path.Combine(uploadsFolder, invoiceImage.FileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                        invoiceImage.CopyTo(stream);

                    string extractedText = ExtractTextFromImage(filePath);
                    Console.WriteLine("Extracted OCR Text:\n"+ extractedText);

                    var (voucherData, lineItems, budgetDetails, accountingAllocations) = ParseInvoiceData(extractedText);

                    var voucher = new PettyCashVoucher
                    {
                        PaidTo = voucherData.PaidTo,
                        StaffNo = voucherData.StaffNo,
                        Department = voucherData.Department,
                        CostCenter = voucherData.CostCenter,
                        Station = voucherData.Station,
                        Date = voucherData.Date,
                        VoucherNo = voucherData.VoucherNo,
                        AmountInWords = voucherData.AmountInWords,
                        ApprovedBy = voucherData.ApprovedBy,
                        ReceivedCash = voucherData.ReceivedCash,
                        TotalAmount = voucherData.TotalAmount,
                        LineItems = lineItems,
                        BudgeteryDetails = budgetDetails,
                        AccountingAllocations = accountingAllocations
                    };

                    return View("Edit", voucher);
                }
                catch (Exception ex) 
                {
                    ModelState.AddModelError("", $"Error processing invoice: {ex.Message}");
                }
            }

            ModelState.AddModelError("", "Please upload a valid invoice image.");
            return View();
        }

        private string ExtractTextFromImage(string imagePath)
        {
            var tessDataPath = Path.Combine(_env.ContentRootPath, "tessdata");
            using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
            using var img = Pix.LoadFromFile(imagePath);
            using var page = engine.Process(img);
            return page.GetText();
        }

        private (PettyCashVoucher, List<VoucherLineItem>, List<BudgeteryDetails>, List<AccountingAllocation>) ParseInvoiceData(string text)
        {
            var voucher = new PettyCashVoucher();

            // Extract VoucherNo (look for "VOUCHER NO" followed by digits)
            voucher.VoucherNo = ExtractValue(text, @"VOUCHER\s*NO\s*[:\s]*([\w\d]+)", 1);

            // Extract PaidTo (after claimant signature or Claim By)
            voucher.PaidTo = ExtractValue(text, @"Signature of Claimant.*?([A-Z\s]+)\s*\|", 1);
            if (string.IsNullOrEmpty(voucher.PaidTo))
                voucher.PaidTo = ExtractValue(text, @"Claim\s*By\s*(.+?)\s*Department", 1);

            // Department (line with "Department" followed by dots/spaces)
            voucher.Department = ExtractValue(text, @"Department\s*\.{3,}\s*([\w\s]+)", 1);

            // CostCenter (look for "CostCentre" or "Cost Center" with some leniency)
            voucher.CostCenter = ExtractValue(text, @"Cost\s*Cent(?:re|er)\s*\.{3,}\s*([\w\d]+)", 1);

            // Station (can be tricky if missing, fallback empty)
            voucher.Station = ExtractValue(text, @"Station\s*\.{3,}\s*([\w\s]+)", 1);

            // StaffNo (look for Staff No or Staff Number followed by digits)
            voucher.StaffNo = ExtractValue(text, @"Staff\s*No\s*[:\s]*([\d]+)", 1);

            // Date (look for Date: dd/mm/yyyy)
            voucher.Date = ExtractValue(text, @"Date\s*[:\s]*(\d{2}/\d{2}/\d{4})", 1);

            // AmountInWords
            voucher.AmountInWords = ExtractValue(text, @"Amount\s*in\s*words\s*[:\s]*(.+)", 1);

            // ApprovedBy (near "Nishan PERERA" or "Approved By")
            voucher.ApprovedBy = ExtractValue(text, @"Approved\s*By\s*[:\s]*(.+)", 1);
            //if (string.IsNullOrEmpty(voucher.ApprovedBy))
            //    voucher.ApprovedBy = ExtractValue(text, @"Nishan\s*PERERA", 0) == "Nishan PERERA" ? "Nishan PERERA" : "";

            // ReceivedCash (numbers near "Received Cash")
            voucher.ReceivedCash = ExtractValue(text, @"Received\s*Cash\s*[:\s]*(\d+)", 1);

            // TotalAmount (look for TOTAL followed by amount)
            var totalStr = ExtractValue(text, @"TOTAL\s*\|?\s*(\d+)", 1);
            voucher.TotalAmount = decimal.TryParse(totalStr, out var total) ? total : 0;

            // --- Parse Line Items ---
            var lineItems = new List<VoucherLineItem>();

            // The OCR shows lines like: Item1 1000 00
            // We'll capture lines that start with "Item" or similar, followed by amount parts
            var lineItemRegex = new Regex(@"(Item\d*|ltem\d*)\s+(\d+)\s+(\d+)", RegexOptions.IgnoreCase);
            foreach (Match match in lineItemRegex.Matches(text))
            {
                if (decimal.TryParse($"{match.Groups[2].Value}.{match.Groups[3].Value}", out var amount))
                {
                    lineItems.Add(new VoucherLineItem
                    {
                        Details = match.Groups[1].Value, // Item name
                        Amount = amount
                    });
                }
            }

            // --- Parse Budgetery Details ---
            var budgetDetails = new List<BudgeteryDetails>();
            // OCR text example line:
            // 11258 | | 4000 13000 1000 5000
            // Let's capture numbers on a line with 5 or 6 groups of digits
            var budgetRegex = new Regex(@"(\d+)\s*\|?\s*\|?\s*(\d*)\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)", RegexOptions.IgnoreCase);
            foreach (Match match in budgetRegex.Matches(text))
            {
                budgetDetails.Add(new BudgeteryDetails
                {
                    Account = match.Groups[1].Value,
                    CC = match.Groups[2].Value,
                    Budget = int.TryParse(match.Groups[3].Value, out var b) ? b : 0,
                    Utilised = int.TryParse(match.Groups[4].Value, out var u) ? u : 0,
                    Variance = int.TryParse(match.Groups[5].Value, out var v) ? v : 0,
                    ThisPayment = decimal.TryParse(match.Groups[6].Value, out var tp) ? tp : 0
                });
            }

            // --- Parse Accounting Allocations ---
            var allocations = new List<AccountingAllocation>();
            // The section header is "ACCOUNTING ALLOCATION ONLY"
            // Then lines with multiple columns separated by pipe or spaces
            // e.g. ACCOUNT| C.C. | FLT NO| ACRFT | PROJ AMOUNT | CROSS REFERENCE DESCRIPTION

            var allocStartIndex = text.IndexOf("ACCOUNTING ALLOCATION ONLY", StringComparison.OrdinalIgnoreCase);
            if (allocStartIndex >= 0)
            {
                var allocText = text.Substring(allocStartIndex);
                // Match lines with account allocations - this may need tuning depending on actual data format
                var allocRegex = new Regex(
                    @"(\w+)\s*\|\s*(\w*)\s*\|\s*(\w*)\s*\|\s*(\w*)\s*\|\s*(\w*)\s*\|\s*(\d+)\s*\|\s*(\w+)\s*\|\s*(.+)",
                    RegexOptions.IgnoreCase);
                foreach (Match match in allocRegex.Matches(allocText))
                {
                    allocations.Add(new AccountingAllocation
                    {
                        Account = match.Groups[1].Value,
                        CC = match.Groups[2].Value,
                        FLTNO = match.Groups[3].Value,
                        ACRFT = match.Groups[4].Value,
                        PROJ = match.Groups[5].Value,
                        Amount = match.Groups[6].Value,
                        CrossReference = match.Groups[7].Value,
                        Description = match.Groups[8].Value.Trim()
                    });
                }
            }

            return (voucher, lineItems, budgetDetails, allocations);
        }


        private string ExtractValue(string text, string pattern, int group = 1)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[group].Value.Trim() : string.Empty;
        }

        public IActionResult List()
        {
            var vouchers = _context.PettyCashVouchers
                                   .Include(v => v.LineItems)
                                   .OrderByDescending(v => v.Id)
                                   .ToList();
            return View(vouchers);
        }

        public IActionResult Edit(int id)
        {
            var voucher = _context.PettyCashVouchers
                                  .Include(v => v.LineItems)
                                  .Include(v => v.BudgeteryDetails)
                                  .Include(v => v.AccountingAllocations)
                                  .FirstOrDefault(v => v.Id == id);
            return voucher == null ? NotFound() : View(voucher);
        }

        [HttpPost]
        public IActionResult Save(PettyCashVoucher model)
        {
            if (ModelState.IsValid)
            {
                model.TotalAmount = model.LineItems?.Sum(i => i.Amount) ?? 0;

                var exists = _context.PettyCashVouchers.Any(v => v.Id == model.Id);

                if (exists)
                {
                    _context.Database.ExecuteSqlInterpolated($@"
                DELETE FROM VoucherLineItems WHERE VoucherId = {model.Id};
                DELETE FROM BudgeteryDetails WHERE VoucherId = {model.Id};
                DELETE FROM AccountingAllocation WHERE VoucherId = {model.Id};

                UPDATE PettyCashVouchers SET
                    PaidTo = {model.PaidTo},
                    StaffNo = {model.StaffNo},
                    Department = {model.Department},
                    CostCenter = {model.CostCenter},
                    Station = {model.Station},
                    Date = {model.Date},
                    VoucherNo = {model.VoucherNo},
                    AmountInWords = {model.AmountInWords},
                    ApprovedBy = {model.ApprovedBy},
                    ReceivedCash = {model.ReceivedCash},
                    TotalAmount = {model.TotalAmount}
                WHERE Id = {model.Id};
            ");
                }
                else
                {
                    _context.Database.ExecuteSqlInterpolated($@"
                INSERT INTO PettyCashVouchers
                (PaidTo, StaffNo, Department, CostCenter, Station, Date, VoucherNo, AmountInWords, ApprovedBy, ReceivedCash, TotalAmount)
                VALUES
                ({model.PaidTo}, {model.StaffNo}, {model.Department}, {model.CostCenter}, {model.Station},
                {model.Date}, {model.VoucherNo}, {model.AmountInWords}, {model.ApprovedBy}, {model.ReceivedCash}, {model.TotalAmount});
            ");

                    // Get the ID of the newly inserted voucher
                    model.Id = _context.PettyCashVouchers
                                       .OrderByDescending(v => v.Id)
                                       .Select(v => v.Id)
                                       .FirstOrDefault();
                }

                // Insert Line Items
                foreach (var item in model.LineItems ?? new List<VoucherLineItem>())
                {
                    _context.Database.ExecuteSqlInterpolated($@"
                INSERT INTO VoucherLineItems (VoucherId, Details, Amount)
                VALUES ({model.Id}, {item.Details}, {item.Amount});
            ");
                }

                // Insert Budgetery Details
                foreach (var budget in model.BudgeteryDetails ?? new List<BudgeteryDetails>())
                {
                    _context.Database.ExecuteSqlInterpolated($@"
                INSERT INTO BudgeteryDetails (VoucherId, Account, CC, Budget, Utilised, Variance, ThisPayment)
                VALUES ({model.Id}, {budget.Account}, {budget.CC}, {budget.Budget}, {budget.Utilised}, {budget.Variance}, {budget.ThisPayment});
            ");
                }

                // Insert Accounting Allocations
                foreach (var alloc in model.AccountingAllocations ?? new List<AccountingAllocation>())
                {
                    _context.Database.ExecuteSqlInterpolated($@"
                INSERT INTO AccountingAllocation (VoucherId, Account, CC, FLTNO, ACRFT, PROJ, Amount, CrossReference, Description)
                VALUES ({model.Id}, {alloc.Account}, {alloc.CC}, {alloc.FLTNO}, {alloc.ACRFT}, {alloc.PROJ}, {alloc.Amount}, {alloc.CrossReference}, {alloc.Description});
            ");
                }

                return RedirectToAction("List");
            }

            return View("Edit", model);
        }


    }
}
