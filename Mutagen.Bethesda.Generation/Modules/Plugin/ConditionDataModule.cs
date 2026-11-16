using Loqui.Generation;
using Mutagen.Bethesda.Generation.Modules.Binary;
using Noggog;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;
using ObjectType = Mutagen.Bethesda.Plugins.Meta.ObjectType;

namespace Mutagen.Bethesda.Generation.Modules.Plugin;

public class ConditionDataModule : GenerationModule
{
    public override Task PreLoad(ObjectGeneration obj)
    {
        var data = obj.GetObjectData();
        data.GenerateConditionData = obj.Node.GetAttribute("generateConditionData", defaultVal: true);
        return base.PreLoad(obj);
    }

    /// <summary>
    /// A condition's two parameters, as the game reads them from the CTDA (C60, after C58/W19): the CTDA has two
    /// parameter slots (two Int32s, the fields the data's generated reader reads; a field another step fills, binary=
    /// "NoGeneration" like UnknownConditionData's Function, isn't one) and two string parameters (the class's String
    /// fields: Starfield's CIS1/CIS2 subrecords, by their record type, else in order). Parameter N is the N-th string when
    /// the class uses it (not named Unused*), the slot being unused then; else the N-th slot. Taking simply the first two
    /// fields mapped the string-parameter classes and UnknownConditionData wrong.
    /// </summary>
    public static IReadOnlyList<(TypeGeneration? Field, bool IsString)> Parameters(ObjectGeneration obj)
    {
        var strings = obj.Fields.Where(f => f is Loqui.Generation.StringType).ToList();
        var slots = obj.Fields
            .Where(f => f is not Loqui.Generation.StringType
                        && f.GetFieldData() is { RecordType: null, Binary: not BinaryGenerationType.NoGeneration })
            .ToList();
        TypeGeneration? StringFor(int i) =>
            strings.FirstOrDefault(f => f.GetFieldData().RecordType?.Type == $"CIS{i}")
            ?? (strings.All(f => f.GetFieldData().RecordType == null) && strings.Count >= i ? strings[i - 1] : null);
        var ret = new List<(TypeGeneration? Field, bool IsString)>();
        for (int i = 1; i <= 2; i++)
        {
            if (StringFor(i) is { } str && !str.Name.Contains("Unused"))
            {
                ret.Add((str, true));
            }
            else
            {
                ret.Add((slots.Count >= i ? slots[i - 1] : null, false));
            }
        }
        return ret;
    }

    public override async Task GenerateInClass(ObjectGeneration obj, StructuredStringBuilder sb)
    {
        await base.GenerateInClass(obj, sb);

        if (obj.Abstract) return;
        if (obj.GetObjectType() != ObjectType.Subrecord) return;
        if (obj.BaseClass is null) return;
        if (obj.BaseClassName != "ConditionData") return;
        if (obj.GetObjectData().GenerateConditionData == false) return;

        var i = 1;
        foreach (var (field, isString) in Parameters(obj))
        {
            if (field == null) break;
            var isUnusedParameter = !isString && field.Name.Contains("Unused");

            // Parameter property
            sb.AppendLine($"object? IConditionParameters.Parameter{i}");
            using (sb.CurlyBrace())
            {
                if (isUnusedParameter)
                {
                    sb.AppendLine("get => null;");
                    sb.AppendLine("set");
                    using (sb.CurlyBrace())
                    {
                        sb.AppendLine();
                    }
                }
                else if (isString)
                {
                    sb.AppendLine($"get => {field.Name};");
                    sb.AppendLine($"set => {field.Name} = value is null or string ? (string?)value : throw new ArgumentException(\"Parameter{i} is a string\");");
                }
                else
                {
                    var name = field.Name;
                    var setterTypeName = field.TypeName(false);
                    sb.AppendLine($"get => {name};");
                    sb.AppendLine($"set => {name} = (value is {setterTypeName} v ? v : throw new ArgumentException());");
                }
            }

            sb.AppendLine($"object? IConditionParametersGetter.Parameter{i}");
            using (sb.CurlyBrace())
            {
                if (isUnusedParameter)
                {
                    sb.AppendLine("get => null;");
                }
                else
                {
                    sb.AppendLine($"get => {field.Name};");
                }
            }

            // ParameterType property
            sb.AppendLine($"Type? IConditionParametersGetter.Parameter{i}Type");
            using (sb.CurlyBrace())
            {
                if (isUnusedParameter)
                {
                    sb.AppendLine("get => null;");
                }
                else if (isString)
                {
                    sb.AppendLine("get => typeof(string);");
                }
                else
                {
                    var name = field.TypeName(true);
                    sb.AppendLine($"get => typeof({name});");
                }
            }

            i++;
        }
    }
}
