using System;
using System.Collections.Generic;
using System.Linq;

namespace AntiquePasabuyPOS.Services;

/// <summary>
/// Normalizes and handles barcode representations across global standards:
/// - EAN-8 (8 digits)
/// - EAN-13 (13 digits)
/// - UPC-A (12 digits)
/// - UPC-E (6-8 digits expanded to 12-digit UPC-A)
/// - GTIN-14 (14 digits)
/// - Handles leading zeros, UPC/EAN cross-standard equivalence, and sanitization.
/// </summary>
public static class BarcodeNormalizer
{
    /// <summary>
    /// Cleans and sanitizes raw input from camera scanner or manual entry.
    /// Removes whitespace, hyphens, and formatting artifacts.
    /// </summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var trimmed = raw.Trim();

        // If it consists of digits separated by hyphens or spaces, strip separators
        if (trimmed.Any(c => c == '-' || c == ' ' || c == '.'))
        {
            var withoutSeparators = new string(trimmed.Where(c => c != '-' && c != ' ' && c != '.').ToArray());
            if (withoutSeparators.Length >= 6 && withoutSeparators.All(char.IsDigit))
            {
                return withoutSeparators;
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Generates an ordered, deduplicated list of candidate barcode representations
    /// to maximize lookup matching across both local database and external registries.
    /// </summary>
    public static List<string> GetNormalizedCandidates(string? rawBarcode)
    {
        var cleaned = Clean(rawBarcode);
        if (string.IsNullOrWhiteSpace(cleaned)) return [];

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? val)
        {
            if (!string.IsNullOrWhiteSpace(val) && seen.Add(val))
            {
                results.Add(val);
            }
        }

        // 1. Primary cleaned barcode
        Add(cleaned);

        bool isAllDigits = cleaned.All(char.IsDigit);

        if (isAllDigits)
        {
            int len = cleaned.Length;

            // 2. UPC-A (12 digits) -> Equivalent 13-digit EAN-13 (padded with leading 0)
            if (len == 12)
            {
                Add("0" + cleaned);
                Add("00" + cleaned); // 14-digit GTIN representation
                if (cleaned.StartsWith("0"))
                {
                    Add(cleaned.TrimStart('0')); // In case local DB stripped leading zero
                }
            }
            // 3. EAN-13 (13 digits)
            else if (len == 13)
            {
                // If it starts with '0', the remaining 12 digits are standard UPC-A
                if (cleaned.StartsWith("0"))
                {
                    var upcA = cleaned.Substring(1);
                    Add(upcA);
                    if (upcA.StartsWith("0"))
                    {
                        Add(upcA.TrimStart('0'));
                    }
                }
                Add("0" + cleaned); // GTIN-14 representation
            }
            // 4. GTIN-14 (14 digits)
            else if (len == 14)
            {
                if (cleaned.StartsWith("0"))
                {
                    var ean13 = cleaned.Substring(1);
                    Add(ean13);
                    if (ean13.StartsWith("0"))
                    {
                        Add(ean13.Substring(1)); // 12-digit UPC-A
                    }
                }
            }
            // 5. UPC-E (6, 7, or 8 digits)
            else if (len == 6 || len == 7 || len == 8)
            {
                var expandedUpcA = ExpandUpcE(cleaned);
                if (!string.IsNullOrEmpty(expandedUpcA))
                {
                    Add(expandedUpcA);
                    Add("0" + expandedUpcA); // 13-digit EAN-13 equivalent
                }
            }

            // 6. Generic leading-zero normalization (if starts with 0 and length > 8)
            if (cleaned.StartsWith("0") && len > 8)
            {
                var unpadded = cleaned.TrimStart('0');
                if (unpadded.Length >= 6)
                {
                    Add(unpadded);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Expands a 6, 7, or 8-digit UPC-E code to its standard 12-digit UPC-A equivalent
    /// following the official GS1 expansion specifications.
    /// </summary>
    public static string? ExpandUpcE(string upce)
    {
        if (string.IsNullOrWhiteSpace(upce)) return null;

        var digits = new string(upce.Where(char.IsDigit).ToArray());
        if (digits.Length < 6 || digits.Length > 8) return null;

        string numSys = "0";
        string data;
        char? check = null;

        if (digits.Length == 8)
        {
            numSys = digits[0].ToString();
            data = digits.Substring(1, 6);
            check = digits[7];
        }
        else if (digits.Length == 7)
        {
            numSys = digits[0].ToString();
            data = digits.Substring(1, 6);
        }
        else // 6 digits
        {
            data = digits;
        }

        // Must have valid number system (0 or 1)
        if (numSys != "0" && numSys != "1")
            numSys = "0";

        char d1 = data[0], d2 = data[1], d3 = data[2], d4 = data[3], d5 = data[4], d6 = data[5];
        string mfr;
        string prod;

        switch (d6)
        {
            case '0':
            case '1':
            case '2':
                mfr = $"{d1}{d2}{d6}00";
                prod = $"00{d3}{d4}{d5}";
                break;
            case '3':
                mfr = $"{d1}{d2}{d3}00";
                prod = $"000{d4}{d5}";
                break;
            case '4':
                mfr = $"{d1}{d2}{d3}{d4}0";
                prod = $"0000{d5}";
                break;
            default: // 5, 6, 7, 8, 9
                mfr = $"{d1}{d2}{d3}{d4}{d5}";
                prod = $"0000{d6}";
                break;
        }

        var elevenDigits = $"{numSys}{mfr}{prod}";
        char computedCheck = CalculateCheckDigit(elevenDigits);

        return $"{elevenDigits}{computedCheck}";
    }

    /// <summary>
    /// Calculates the standard GS1 Modulo-10 check digit for an uncheck-digited barcode string.
    /// </summary>
    public static char CalculateCheckDigit(string digitsWithoutCheck)
    {
        int sum = 0;
        int weight = 3;

        for (int i = digitsWithoutCheck.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(digitsWithoutCheck[i]))
            {
                sum += (digitsWithoutCheck[i] - '0') * weight;
                weight = weight == 3 ? 1 : 3;
            }
        }

        int remainder = sum % 10;
        int check = (10 - remainder) % 10;
        return (char)('0' + check);
    }

    /// <summary>
    /// Validates the GS1 Modulo-10 check digit for standard numeric barcodes (EAN-13, UPC-A, EAN-8, GTIN-14).
    /// Returns true if valid or if not a fixed-length numeric barcode.
    /// </summary>
    public static bool ValidateCheckDigit(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return false;
        var clean = barcode.Trim();
        if (!clean.All(char.IsDigit)) return true; // Alpha/Code 128 / QR codes don't use GS1 Modulo-10

        int len = clean.Length;
        if (len != 8 && len != 12 && len != 13 && len != 14) return true;

        var withoutCheck = clean.Substring(0, len - 1);
        char expected = CalculateCheckDigit(withoutCheck);
        return clean[len - 1] == expected;
    }
}
