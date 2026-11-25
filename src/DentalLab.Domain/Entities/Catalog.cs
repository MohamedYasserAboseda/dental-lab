namespace DentalLab.Domain.Entities;

public class RestorationType
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public decimal DefaultUnitPrice { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    private RestorationType() { }

    public static RestorationType Create(string name, string? nameAr, decimal price, bool isActive, int sortOrder = 0)
    {
        var r = new RestorationType { SortOrder = sortOrder };
        r.Update(name, nameAr, price, isActive);
        return r;
    }

    public void Update(string name, string? nameAr, decimal price, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");
        Name = name.Trim();
        NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim();
        DefaultUnitPrice = Money.Round(price);
        IsActive = isActive;
    }
}

public class Material
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    private Material() { }

    public static Material Create(string name, string? nameAr, bool isActive, int sortOrder = 0)
    {
        var m = new Material { SortOrder = sortOrder };
        m.Update(name, nameAr, isActive);
        return m;
    }

    public void Update(string name, string? nameAr, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name.Trim();
        NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim();
        IsActive = isActive;
    }
}

/// <summary>Price agreed with one doctor for one restoration type. Overrides the default price.</summary>
public class DoctorPrice
{
    public int Id { get; private set; }
    public int DoctorId { get; private set; }
    public int RestorationTypeId { get; private set; }
    public decimal UnitPrice { get; private set; }

    private DoctorPrice() { }

    public static DoctorPrice Create(int doctorId, int restorationTypeId, decimal price)
    {
        var d = new DoctorPrice { DoctorId = doctorId, RestorationTypeId = restorationTypeId };
        d.SetPrice(price);
        return d;
    }

    public void SetPrice(decimal price)
    {
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));
        UnitPrice = Money.Round(price);
    }
}
