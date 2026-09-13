using System.Collections.Generic;
using System.Linq;

namespace IES_2.Avalonia.Models
{
    /// <summary>
    /// One selectable ECU family in the list: its display name and its
    /// supported vehicles grouped by brand.
    /// </summary>
    public class EcuOption
    {
        /// <summary>Internal identifier - the ECU class's `name` constant.</summary>
        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<VehicleGroup> VehicleGroups { get; }

        /// <summary>Short generic label shown in the family badge (no logos/photos) - e.g. "16F", "04K".</summary>
        public string Badge { get; }

        public int VehicleCount { get; }

        public EcuOption(string id, string displayName, string carsText)
        {
            Id = id;
            DisplayName = displayName;
            VehicleGroups = VehicleGrouping.Group(carsText);
            VehicleCount = VehicleGroups.Sum(g => g.Vehicles.Count);
            Badge = MakeBadge(id);
        }

        private static string MakeBadge(string id)
        {
            if (id.StartsWith("IAW-")) return id.Substring(4);
            if (id == "FIAT CODE") return "COD";
            return id.Length <= 4 ? id.ToUpperInvariant() : id.Substring(0, 4).ToUpperInvariant();
        }
    }
}
