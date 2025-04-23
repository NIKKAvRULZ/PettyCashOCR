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
using Microsoft.Data.SqlClient;
using System.Globalization;

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

        public IActionResult Upload()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Edit(PettyCashVoucher voucher)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    voucher.LineItems = voucher.LineItems ?? new List<VoucherLineItem>();

                    // 👉 Calculate total amount before saving
                    voucher.TotalAmount = voucher.LineItems.Sum(i => i.Amount);

                    if (voucher.Id == 0)
                    {
                        _context.PettyCashVouchers.Add(voucher);
                        _context.SaveChanges();
                    }
                    else
                    {
                        _context.PettyCashVouchers.Update(voucher);
                        _context.SaveChanges();
                    }

                    foreach (var lineItem in voucher.LineItems)
                    {
                        if (lineItem.Id == 0)
                        {
                            lineItem.VoucherId = voucher.Id;
                            _context.VoucherLineItems.Add(lineItem);
                        }
                        else
                        {
                            _context.VoucherLineItems.Update(lineItem);
                        }
                    }

                    _context.SaveChanges();

                    TempData["Success"] = "Voucher saved successfully!";
                    return RedirectToAction("List");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error saving voucher: {ex.Message}");
                }
            }

            return View(voucher);
        }

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
                    {
                        invoiceImage.CopyTo(stream);
                    }

                    string extractedText = ExtractTextFromImage(filePath);

                    var (voucherData, lineItemsData) = ParseInvoiceData(extractedText);

                    var voucher = new PettyCashVoucher
                    {
                        PaidTo = voucherData.PaidTo,
                        Date = voucherData.Date,
                        VoucherNo = voucherData.VoucherNo,
                        Email = voucherData.Email,
                        ContactNo = voucherData.ContactNo,
                        TotalAmount = voucherData.TotalAmount,
                        LineItems = lineItemsData.Select(li => new VoucherLineItem
                        {
                            ItemDate = li.ItemDate,
                            Description = li.Description,
                            Amount = li.Amount
                        }).ToList()
                    };

                    // ✅ Recalculate to ensure accuracy
                    voucher.TotalAmount = voucher.LineItems.Sum(i => i.Amount);

                    ViewBag.ExtractedText = CleanExtractedText(extractedText);
                    return View("Edit", voucher);
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error processing invoice: {ex.Message}");
                    return View();
                }
            }

            ModelState.AddModelError("", "Please upload a valid invoice image.");
            return View();
        }

        [HttpPost]
        public IActionResult Save(PettyCashVoucher model)
        {
            if (ModelState.IsValid)
            {
                // ✅ Ensure total amount is always accurate
                model.TotalAmount = model.LineItems?.Sum(i => i.Amount) ?? 0;

                var existingVoucher = _context.PettyCashVouchers
                    .Include(v => v.LineItems)
                    .FirstOrDefault(v => v.Id == model.Id);

                if (existingVoucher != null)
                {
                    existingVoucher.PaidTo = model.PaidTo;
                    existingVoucher.Date = model.Date;
                    existingVoucher.VoucherNo = model.VoucherNo;
                    existingVoucher.Email = model.Email;
                    existingVoucher.ContactNo = model.ContactNo;
                    existingVoucher.TotalAmount = model.TotalAmount;

                    _context.VoucherLineItems.RemoveRange(existingVoucher.LineItems);
                    existingVoucher.LineItems = model.LineItems;
                }
                else
                {
                    _context.PettyCashVouchers.Add(model);
                }

                _context.SaveChanges();
                return RedirectToAction("List");
            }

            return View("Edit", model);
        }

        public IActionResult List()
        {
            var vouchers = _context.PettyCashVouchers
                                   .Include(v => v.LineItems)
                                   .OrderByDescending(v => v.Id)
                                   .ToList();

            // Debugging
            foreach (var v in vouchers)
            {
                Console.WriteLine($"Voucher: {v.VoucherNo} | TotalAmount: {v.TotalAmount}");
            }

            return View(vouchers);
        }

        public IActionResult Edit(int id)
        {
            var voucher = _context.PettyCashVouchers
                                  .Include(v => v.LineItems)
                                  .FirstOrDefault(v => v.Id == id);

            if (voucher == null)
            {
                return NotFound();
            }

            return View(voucher);
        }

        private string CleanExtractedText(string rawText)
        {
            return string.Join("\n", rawText.Split('\n')
                                            .Select(line => Regex.Replace(line, @"\s{2,}", " ").Trim())
                                            .Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        private string ExtractTextFromImage(string imagePath)
        {
            try
            {
                using (var engine = new TesseractEngine(Path.Combine(_env.ContentRootPath, "tessdata"), "eng", EngineMode.Default))
                using (var img = Pix.LoadFromFile(imagePath))
                {
                    var gray = img.ConvertRGBToGray().BinarizeOtsuAdaptiveThreshold(200, 200, 10, 10, 0.1f);
                    using (var page = engine.Process(gray))
                    {
                        return page.GetText();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR Error: {ex.Message}");
                throw;
            }
        }

        private static (
            (string PaidTo, string Date, string VoucherNo, string Email, string ContactNo, decimal TotalAmount),
            List<(string ItemDate, string Description, decimal Amount)>
        ) ParseInvoiceData(string text)
        {
            var voucherData = ExtractVoucherData(text);
            var lineItems = ParseLineItems(text);
            return (voucherData, lineItems);
        }

        private static (string PaidTo, string Date, string VoucherNo, string Email, string ContactNo, decimal TotalAmount) ExtractVoucherData(string text)
        {
            string paidTo = SafeRegexExtract(text, @"Paid To\s*(\w+\s\w+)", 1, "Paid To");
            string date = SafeRegexExtract(text, @"Date\s*(\d{2}/\d{2}/\d{4})", 1, "Date");
            string voucherNo = SafeRegexExtract(text, @"Voucher No\s*(\d+)", 1, "Voucher No");
            string email = SafeRegexExtract(text, @"Email\s*([\w\.-]+@[\w\.-]+)", 1, "Email");
            string contactNo = SafeRegexExtract(text, @"Contact No\s*(\d+)", 1, "Contact No");
            decimal totalAmount = decimal.TryParse(SafeRegexExtract(text, @"Final Amount\s*(\d+\.\d{2})", 1, "Total Amount"), out decimal result) ? result : 0;

            return (paidTo, date, voucherNo, email, contactNo, totalAmount);
        }

        private static string SafeRegexExtract(string text, string pattern, int group, string fieldName)
        {
            try
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                return match.Success && match.Groups.Count > group ? match.Groups[group].Value : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting {fieldName}: {ex.Message}");
                return null;
            }
        }

        private static List<(string ItemDate, string Description, decimal Amount)> ParseLineItems(string text)
        {
            var lineItems = new List<(string, string, decimal)>();
            var lines = text.Split('\n');

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var match = Regex.Match(line.Trim(), @"^\d+\s+(\d{2}/\d{2}/\d{4})\s+(.+?)\s+(\d+\.\d{2})$");
                if (match.Success)
                {
                    string date = match.Groups[1].Value;
                    string description = match.Groups[2].Value;
                    decimal amount = decimal.TryParse(match.Groups[3].Value.Replace(",", ""), out var parsedAmount) ? parsedAmount : 0;
                    lineItems.Add((date, description, amount));
                }
            }

            return lineItems;
        }
    }
}
