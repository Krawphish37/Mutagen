using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

partial class AVirtualMachineAdapterBinaryCreateTranslation
{
    /// <summary>The scripts (C60: noted as the items of the list the caller began)</summary>
    public static IEnumerable<ScriptEntry> ReadEntries(MutagenFrame frame, ushort objectFormat)
    {
        var countAt = ValuePositionRecorder.Here(frame);
        ushort count = frame.ReadUInt16();
        if (countAt >= 0) ValuePositionRecorder.Count(frame, countAt);
        for (int i = 0; i < count; i++)
        {
            if (ValuePositionRecorder.On) ValuePositionRecorder.Item(frame, i);
            var entry = ReadEntry(frame, objectFormat);
            if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
            yield return entry;
        }
    }

    public static ScriptEntry ReadEntry(MutagenFrame frame, ushort objectFormat)
    {
        var at = ValuePositionRecorder.Here(frame);
        var scriptName = StringBinaryTranslation.Instance.Parse(frame, stringBinaryType: StringBinaryType.PrependLengthUShort, parseWhole: true);
        ValuePositionRecorder.Value(frame, nameof(ScriptEntry.Name), at);
        var entry = new ScriptEntry()
        {
            Name = scriptName,
        };
        if (scriptName.IsNullOrEmpty()) return entry;
        at = ValuePositionRecorder.Here(frame);
        entry.Flags = (ScriptEntry.Flag)frame.ReadUInt8();
        ValuePositionRecorder.Value(frame, nameof(ScriptEntry.Flags), at);
        FillProperties(frame, objectFormat, entry);
        return entry;
    }

    public static partial void FillBinaryScriptsCustom(MutagenFrame frame, IAVirtualMachineAdapter item)
    {
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.Scripts));
        item.Scripts.AddRange(ReadEntries(frame, item.ObjectFormat));
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
    }

    public static ScriptProperty ParseProperty(MutagenFrame frame, ushort objectFormat)
    {
        var nameAt = ValuePositionRecorder.Here(frame);
        var name = StringBinaryTranslation.Instance.Parse(frame, stringBinaryType: StringBinaryType.PrependLengthUShort, parseWhole: true);
        ValuePositionRecorder.Value(frame, nameof(ScriptProperty.Name), nameAt);
        var type = (ScriptProperty.Type)frame.ReadUInt8();
        var flagsAt = ValuePositionRecorder.Here(frame);
        var flags = (ScriptProperty.Flag)frame.ReadUInt8();
        ValuePositionRecorder.Value(frame, nameof(ScriptProperty.Flags), flagsAt);
        ScriptProperty prop = type switch
        {
            ScriptProperty.Type.None => new ScriptProperty(),
            ScriptProperty.Type.Object => new ScriptObjectProperty(),
            ScriptProperty.Type.String => new ScriptStringProperty(),
            ScriptProperty.Type.Int => new ScriptIntProperty(),
            ScriptProperty.Type.Float => new ScriptFloatProperty(),
            ScriptProperty.Type.Bool => new ScriptBoolProperty(),
            ScriptProperty.Type.Variable => new ScriptVariableProperty(),
            ScriptProperty.Type.Struct => new ScriptStructProperty(),
            ScriptProperty.Type.ArrayOfObject => new ScriptObjectListProperty(),
            ScriptProperty.Type.ArrayOfString => new ScriptStringListProperty(),
            ScriptProperty.Type.ArrayOfInt => new ScriptIntListProperty(),
            ScriptProperty.Type.ArrayOfFloat => new ScriptFloatListProperty(),
            ScriptProperty.Type.ArrayOfBool => new ScriptBoolListProperty(),
            ScriptProperty.Type.ArrayOfVariable => new ScriptVariableListProperty(),
            ScriptProperty.Type.ArrayOfStruct => new ScriptStructListProperty(),
            _ => throw new NotImplementedException(),
        };
        prop.Name = name;
        prop.Flags = flags;
        switch (prop)
        {
            case ScriptObjectProperty obj:
                FillObject(frame, obj, objectFormat);
                break;
            case ScriptObjectListProperty objList:
            {
                if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(objList.Objects));
                var countAt = ValuePositionRecorder.Here(frame);
                var objListCount = frame.ReadUInt32();
                if (countAt >= 0) ValuePositionRecorder.Count(frame, countAt);
                for (int j = 0; j < objListCount; j++)
                {
                    var subObj = new ScriptObjectProperty();
                    if (ValuePositionRecorder.On) ValuePositionRecorder.Item(frame, j);
                    FillObject(frame, subObj, objectFormat);
                    if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
                    objList.Objects.Add(subObj);
                }
                if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
                break;
            }
            case ScriptVariableProperty varProp:
            case ScriptVariableListProperty varPropList:
                throw new NotImplementedException();
            case ScriptStructProperty subStructs:
                // One struct: the members list's only item
                if (ValuePositionRecorder.On)
                {
                    ValuePositionRecorder.List(frame, nameof(subStructs.Members));
                    ValuePositionRecorder.Item(frame, 0);
                }
                subStructs.Members.Add(GetScriptEntryStruct(frame, objectFormat));
                if (ValuePositionRecorder.On)
                {
                    ValuePositionRecorder.End(frame);
                    ValuePositionRecorder.End(frame);
                }
                break;
            case ScriptStructListProperty structList:
            {
                if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(structList.Structs));
                var countAt = ValuePositionRecorder.Here(frame);
                var structListCount = frame.ReadUInt32();
                if (countAt >= 0) ValuePositionRecorder.Count(frame, countAt);
                for (int j = 0; j < structListCount; j++)
                {
                    var subStructs = new ScriptEntryStructs();
                    if (ValuePositionRecorder.On)
                    {
                        ValuePositionRecorder.Item(frame, j);
                        ValuePositionRecorder.List(frame, nameof(subStructs.Members));
                    }
                    var membersAt = ValuePositionRecorder.Here(frame);
                    var membersCount = frame.ReadInt32();
                    if (membersAt >= 0) ValuePositionRecorder.Count(frame, membersAt);
                    subStructs.Members.SetTo(
                        Mutagen.Bethesda.Plugins.Binary.Translations.ListBinaryTranslation<ScriptProperty>.Instance.Parse(
                            amount: membersCount,
                            reader: frame,
                            transl: (MutagenFrame f, [MaybeNullWhen(false)] out ScriptProperty item, TypedParseParams p) =>
                            {
                                item = ParseProperty(f, objectFormat);
                                return true;
                            }));
                    if (ValuePositionRecorder.On)
                    {
                        ValuePositionRecorder.End(frame);
                        ValuePositionRecorder.End(frame);
                    }
                    structList.Structs.Add(subStructs);
                }
                if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
                break;
            }
            default:
                prop.CopyInFromBinary(frame);
                break;
        }
        return prop;
    }

    static void FillProperties(MutagenFrame frame, ushort objectFormat, IScriptEntry item, bool isStruct = false)
    {
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.Properties));
        var countAt = ValuePositionRecorder.Here(frame);
        var count = isStruct ? frame.ReadUInt32() : frame.ReadUInt16();
        if (countAt >= 0) ValuePositionRecorder.Count(frame, countAt);
        for (int i = 0; i < count; i++)
        {
            if (ValuePositionRecorder.On) ValuePositionRecorder.Item(frame, i);
            item.Properties.Add(ParseProperty(frame, objectFormat));
            if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
        }
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
    }

    static IEnumerable<ScriptProperty> ParseProperties(MutagenFrame frame, ushort objectFormat, bool isStruct = false)
    {
        var count = isStruct ? frame.ReadUInt32() : frame.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            yield return ParseProperty(frame, objectFormat);
        }
    }

    public static void FillObject(MutagenFrame frame, IScriptObjectProperty obj, ushort objectFormat)
    {
        var at = ValuePositionRecorder.Here(frame);
        switch (objectFormat)
        {
            case 2:
                obj.Unused = frame.ReadUInt16();
                if (at >= 0) ValuePositionRecorder.At(nameof(obj.Unused), at, 2);
                obj.Alias = frame.ReadInt16();
                if (at >= 0) ValuePositionRecorder.At(nameof(obj.Alias), at + 2, 2);
                obj.Object.FormKey = FormLinkBinaryTranslation.Instance.Parse(
                    reader: frame,
                    defaultVal: FormKey.Null);
                if (at >= 0) ValuePositionRecorder.At(nameof(obj.Object), at + 4, 4);
                break;
            case 1:
                obj.Object.FormKey = FormLinkBinaryTranslation.Instance.Parse(
                    reader: frame,
                    defaultVal: FormKey.Null);
                if (at >= 0) ValuePositionRecorder.At(nameof(obj.Object), at, 4);
                obj.Alias = frame.ReadInt16();
                if (at >= 0) ValuePositionRecorder.At(nameof(obj.Alias), at + 4, 2);
                obj.Unused = frame.ReadUInt16();
                if (at >= 0) ValuePositionRecorder.At(nameof(obj.Unused), at + 6, 2);
                break;
            default:
                throw new NotImplementedException();
        }
    }

    public static ScriptEntry GetScriptEntryStruct(MutagenFrame frame, ushort objectFormat)
    {
        var member = new ScriptEntry();
        FillProperties(frame, objectFormat, member, true);
        return member;
    }
}

partial class AVirtualMachineAdapterBinaryWriteTranslation
{
    public static void WriteProperty(MutagenWriter writer, IScriptPropertyGetter property, ushort objFormat)
    {
        writer.Write(property.Name, StringBinaryType.PrependLengthUShort, encoding: writer.MetaData.Encodings.NonTranslated);
        var type = property switch
        {
            ScriptObjectProperty _ => ScriptProperty.Type.Object,
            ScriptStringProperty _ => ScriptProperty.Type.String,
            ScriptIntProperty _ => ScriptProperty.Type.Int,
            ScriptFloatProperty _ => ScriptProperty.Type.Float,
            ScriptBoolProperty _ => ScriptProperty.Type.Bool,
            ScriptVariableProperty _ => ScriptProperty.Type.Variable,
            ScriptStructProperty _ => ScriptProperty.Type.Struct,
            ScriptObjectListProperty _ => ScriptProperty.Type.ArrayOfObject,
            ScriptStringListProperty _ => ScriptProperty.Type.ArrayOfString,
            ScriptIntListProperty _ => ScriptProperty.Type.ArrayOfInt,
            ScriptFloatListProperty _ => ScriptProperty.Type.ArrayOfFloat,
            ScriptBoolListProperty _ => ScriptProperty.Type.ArrayOfBool,
            ScriptVariableListProperty _ => ScriptProperty.Type.ArrayOfVariable,
            ScriptStructListProperty _ => ScriptProperty.Type.ArrayOfStruct,
            ScriptProperty _ => ScriptProperty.Type.None,
            _ => throw new NotImplementedException(),
        };
        writer.Write((byte)type);
        writer.Write((byte)property.Flags);
        switch (property)
        {
            case ScriptObjectProperty obj:
                WriteObject(writer, obj, objFormat);
                break;
            case ScriptObjectListProperty objList:
                var objsList = objList.Objects;
                writer.Write(objsList.Count);
                foreach (var subObj in objsList)
                {
                    WriteObject(writer, subObj, objFormat);
                }
                break;
            case ScriptVariableProperty varProp:
            case ScriptVariableListProperty varPropList:
                throw new NotImplementedException();
            case ScriptStructProperty subStructs:
                WriteScripts(writer, objFormat, (IReadOnlyList<IScriptEntryGetter>)subStructs.Members, true);
                break;
            case ScriptStructListProperty structList:
                var structsList = structList.Structs;
                writer.Write(checked((uint)structsList.Count));
                for (int i = 0; i < structsList.Count; ++i)
                {
                    var subStructs = structsList[i];
                    WriteProperties(writer, objFormat, (IReadOnlyList<IScriptPropertyGetter>)subStructs.Members);
                }

                break;
            default:
                property.WriteToBinary(writer);
                break;
        }
    }

    public static void WriteEntry(
        MutagenWriter writer,
        IScriptEntryGetter entry,
        ushort objFormat,
        bool isStruct = false)
    {
        var properties = entry.Properties;
        
        if (!isStruct)
        {
            var name = entry.Name;
            StringBinaryTranslation.Instance.Write(writer, name, StringBinaryType.PrependLengthUShort);
            if (!name.IsNullOrWhitespace())
            {
                writer.Write((byte)entry.Flags);
            }
            else
            {
                if (properties.Count > 0)
                {
                    throw new ArgumentException("Entry had no name, but had properties");
                }
                return;
            }
        }

        if (isStruct)
            writer.Write(checked((uint)properties.Count));
        else
            writer.Write(checked((ushort)properties.Count));

        foreach (var property in properties)
        {
            WriteProperty(writer, property, objFormat);
        }
    }

    public static void WriteEntry(
        MutagenWriter writer,
        IScriptEntryStructsGetter entry,
        ushort objFormat)
    {
        var properties = entry.Members;
        writer.Write(checked((uint)properties.Count));

        foreach (var property in properties)
        {
            WriteProperty(writer, property, objFormat);
        }
    }

    public static void WriteScripts(
        MutagenWriter writer,
        ushort objFormat,
        IReadOnlyList<IScriptEntryGetter> scripts,
        bool isStruct = false)
    {
        if (!isStruct)
            writer.Write(checked((ushort)scripts.Count));

        foreach (var entry in scripts)
        {
            WriteEntry(writer, entry, objFormat, isStruct: isStruct);
        }
    }

    public static void WriteProperties(
        MutagenWriter writer,
        ushort objFormat,
        IReadOnlyList<IScriptPropertyGetter> properties)
    {
        writer.Write(checked((uint)properties.Count));
        foreach (var property in properties)
        {
            WriteProperty(writer, property, objFormat);
        }
    }

    public static void WriteObject(MutagenWriter writer, IScriptObjectPropertyGetter obj, ushort objFormat)
    {
        switch (objFormat)
        {
            case 2:
                writer.Write(obj.Unused);
                writer.Write(obj.Alias);
                FormKeyBinaryTranslation.Instance.Write(writer, obj.Object);break;
            case 1:
                FormKeyBinaryTranslation.Instance.Write(writer, obj.Object);
                writer.Write(obj.Alias);
                writer.Write(obj.Unused);
                break;
            default:
                throw new NotImplementedException();
        }
    }

    public static partial void WriteBinaryScriptsCustom(MutagenWriter writer, IAVirtualMachineAdapterGetter item)
    {
        WriteScripts(writer, item.ObjectFormat, item.Scripts);
    }
}
    
partial class AVirtualMachineAdapterBinaryOverlay
{
    public IReadOnlyList<IScriptEntryGetter> Scripts { get; private set; } = null!;

    partial void CustomCtor()
    {
        var frame = new MutagenFrame(new MutagenMemoryReadStream(_structData, _package.MetaData))
        {
            Position = 0x04
        };
        var ret = new ExtendedList<IScriptEntryGetter>();
        ret.AddRange(VirtualMachineAdapterBinaryCreateTranslation.ReadEntries(frame, this.ObjectFormat));
        this.Scripts = ret;
        this.ScriptsEndingPos = checked((int)frame.Position);
    }
}