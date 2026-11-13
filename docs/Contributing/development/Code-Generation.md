# Code Generation
Most of Mutagen's verbose record classes, interfaces, and binary translation code is created by a code generation library called Loqui, rather than handwritten.  

## Xml Definitions
Each record is defined by an xml file that sits next to its handwritten partial class, such as `Mutagen.Bethesda.Skyrim/Records/Major Records/DialogTopic.xml`.  The xml lists the record's fields, their types, and their record types, and the generator produces the matching `_Generated.cs` file.

To change a record's structure, edit its xml and regenerate.  The `_Generated.cs` files should never be edited by hand, as they are overwritten on the next run.

### Properties the generator adds that aren't xml fields
A few properties of the generated classes have no `<Field>` of their own in the xml. The generator derives them from
the xml's structure, or they come from a hand-written base class. Anything that reads the xml to describe a record
(a schema, a serializer's field list) should derive them the same way rather than look for a field:

| Property | Where it comes from | Type and values |
|---|---|---|
| `MajorFlags` | `majorFlag="true"` on a record's `<Object>` | `<Record>.MajorFlag`, a hand-written `[Flags]` enum in the record's partial class; a view of the record header's flags (`MajorRecordFlagsRaw`) |
| `IsCompressed` (also `IsDeleted`) | the hand-written base class `MajorRecord` (Mutagen.Bethesda.Core), on every major record | `bool`; a view of the record header flag 0x40000 (compressed) / 0x20 (deleted) |
| `Versioning` | one or more `<Break />` elements directly in an object's `<Fields>` | `<Object>.VersioningBreaks`, `[Flags]`: `Break0`, `Break1`, ... in xml order. A flag is set when the data ended at that break, and the writer stops there |
| `<Sig>DataTypeState` | a `<Data recordType="SIG">` element that holds `<Break />` or `<Range>` elements, or is optional | `<Object>.<Sig>DataType`, `[Flags]`: `Break0`.., then `Range0`.., then `Absent` (an optional block that wasn't there), each a power of two in that order |

These hold no data of their own beyond what the file's bytes say (the header flags, or how far a subrecord's data
went), but they are written by the serializer and compared by equality like any field.

## Loqui
The generation is built on top of [Loqui](https://github.com/Noggog/Loqui), a code generation library that is only really used by Mutagen.  Loqui provides the xml schema and the base generation of the classes, interfaces, masks, equality, and copying.  `Mutagen.Bethesda.Generation` layers the Bethesda specific pieces on top, such as binary translation and FormLink handling.

## Running the Generator
`Mutagen.Bethesda.Generator.All` regenerates every game in one pass.  Run it after changing any xml definitions or generation code.

!!! note
    The generator locates the projects it writes to via paths relative to its working directory, so it must be run with its build output folder as the working directory.  IDEs do this by default.

## Debugging
When you want to modify a line in the generated code, the hard part is often finding which piece of code wrote it.  The generator has a built-in hook for this: you can list snippets of output text, and the debugger will break the moment a matching line is written.

### GenerationLines.txt
Create a file named `GenerationLines.txt` at the root of the repository, next to the solution files.  Each non-blank line is a snippet to watch for:

```
public partial class DialogTopicBinaryOverlay
SubtypeName =
```

Then whenever the generator writes a line that **contains** any of the snippets, it calls `Debugger.Break()` and pauses the run.  This allows you to then see the code involved, which gives you a starting point for modifying the code generator for the line you're interested in.

The `GenerationLines.txt` file is already in `.gitignore`, so it can be left in place between sessions with junk content.

Things To Know

- **Debug builds only.**  The hook is compiled under `#if DEBUG`, so a Release build will never break.
- **Edits are picked up live.**  The file is watched while the generator runs, so snippets can be added or removed mid-session without restarting.  An empty or missing file disables the check entirely.
- **Matches are substrings.**  Prefer a distinctive chunk of the line.  A short snippet like `Subtype` will match many lines and break constantly.
