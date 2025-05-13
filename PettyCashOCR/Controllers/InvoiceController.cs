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
using System.Globalization;
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

        private (PettyCashVoucher VoucherData, List<VoucherLineItem>, List<BudgeteryDetails>, List<AccountingAllocation>) ParseInvoiceData(string text)
        {
            var voucher = new PettyCashVoucher
            {
                VoucherNo = ExtractValue(text, @"Voucher\s*No\s*\[\s*(\d+)", 1),
                PaidTo = ExtractValue(text, @"Claim\s*By\s*(.+?)\sDepartment", 1),
                Department = ExtractValue(text, @"Department\s*(.+?)\sStation", 1),
                Station = ExtractValue(text, @"Station\s*(.+)", 1).Split(' ')[0],
                StaffNo = ExtractValue(text, @"Staff\s*No\s*(\d+)", 1),
                CostCenter = ExtractValue(text, @"Cost\s*Center\s*(\w+)", 1),
                Date = ExtractValue(text, @"Date\s*(\d{2}/\d{2}/\d{4})", 1),
                AmountInWords = ExtractValue(text, @"Amount in words:\s*(.+)", 1),
                ApprovedBy = ExtractValue(text, @"Approved By\s*(.+)", 1),
                ReceivedCash = ExtractValue(text, @"Received Cash\s*(\d+)", 1),
                TotalAmount = decimal.TryParse(ExtractValue(text, @"Total\s*(\d+)", 1), out var tAmt) ? tAmt : 0
            };

            var lineItems = new List<VoucherLineItem>();
            var itemMatches = Regex.Matches(text, @"(?i)(Book|Pen|Pins)\s+(\d+)\s+00");
            foreach (Match match in itemMatches)
            {
                lineItems.Add(new VoucherLineItem
                {
                    Details = match.Groups[1].Value,
                    Amount = decimal.Parse(match.Groups[2].Value + ".00")
                });
            }

            var budgetDetails = new List<BudgeteryDetails>();
            var budgetMatch = Regex.Match(text, @"(?i)(\d+)\s+(\d+)\s+\|\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)");
            if (budgetMatch.Success)
            {
                budgetDetails.Add(new BudgeteryDetails
                {
                    Account = budgetMatch.Groups[1].Value,
                    CC = budgetMatch.Groups[2].Value,
                    Budget = int.Parse(budgetMatch.Groups[3].Value),
                    Utilised = int.Parse(budgetMatch.Groups[4].Value),
                    Variance = int.Parse(budgetMatch.Groups[5].Value),
                    ThisPayment = decimal.Parse(budgetMatch.Groups[6].Value)
                });
            }

            var allocations = new List<AccountingAllocation>();
            var allocMatch = Regex.Match(text, @"(?i)Accounting Allocation Only.*?(\d+)\s+(\d+)\s+\|(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+00\s+(\d+)\s+(.+)", RegexOptions.Singleline);
            if (allocMatch.Success)
            {
                allocations.Add(new AccountingAllocation
                {
                    Account = allocMatch.Groups[1].Value,
                    CC = allocMatch.Groups[2].Value,
                    FLTNO = allocMatch.Groups[3].Value,
                    ACRFT = allocMatch.Groups[4].Value,
                    PROJ = allocMatch.Groups[5].Value,
                    Amount = $"{allocMatch.Groups[6].Value}.00",
                    CrossReference = allocMatch.Groups[7].Value,
                    Description = allocMatch.Groups[8].Value.Trim()
                });
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
                                  .FirstOrDefault(v => v.Id == id);
            return voucher == null ? NotFound() : View(voucher);
        }

        [HttpPost]
        public IActionResult Edit(PettyCashVoucher voucher)
        {
            if (ModelState.IsValid)
            {
                voucher.LineItems = voucher.LineItems ?? new List<VoucherLineItem>();
                voucher.TotalAmount = voucher.LineItems.Sum(i => i.Amount);

                if (voucher.Id == 0)
                {
                    // Insert voucher
                    _context.Database.ExecuteSqlInterpolated($@"
                INSERT INTO PettyCashVouchers 
                (PaidTo, StaffNo, Department, CostCenter, Station, Date, VoucherNo, AmountInWords, ApprovedBy, ReceivedCash, TotalAmount)
                VALUES 
                ({voucher.PaidTo}, {voucher.StaffNo}, {voucher.Department}, {voucher.CostCenter}, {voucher.Station},
                 {voucher.Date}, {voucher.VoucherNo}, {voucher.AmountInWords}, {voucher.ApprovedBy}, {voucher.ReceivedCash}, {voucher.TotalAmount});
            ");

                    // Get last inserted ID
                    var lastVoucherId = _context.PettyCashVouchers
                                                .OrderByDescending(v => v.Id)
                                                .Select(v => v.Id)
                                                .FirstOrDefault();

                    foreach (var item in voucher.LineItems)
                    {
                        _context.Database.ExecuteSqlInterpolated($@"
                    INSERT INTO VoucherLineItems (VoucherId, Details, Amount)
                    VALUES ({lastVoucherId}, {item.Details}, {item.Amount});
                ");
                    }
                }
                else
                {
                    // Update voucher
                    _context.Database.ExecuteSqlInterpolated($@"
                UPDATE PettyCashVouchers
                SET PaidTo = {voucher.PaidTo},
                    StaffNo = {voucher.StaffNo},
                    Department = {voucher.Department},
                    CostCenter = {voucher.CostCenter},
                    Station = {voucher.Station},
                    Date = {voucher.Date},
                    VoucherNo = {voucher.VoucherNo},
                    AmountInWords = {voucher.AmountInWords},
                    ApprovedBy = {voucher.ApprovedBy},
                    ReceivedCash = {voucher.ReceivedCash},
                    TotalAmount = {voucher.TotalAmount}
                WHERE Id = {voucher.Id};
            ");

                    // Delete old items
                    _context.Database.ExecuteSqlInterpolated($@"
                DELETE FROM VoucherLineItems WHERE VoucherId = {voucher.Id};
            ");

                    // Re-insert line items
                    foreach (var item in voucher.LineItems)
                    {
                        _context.Database.ExecuteSqlInterpolated($@"
                    INSERT INTO VoucherLineItems (VoucherId, Details, Amount)
                    VALUES ({voucher.Id}, {item.Details}, {item.Amount});
                ");
                    }
                }

                TempData["Success"] = "Voucher saved successfully!";
                return RedirectToAction("List");
            }

            return View(voucher);
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
                    // Delete old line items
                    _context.Database.ExecuteSqlInterpolated($@"
                DELETE FROM VoucherLineItems WHERE VoucherId = {model.Id};
            ");

                    // Update main voucher
                    _context.Database.ExecuteSqlInterpolated($@"
                UPDATE PettyCashVouchers
                SET PaidTo = {model.PaidTo},
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
                    // Insert new voucher
                    _context.Database.ExecuteSqlInterpolated($@"
                INSERT INTO dbo.PettyCashVouchers
                (PaidTo, StaffNo, Department, CostCenter, Station, Date, VoucherNo, AmountInWords, ApprovedBy, ReceivedCash, TotalAmount)
                VALUES
                ({model.PaidTo}, {model.StaffNo}, {model.Department}, {model.CostCenter}, {model.Station},
                 {model.Date}, {model.VoucherNo}, {model.AmountInWords}, {model.ApprovedBy}, {model.ReceivedCash}, {model.TotalAmount});
            ");

                    model.Id = _context.PettyCashVouchers
                                       .OrderByDescending(v => v.Id)
                                       .Select(v => v.Id)
                                       .FirstOrDefault();
                }

                // Insert line items
                foreach (var item in model.LineItems)
                {
                    _context.Database.ExecuteSqlInterpolated($@"
                INSERT INTO VoucherLineItems (VoucherId, Details, Amount)
                VALUES ({model.Id}, {item.Details}, {item.Amount});
            ");
                }

                foreach (var Budget in model.BudgeteryDetails)
                {
                    _context.Database.ExecuteSqlInterpolated($@"
                INSERT INTO BudgeteryDetails (VoucherId, Account, CC, Budget, Utilised, Variance, ThisPayment)
                VALUES ({model.Id}, {Budget.Account}, {Budget.CC}, {Budget.Budget}, {Budget.Utilised}, {Budget.Variance}, {Budget.ThisPayment})
            ");
                }

                foreach (var Acc in model.AccountingAllocations)
                {
                    if (string.IsNullOrEmpty(Acc.Account) || string.IsNullOrEmpty(Acc.CC) || string.IsNullOrEmpty(Acc.Amount) ||
                        string.IsNullOrEmpty(Acc.FLTNO) || string.IsNullOrEmpty(Acc.ACRFT) || string.IsNullOrEmpty(Acc.PROJ) ||
                        string.IsNullOrEmpty(Acc.CrossReference) || string.IsNullOrEmpty(Acc.Description))
                    {
                        throw new InvalidOperationException("One or more required fields in AccountingAllocation are null or empty.");
                    }

                    _context.Database.ExecuteSqlInterpolated($@"
                        INSERT INTO AccountingAllocation (VoucherId, Account, CC, FLTNO, ACRFT, PROJ, Amount, CrossReference, Description)
                        VALUES ({model.Id}, {Acc.Account}, {Acc.CC}, {Acc.FLTNO}, {Acc.ACRFT}, {Acc.PROJ}, {Acc.Amount}, {Acc.CrossReference}, {Acc.Description});
                    ");
                }

                TempData["Success"] = "Voucher saved successfully!";
                return RedirectToAction("List");
            }

            return View("Edit", model);
        }


    }
}

