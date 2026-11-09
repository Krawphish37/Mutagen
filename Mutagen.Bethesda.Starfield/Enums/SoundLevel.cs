namespace Mutagen.Bethesda.Starfield;

// Starfield's order, which differs from Skyrim and Fallout 4 (as in xEdit's Starfield definitions).
// Starfield.esm confirms it: doors named "..._NoDetectionEvent" store 0, the door "..._Loud" stores 4,
// knives store 1, and ship, asteroid and nuke explosions store 5.
public enum SoundLevel
{
     None = 0,
     Silent = 1,
     Quiet = 2,
     Normal = 3,
     Loud = 4,
     VeryLoud = 5,
}