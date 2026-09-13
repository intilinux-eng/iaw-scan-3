using System;
using System.Collections.Generic;
using System.Linq;

namespace IES_2.Avalonia.Models
{
    /// <summary>
    /// Groups the newline-separated vehicle list returned by each ECU class's
    /// GetCars() by brand, inferred from the vehicle name text itself (the
    /// existing data already spells out "Lancia"/"Alfa" when relevant, and
    /// defaults to Fiat otherwise since Fiat produced most of these ECUs).
    /// </summary>
    public static class VehicleGrouping
    {
        public static IReadOnlyList<VehicleGroup> Group(string carsText)
        {
            var fiat = new List<string>();
            var lancia = new List<string>();
            var alfa = new List<string>();

            foreach (var line in carsText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = line.Trim();
                if (name.Length == 0) continue;

                if (name.Contains("lancia", StringComparison.OrdinalIgnoreCase))
                    lancia.Add(name);
                else if (name.Contains("alfa", StringComparison.OrdinalIgnoreCase))
                    alfa.Add(name);
                else
                    fiat.Add(name);
            }

            var groups = new List<VehicleGroup>();
            if (fiat.Count > 0) groups.Add(new VehicleGroup("Fiat", fiat));
            if (lancia.Count > 0) groups.Add(new VehicleGroup("Lancia", lancia));
            if (alfa.Count > 0) groups.Add(new VehicleGroup("Alfa Romeo", alfa));
            return groups;
        }
    }
}
