using System;

namespace RestaurantManagementSystem.Helpers
{
    /// <summary>
    /// Converts a decimal rupee amount to its Indian-English words representation.
    /// E.g.  120.50  →  "One Hundred Twenty Rupees and Fifty Paise Only"
    /// </summary>
    public static class AmountToWords
    {
        private static readonly string[] Ones =
        {
            "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
            "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen",
            "Sixteen", "Seventeen", "Eighteen", "Nineteen"
        };

        private static readonly string[] Tens =
        {
            "", "", "Twenty", "Thirty", "Forty", "Fifty",
            "Sixty", "Seventy", "Eighty", "Ninety"
        };

        /// <summary>
        /// Converts a decimal amount to words (Indian numbering system).
        /// Supports up to 99,99,99,999.99 (99 crore 99 lakh 99 thousand 9 hundred 99).
        /// </summary>
        public static string Convert(decimal amount)
        {
            if (amount < 0) return "Minus " + Convert(-amount);
            if (amount == 0) return "Zero Rupees Only";

            long rupees = (long)Math.Floor(amount);
            int  paise  = (int)Math.Round((amount - rupees) * 100);

            string result = NumberToWords(rupees) + " Rupee" + (rupees != 1 ? "s" : "");
            if (paise > 0)
                result += " and " + NumberToWords(paise) + " Paise";
            result += " Only";

            return result;
        }

        // ── private helpers ──────────────────────────────────────────────────

        private static string NumberToWords(long number)
        {
            if (number == 0) return "";

            if (number < 20)
                return Ones[number];

            if (number < 100)
                return Tens[number / 10] + (number % 10 > 0 ? " " + Ones[number % 10] : "");

            if (number < 1_000)
                return Ones[number / 100] + " Hundred"
                    + (number % 100 > 0 ? " " + NumberToWords(number % 100) : "");

            if (number < 1_00_000)          // up to 99,999
                return NumberToWords(number / 1_000) + " Thousand"
                    + (number % 1_000 > 0 ? " " + NumberToWords(number % 1_000) : "");

            if (number < 1_00_00_000)       // up to 99,99,999
                return NumberToWords(number / 1_00_000) + " Lakh"
                    + (number % 1_00_000 > 0 ? " " + NumberToWords(number % 1_00_000) : "");

            // crores
            return NumberToWords(number / 1_00_00_000) + " Crore"
                + (number % 1_00_00_000 > 0 ? " " + NumberToWords(number % 1_00_00_000) : "");
        }
    }
}
