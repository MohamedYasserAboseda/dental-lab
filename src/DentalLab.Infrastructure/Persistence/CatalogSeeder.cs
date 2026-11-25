using DentalLab.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalLab.Infrastructure.Persistence;

/// <summary>Seeds the initial editable lists. Prices start at 0: the lab sets its own prices.</summary>
public static class CatalogSeeder
{
    public static async Task SeedAsync(DentalLabDbContext db, CancellationToken ct = default)
    {
        if (!await db.RestorationTypes.AnyAsync(ct))
        {
            (string en, string ar)[] types =
            [
                ("Zirconia", "زيركون"), ("E.max", "إي ماكس"), ("PFM", "بورسلين على معدن"), ("Porcelain", "بورسلين"),
                ("Implant Crown", "تاج زرعة"), ("Full Arch", "فك كامل"), ("Dentures", "أطقم أسنان"),
                ("Temporary Crown", "تاج مؤقت"), ("Veneers", "فينير"), ("Wax-up", "واكس أب"), ("Other", "أخرى")
            ];
            for (var i = 0; i < types.Length; i++)
                db.RestorationTypes.Add(RestorationType.Create(types[i].en, types[i].ar, 0m, true, i));
        }

        if (!await db.Materials.AnyAsync(ct))
        {
            (string en, string ar)[] materials =
            [
                ("Zirconia", "زيركون"), ("Lithium Disilicate", "ليثيوم ديسيليكات"), ("Metal Ceramic", "معدن سيراميك"),
                ("Acrylic", "أكريليك"), ("PMMA", "PMMA"), ("Other", "أخرى")
            ];
            for (var i = 0; i < materials.Length; i++)
                db.Materials.Add(Material.Create(materials[i].en, materials[i].ar, true, i));
        }

        await db.SaveChangesAsync(ct);
    }
}
