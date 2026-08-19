namespace SnookerPoint.Domain.Enums;

/// <summary>Whether a sale is a walk-in store sale or the checkout of a table session.</summary>
public enum SaleType
{
    Walkin = 0,
    Table = 1,
}

/// <summary>The lifecycle status of a sale.</summary>
public enum SaleStatus
{
    /// <summary>An open, editable draft; deducts no stock and is not revenue.</summary>
    Draft = 0,

    /// <summary>A parked draft the cashier can reopen later.</summary>
    Held = 1,

    /// <summary>Paid in full; immutable financial record.</summary>
    Completed = 2,

    /// <summary>Abandoned before payment; deducts no stock and is not revenue.</summary>
    Cancelled = 3,
}

/// <summary>How a payment method behaves with respect to the physical cash drawer.</summary>
public enum PaymentMethodKind
{
    /// <summary>Physical cash: affects the drawer and supports change.</summary>
    Cash = 0,

    /// <summary>Electronic (EasyPaisa/JazzCash/Bank Transfer): counts as sales but not drawer cash.</summary>
    Electronic = 1,
}

/// <summary>Whether a sale-level discount is a fixed rupee amount or a percentage.</summary>
public enum DiscountKind
{
    None = 0,
    FixedAmount = 1,
    Percentage = 2,
}
