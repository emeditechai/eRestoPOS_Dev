using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using RestaurantManagementSystem.Helpers;
using RestaurantManagementSystem.Models;
using RestaurantManagementSystem.ViewModels;

namespace RestaurantManagementSystem.Services
{
    /// <summary>
    /// Builds ESC/POS byte arrays mirroring the PrintPOS.cshtml receipt layout
    /// and optionally sends them via TCP to a WiFi-connected thermal printer.
    /// Designed for 58mm (32-char), 76mm (40-char), and 80mm (42-char) paper.
    /// </summary>
    public class EscPosService
    {
        // ── ESC/POS command constants ─────────────────────────────────────────
        private static readonly byte[] ESC_INIT     = { 0x1B, 0x40 };
        private static readonly byte[] ESC_ALIGN_L  = { 0x1B, 0x61, 0x00 };
        private static readonly byte[] ESC_ALIGN_C  = { 0x1B, 0x61, 0x01 };
        private static readonly byte[] ESC_BOLD_ON  = { 0x1B, 0x45, 0x01 };
        private static readonly byte[] ESC_BOLD_OFF = { 0x1B, 0x45, 0x00 };
        private static readonly byte[] GS_DOUBLE    = { 0x1D, 0x21, 0x11 };   // 2× width + 2× height
        private static readonly byte[] GS_NORMAL    = { 0x1D, 0x21, 0x00 };
        private static readonly byte[] ESC_FEED3    = { 0x1B, 0x64, 0x03 };
        private static readonly byte[] GS_CUT_PART  = { 0x1D, 0x56, 0x42, 0x00 };
        private static readonly byte[] LF           = { 0x0A };

        // ── Paper-width helpers ───────────────────────────────────────────────
        private static int GetLineWidth(string paperSize) => paperSize switch
        {
            "58mm" => 32,
            "76mm" => 40,
            _      => 42   // 80mm
        };

        /// <summary>
        /// Item column widths (name, qty, price, amt) that fit exactly in <paramref name="width"/> chars.
        /// Price/Amt columns are sized for "Rs.9999" (7 chars) or "Rs.99999" (8 chars).
        /// </summary>
        private static (int name, int qty, int price, int amt) GetItemCols(int width)
        {
            return width switch
            {
                32 => (14, 3, 8, 7),    // 14+3+8+7 = 32
                40 => (20, 4, 8, 8),    // 20+4+8+8 = 40
                _  => (22, 4, 8, 8)     // 22+4+8+8 = 42 (80mm)
            };
        }

        // ── String helpers ────────────────────────────────────────────────────
        private static string Dashes(int w) => new string('-', w);
        private static string Equals_(int w) => new string('=', w);

        /// <summary>Word-wrap <paramref name="text"/> at <paramref name="maxWidth"/> chars, breaking at spaces.</summary>
        private static List<string> WordWrap(string text, int maxWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return lines;
            if (maxWidth <= 0) { lines.Add(text); return lines; }

            var words = text.Split(' ');
            var current = new System.Text.StringBuilder();

            foreach (var word in words)
            {
                if (word.Length == 0) continue;
                if (current.Length == 0)
                {
                    // Single word longer than max → hard-break it
                    var w = word;
                    while (w.Length > maxWidth)
                    {
                        lines.Add(w.Substring(0, maxWidth));
                        w = w.Substring(maxWidth);
                    }
                    current.Append(w);
                }
                else if (current.Length + 1 + word.Length <= maxWidth)
                {
                    current.Append(' ').Append(word);
                }
                else
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    var w = word;
                    while (w.Length > maxWidth)
                    {
                        lines.Add(w.Substring(0, maxWidth));
                        w = w.Substring(maxWidth);
                    }
                    current.Append(w);
                }
            }
            if (current.Length > 0) lines.Add(current.ToString());
            return lines;
        }

        /// <summary>
        /// Right-aligns <paramref name="value"/> and pads <paramref name="label"/> to fill <paramref name="width"/>.
        /// </summary>
        private static string LabelValue(string label, string value, int width)
        {
            int maxLbl = width - value.Length - 1;
            if (maxLbl < 0) maxLbl = 0;
            if (label.Length > maxLbl) label = label.Substring(0, maxLbl);
            return label.PadRight(width - value.Length) + value;
        }

        private static void Add(List<byte> buf, byte[] bytes) => buf.AddRange(bytes);
        private static void AddLine(List<byte> buf, string text, Encoding enc)
        {
            buf.AddRange(enc.GetBytes(text));
            buf.AddRange(LF);
        }

        // ── Main public method ────────────────────────────────────────────────
        public byte[] BuildReceiptBytes(
            PaymentViewModel model,
            RestaurantSettings settings,
            string posPaperSize,
            string counterDisplay,
            string printBranchName,
            bool includeKot,
            IList<dynamic> kotItems,
            string kotTicketNumber)
        {
            Encoding enc;
            try { enc = Encoding.GetEncoding(437); }
            catch { enc = Encoding.ASCII; }

            int width = GetLineWidth(posPaperSize);
            var (colName, colQty, colPrice, colAmt) = GetItemCols(width);

            var buf = new List<byte>(1024);

            // ── Initialize ──────────────────────────────────────────────────
            Add(buf, ESC_INIT);

            // ── Shop Header ─────────────────────────────────────────────────
            Add(buf, ESC_ALIGN_C);
            Add(buf, ESC_BOLD_ON);
            string shopName = SafeText(settings.RestaurantName?.ToUpperInvariant() ?? "", enc);
            foreach (var nameLine in WordWrap(shopName, width))
                AddLine(buf, nameLine, enc);
            Add(buf, ESC_BOLD_OFF);

            // Address lines — centered, normal size
            if (!string.IsNullOrWhiteSpace(settings.StreetAddress))
                foreach (var l in WordWrap(SafeText(settings.StreetAddress, enc), width))
                    AddLine(buf, l, enc);

            if (!string.IsNullOrWhiteSpace(settings.City))
            {
                string cityLine = $"{settings.City} {settings.State} - {settings.Pincode}".Trim();
                foreach (var l in WordWrap(SafeText(cityLine, enc), width))
                    AddLine(buf, l, enc);
            }

            if (!string.IsNullOrWhiteSpace(printBranchName))
                AddLine(buf, SafeText($"Branch: {printBranchName}", enc), enc);

            if (!string.IsNullOrWhiteSpace(settings.PhoneNumber))
                AddLine(buf, SafeText($"Phone: {settings.PhoneNumber}", enc), enc);

            if (!string.IsNullOrWhiteSpace(settings.GSTCode))
                AddLine(buf, SafeText($"GSTIN: {settings.GSTCode}", enc), enc);

            Add(buf, ESC_ALIGN_L);
            AddLine(buf, Dashes(width), enc);

            // ── Bill Meta ───────────────────────────────────────────────────
            string customerName = string.IsNullOrWhiteSpace(model.CustomerName)
                ? new string('_', Math.Max(10, width - 6))
                : model.CustomerName;
            AddLine(buf, SafeText($"Name: {customerName}", enc), enc);

            string dineInfo = model.OrderType == 0
                ? $"Table:{model.TableName}"
                : model.OrderType == 4
                    ? $"Room:{(string.IsNullOrWhiteSpace(model.RoomNo) ? (model.RoomId?.ToString() ?? "-") : model.RoomNo)}"
                    : "";

            string dateLine = $"Date: {DateTime.Now:dd/MM/yy}";
            if (!string.IsNullOrWhiteSpace(dineInfo))
                AddLine(buf, SafeText(LabelValue(dateLine, dineInfo, width), enc), enc);
            else
                AddLine(buf, SafeText(dateLine, enc), enc);

            string cashier = model.Payments?.FirstOrDefault()?.ProcessedByName ?? "Cashier";
            AddLine(buf, SafeText(LabelValue($"Time: {DateTime.Now:HH:mm}", $"By:{cashier}", width), enc), enc);

            if (!string.IsNullOrWhiteSpace(counterDisplay))
                AddLine(buf, SafeText($"Counter: {counterDisplay}", enc), enc);

            AddLine(buf, SafeText($"Bill No: {model.OrderNumber}", enc), enc);
            AddLine(buf, Dashes(width), enc);

            // ── Item Lines ──────────────────────────────────────────────────
            var mainItems = (model.OrderItems ?? new List<OrderItemViewModel>())
                .Where(i => !i.IsExtraCharge).ToList();
            var extraItems = (model.OrderItems ?? new List<OrderItemViewModel>())
                .Where(i => i.IsExtraCharge).ToList();

            // Column header
            Add(buf, ESC_BOLD_ON);
            // For 58mm: "Item          " (14) + "Qty" (3) + "  Rate  " (8) + "   Amt " (7) = 32
            string hdr = "Item".PadRight(colName)
                + "Qty".PadLeft(colQty)
                + "Rate".PadLeft(colPrice)
                + "Amt".PadLeft(colAmt);
            AddLine(buf, SafeText(hdr, enc), enc);
            Add(buf, ESC_BOLD_OFF);
            AddLine(buf, Dashes(width), enc);

            foreach (var item in mainItems)
            {
                string name = SafeText(item.MenuItemName ?? "", enc);
                // Format price/amt to fit — drop decimal for whole numbers, cap width
                string priceStr = FormatAmount(item.UnitPrice, colPrice);
                string amtStr   = FormatAmount(item.Subtotal,  colAmt);
                string qtyStr   = item.Quantity.ToString().PadLeft(colQty);

                if (name.Length <= colName)
                {
                    // Single line
                    AddLine(buf, name.PadRight(colName) + qtyStr + priceStr + amtStr, enc);
                }
                else
                {
                    // First line: wrapped name portion only
                    AddLine(buf, name.Substring(0, colName), enc);
                    name = name.Substring(colName);
                    // Subsequent name wraps (indent 2)
                    while (name.Length > colName - 2)
                    {
                        AddLine(buf, "  " + name.Substring(0, colName - 2), enc);
                        name = name.Substring(colName - 2);
                    }
                    // Last line with qty/price/amt
                    AddLine(buf, ("  " + name).PadRight(colName) + qtyStr + priceStr + amtStr, enc);
                }
            }

            // ── Totals ──────────────────────────────────────────────────────
            AddLine(buf, Dashes(width), enc);

            int totalQty = mainItems.Sum(i => i.Quantity);
            decimal mainSubtotal = mainItems.Sum(i => i.Subtotal);
            decimal displaySubtotal = extraItems.Any() ? mainSubtotal : model.Subtotal;

            decimal taxTotal = 0M, gstPercent = 0M;
            try { taxTotal   = model.TaxAmount; }     catch { }
            try { gstPercent = model.GSTPercentage; } catch { }
            var cgstAmt  = Math.Round(taxTotal / 2, 2);
            var sgstAmt  = Math.Round(taxTotal - cgstAmt, 2);
            var cgstPct  = Math.Round(gstPercent / 2, 2);
            var sgstPct  = Math.Round(gstPercent - cgstPct, 2);
            var discount = model.DiscountAmount > 0 ? model.DiscountAmount : 0M;
            var total    = model.TotalAmount;
            var totalRounded = Math.Round(total, 0, MidpointRounding.AwayFromZero);
            var roundOff = Math.Round(totalRounded - total, 2, MidpointRounding.AwayFromZero);

            if (totalQty > 0)
                AddLine(buf, LabelValue("Total Qty", totalQty.ToString(), width), enc);

            AddLine(buf, SafeText(LabelValue("Sub Total", $"Rs.{displaySubtotal:F2}", width), enc), enc);

            foreach (var item in extraItems)
                AddLine(buf, SafeText(LabelValue(
                    SafeText(item.MenuItemName ?? "", enc),
                    $"Rs.{item.Subtotal:F2}", width), enc), enc);

            if (model.IsInclusiveGST && taxTotal > 0)
                AddLine(buf, LabelValue("Taxable Value", $"Rs.{model.Subtotal:F2}", width), enc);

            if (cgstAmt > 0)
                AddLine(buf, LabelValue($"CGST {cgstPct}%", $"Rs.{cgstAmt:F2}", width), enc);
            if (sgstAmt > 0)
                AddLine(buf, LabelValue($"SGST {sgstPct}%", $"Rs.{sgstAmt:F2}", width), enc);

            if (discount > 0)
                AddLine(buf, LabelValue("Discount", $"-Rs.{discount:F2}", width), enc);

            if (roundOff != 0)
            {
                string roStr = roundOff > 0 ? $"+{roundOff:F2}" : $"{roundOff:F2}";
                AddLine(buf, LabelValue("Round Off", roStr, width), enc);
            }

            // ── Grand Total ──────────────────────────────────────────────────
            AddLine(buf, Dashes(width), enc);
            Add(buf, ESC_ALIGN_C);
            Add(buf, ESC_BOLD_ON);
            AddLine(buf, "Grand Total", enc);
            AddLine(buf, SafeText($"Rs.{totalRounded:F0}", enc), enc);
            Add(buf, ESC_BOLD_OFF);
            Add(buf, ESC_ALIGN_L);

            // In Words — word-wrap at full width
            try
            {
                string inWords = AmountToWords.Convert(totalRounded);
                string fullLine = $"In Words: {inWords}";
                foreach (var l in WordWrap(SafeText(fullLine, enc), width))
                    AddLine(buf, l, enc);
            }
            catch { }

            AddLine(buf, Equals_(width), enc);

            // ── Footer ──────────────────────────────────────────────────────
            Add(buf, ESC_ALIGN_C);
            if (!string.IsNullOrWhiteSpace(settings.FssaiNo))
            {
                string fssaiLine = $"FSSAI: {settings.FssaiNo}";
                foreach (var l in WordWrap(SafeText(fssaiLine, enc), width))
                    AddLine(buf, l, enc);
            }
            Add(buf, ESC_BOLD_ON);
            AddLine(buf, "** Thank You! Visit Again **", enc);
            Add(buf, ESC_BOLD_OFF);
            Add(buf, ESC_ALIGN_L);

            Add(buf, ESC_FEED3);
            Add(buf, GS_CUT_PART);

            // ── KOT Section ─────────────────────────────────────────────────
            if (includeKot && kotItems != null && kotItems.Any())
            {
                Add(buf, ESC_ALIGN_C);
                Add(buf, ESC_BOLD_ON);
                foreach (var nl in WordWrap(SafeText(settings.RestaurantName?.ToUpperInvariant() ?? "", enc), width))
                    AddLine(buf, nl, enc);
                Add(buf, ESC_BOLD_OFF);

                if (!string.IsNullOrWhiteSpace(printBranchName))
                    AddLine(buf, SafeText($"Branch: {printBranchName}", enc), enc);

                AddLine(buf, Dashes(width), enc);

                Add(buf, ESC_BOLD_ON);
                Add(buf, GS_DOUBLE);
                AddLine(buf, "KOT", enc);
                Add(buf, GS_NORMAL);

                if (!string.IsNullOrWhiteSpace(kotTicketNumber))
                    AddLine(buf, SafeText(kotTicketNumber, enc), enc);

                AddLine(buf, SafeText($"Order: {model.OrderNumber}", enc), enc);
                Add(buf, ESC_BOLD_OFF);
                AddLine(buf, SafeText($"{DateTime.Now:dd/MM/yy  HH:mm}", enc), enc);
                AddLine(buf, Dashes(width), enc);
                Add(buf, ESC_ALIGN_L);

                int kotQtyCol = 5;
                Add(buf, ESC_BOLD_ON);
                AddLine(buf, "Item".PadRight(width - kotQtyCol) + "Qty".PadLeft(kotQtyCol), enc);
                Add(buf, ESC_BOLD_OFF);
                AddLine(buf, Dashes(width), enc);

                foreach (var item in kotItems)
                {
                    string kotName = SafeText((string)(item.Name ?? ""), enc);
                    string kotQty  = ((int)item.Quantity).ToString().PadLeft(kotQtyCol);
                    int nameCol = width - kotQtyCol;

                    if (kotName.Length <= nameCol)
                    {
                        AddLine(buf, kotName.PadRight(nameCol) + kotQty, enc);
                    }
                    else
                    {
                        AddLine(buf, kotName.Substring(0, nameCol), enc);
                        kotName = kotName.Substring(nameCol);
                        while (kotName.Length > nameCol - 2)
                        {
                            AddLine(buf, "  " + kotName.Substring(0, nameCol - 2), enc);
                            kotName = kotName.Substring(nameCol - 2);
                        }
                        AddLine(buf, ("  " + kotName).PadRight(nameCol) + kotQty, enc);
                    }

                    string si = item.SpecialInstructions as string;
                    if (!string.IsNullOrEmpty(si))
                        foreach (var l in WordWrap(SafeText($"  * {si}", enc), width))
                            AddLine(buf, l, enc);
                }

                AddLine(buf, Dashes(width), enc);
                Add(buf, ESC_ALIGN_C);
                Add(buf, ESC_BOLD_ON);
                AddLine(buf, "--- End of KOT ---", enc);
                Add(buf, ESC_BOLD_OFF);
                Add(buf, ESC_ALIGN_L);

                Add(buf, ESC_FEED3);
                Add(buf, GS_CUT_PART);
            }

            return buf.ToArray();
        }

        /// <summary>
        /// Formats a monetary amount to fit in <paramref name="colWidth"/> chars.
        /// Uses F0 (no decimal) unless the amount has a decimal part.
        /// Already right-padded.
        /// </summary>
        private static string FormatAmount(decimal amount, int colWidth)
        {
            string s = amount == Math.Floor(amount)
                ? $"Rs.{amount:F0}"
                : $"Rs.{amount:F2}";
            // If still too wide, drop decimal
            if (s.Length > colWidth)
                s = $"Rs.{Math.Round(amount, 0):F0}";
            return s.PadLeft(colWidth);
        }

        /// <summary>
        /// Sends ESC/POS bytes to a WiFi thermal printer via raw TCP socket.
        /// </summary>
        public async Task SendViaTcpAsync(string printerIp, int printerPort, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(printerIp))
                throw new ArgumentException("Printer IP address is not configured.");
            if (printerPort <= 0) printerPort = 9100;

            using var client = new TcpClient();
            client.SendTimeout    = 5000;
            client.ReceiveTimeout = 5000;

            await client.ConnectAsync(printerIp, printerPort);
            using var stream = client.GetStream();
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
        }

        // ── Private helpers ───────────────────────────────────────────────────
        private static string SafeText(string text, Encoding enc)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            text = text.Replace("₹", "Rs.").Replace("\u20b9", "Rs.");
            var bytes = enc.GetBytes(text);
            return enc.GetString(bytes);
        }
    }
}

