using System.Text.RegularExpressions;

namespace Qadr.Core;

/// <summary>
/// Which recorded routine steps must be confirmed before they are replayed.
/// The previous check was a short word list (send, delete, pay, purchase,
/// buy, submit, confirm order), so "OK", "Remove", "Transfer", "Post" or an
/// icon-only button with no name replayed without asking. Replay runs while
/// head control is off, so a wrong click cannot be caught by the user.
/// </summary>
public static class RoutineSafety
{
    private static readonly Regex Risky = new(
        @"\b(send|delete|remove|erase|discard|empty|reset|uninstall|format|pay|payment|purchase|buy|order|checkout|"
        + @"submit|confirm|ok|yes|accept|agree|allow|transfer|post|publish|share|upload|sign ?out|log ?out|install)\b"
        + "|إرسال|ارسال|حذف|إزالة|ازالة|دفع|شراء|طلب|تأكيد|تاكيد|موافق|نعم|نشر|مشاركة|تحويل|رفع|تثبيت",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool NeedsConfirmation(string? name) =>
        string.IsNullOrWhiteSpace(name) || Risky.IsMatch(name);
}
