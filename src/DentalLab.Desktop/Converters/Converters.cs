using System.Globalization;
using System.Windows.Data;
using DentalLab.Desktop.Services;
using DentalLab.Domain.Entities;

namespace DentalLab.Desktop.Converters;

public sealed class LedgerTypeToTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => LocalizationService.Instance["Ledger_" + value];
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class BoolToStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => LocalizationService.Instance[value is true ? "Filter_Active" : "Filter_Inactive"];
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Positive balance (doctor owes) = warning, negative (credit) = success, zero = normal text.</summary>
public sealed class BalanceToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var key = value is decimal d ? (d > 0 ? "WarningBrush" : d < 0 ? "SuccessBrush" : "TextBrush") : "TextBrush";
        return System.Windows.Application.Current.FindResource(key);
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>CaseStatus (or null) to localized text; null shows an em dash.</summary>
public sealed class CaseStatusToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object p, CultureInfo c)
        => value is CaseStatus s ? LocalizationService.Instance["Status_" + s] : "—";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class InvoiceStatusToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object p, CultureInfo c)
        => value is InvoiceStatus s ? LocalizationService.Instance["InvStatus_" + s] : "—";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class InvoiceStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object p, CultureInfo c)
    {
        var key = value switch
        {
            InvoiceStatus.Paid => "SuccessBrush",
            InvoiceStatus.PartiallyPaid => "GoldBrush",
            InvoiceStatus.Unpaid => "WarningBrush",
            _ => "TextMutedBrush"
        };
        return System.Windows.Application.Current.FindResource(key);
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class PaymentMethodToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object p, CultureInfo c)
        => value is PaymentMethod m ? LocalizationService.Instance["Method_" + m] : "—";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class CaseStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object p, CultureInfo c)
    {
        var key = value switch
        {
            CaseStatus.Cancelled => "DangerBrush",
            CaseStatus.Delivered or CaseStatus.Ready => "SuccessBrush",
            CaseStatus.Received => "TextMutedBrush",
            _ => "GoldBrush"
        };
        return System.Windows.Application.Current.FindResource(key);
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
