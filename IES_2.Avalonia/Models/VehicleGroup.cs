using System.Collections.Generic;

namespace IES_2.Avalonia.Models
{
    /// <summary>
    /// A brand-level grouping of supported vehicle names for one ECU family,
    /// shown as an always-visible expandable group instead of a hover tooltip.
    /// </summary>
    public class VehicleGroup
    {
        public string Brand { get; }
        public IReadOnlyList<string> Vehicles { get; }

        public VehicleGroup(string brand, IReadOnlyList<string> vehicles)
        {
            Brand = brand;
            Vehicles = vehicles;
        }
    }
}
