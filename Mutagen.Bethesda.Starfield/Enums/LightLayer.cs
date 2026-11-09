namespace Mutagen.Bethesda.Starfield;

/// <summary>
/// A model's light layer (FLLD), per xEdit. Default Light Layer is set on almost every model in Starfield.esm.
/// </summary>
[Flags]
public enum LightLayer : uint
{
    DefaultLightLayer = 0x01,
    HelmetLight = 0x02,
    ShipInterior = 0x04,
    ShipExterior = 0x08,
}
