using Loqui.Generation;
using Loqui.Internal;
using Mutagen.Bethesda.Plugins;
using Noggog;
using Noggog.StructuredStrings;
using Noggog.StructuredStrings.CSharp;
using DictType = Mutagen.Bethesda.Generation.Fields.DictType;

namespace Mutagen.Bethesda.Generation.Modules.Plugin;

public class DuplicateModule : GenerationModule
{
    public override async Task GenerateInClass(ObjectGeneration obj, StructuredStringBuilder sb)
    {
        await base.GenerateInClass(obj, sb);
        if (!await obj.IsMajorRecord()) return;
    }

    public override async Task GenerateInInterface(ObjectGeneration obj, StructuredStringBuilder sb, bool internalInterface, bool getter)
    {
        await base.GenerateInInterface(obj, sb, internalInterface, getter);
    }

    public override async Task GenerateInCommonMixin(ObjectGeneration obj, StructuredStringBuilder sb)
    {
        await base.GenerateInCommonMixin(obj, sb);
        if (!await obj.IsMajorRecord()) return;
        using (var args = sb.Function(
                   $"public static {obj.ObjectName} Duplicate{obj.GetGenericTypes(MaskType.Normal, MaskType.NormalGetter)}"))
        {
            args.Wheres.AddRange(obj.GenericTypeMaskWheres(LoquiInterfaceType.IGetter, MaskType.Normal, MaskType.NormalGetter));
            args.Add($"this {obj.Interface(obj.GetGenericTypes(MaskType.NormalGetter), getter: true, internalInterface: true)} item");
            args.Add($"{nameof(FormKey)} formKey");
            args.Add($"{obj.Mask(MaskType.Translation)}? copyMask = null");
        }
        using (sb.CurlyBrace())
        {
            using (var args = sb.Call(
                       $"return {obj.CommonClassInstance("item", LoquiInterfaceType.IGetter, CommonGenerics.Functions, MaskType.NormalGetter)}.Duplicate{obj.GetGenericTypes(MaskType.Normal, MaskType.NormalGetter, MaskType.Translation)}"))
            {
                args.AddPassArg("item");
                args.AddPassArg("formKey");
                args.Add("copyMask: copyMask?.GetCrystal()");
            }
        }
        sb.AppendLine();
        
        using (var args = sb.Function(
                   $"public static {obj.ObjectName} Duplicate{obj.GetGenericTypes(MaskType.Normal, MaskType.NormalGetter)}"))
        {
            args.Wheres.AddRange(obj.GenericTypeMaskWheres(LoquiInterfaceType.IGetter, MaskType.Normal, MaskType.NormalGetter));
            args.Add($"this {obj.Interface(obj.GetGenericTypes(MaskType.NormalGetter), getter: true, internalInterface: true)} item");
            args.Add($"{nameof(FormKey)} formKey");
            args.Add($"{nameof(TranslationCrystal)}? copyMask");
        }
        using (sb.CurlyBrace())
        {
            using (var args = sb.Call(
                       $"return {obj.CommonClassInstance("item", LoquiInterfaceType.IGetter, CommonGenerics.Functions, MaskType.NormalGetter)}.Duplicate{obj.GetGenericTypes(MaskType.Normal, MaskType.NormalGetter, MaskType.Translation)}"))
            {
                args.AddPassArg("item");
                args.AddPassArg("formKey");
                args.AddPassArg("copyMask");
            }
        }
        sb.AppendLine();
    }

    public override async Task GenerateInCommon(ObjectGeneration obj, StructuredStringBuilder sb, MaskTypeSet maskTypes)
    {
        if (!maskTypes.Applicable(LoquiInterfaceType.IGetter, CommonGenerics.Class)) return;
        await base.GenerateInCommon(obj, sb, maskTypes);
        if (!await obj.IsMajorRecord()) return;
        using (sb.Region("Duplicate"))
        {
            var nestedRecordFields = obj.Abstract ? new List<TypeGeneration>() : await GetNestedMajorRecordFields(obj);
            if (nestedRecordFields.Count > 0)
            {
                // Nested major records keep their own FormKeys when copied, so a duplicate
                // would carry them along as overrides of the source's children.
                // By default, only the record itself is duplicated
                sb.AppendLine($"private static readonly {obj.Name}.TranslationMask DuplicateMask = new(defaultOn: true)");
                using (sb.CurlyBrace(appendSemiColon: true))
                {
                    foreach (var field in nestedRecordFields)
                    {
                        sb.AppendLine($"{field.Name} = false,");
                    }
                }
                sb.AppendLine($"private static readonly TranslationCrystal DuplicateCrystal = DuplicateMask.GetCrystal();");
                sb.AppendLine();
            }

            using (var args = sb.Function(
                       $"public{obj.Virtual()}{obj.Name} Duplicate{obj.GetGenericTypes(MaskType.Normal, MaskType.NormalGetter)}"))
            {
                args.Wheres.AddRange(obj.GenericTypeMaskWheres(LoquiInterfaceType.IGetter, MaskType.Normal, MaskType.NormalGetter));
                args.Add($"{obj.Interface(getter: true)} item");
                args.Add($"{nameof(FormKey)} formKey");
                args.Add($"TranslationCrystal? copyMask");
            }
            using (sb.CurlyBrace())
            {
                if (obj.Abstract)
                {
                    sb.AppendLine("throw new NotImplementedException();");
                }
                else
                {
                    var hasFormVersion = obj.GetObjectData().GameCategory?.HasFormVersion() ?? false;
                    var multipleGameReleases = obj.GetObjectData().HasMultipleReleases;
                    sb.AppendLine($"var newRec = new {obj.Name}(formKey{(hasFormVersion ? $", item.FormVersion" : (multipleGameReleases ? $", default({obj.GetObjectData().GameCategory}Release)" : string.Empty))});");
                    sb.AppendLine($"newRec.DeepCopyIn(item, default({nameof(ErrorMaskBuilder)}?), copyMask{(nestedRecordFields.Count > 0 ? " ?? DuplicateCrystal" : null)});");
                    sb.AppendLine("return newRec;");
                }
            }
            sb.AppendLine();

            foreach (var baseClass in obj.BaseClassTrail())
            {
                using (var args = sb.Function(
                           $"public override {baseClass.Name} Duplicate{baseClass.GetGenericTypes(MaskType.Normal, MaskType.NormalGetter)}"))
                {
                    args.Wheres.AddRange(baseClass.GenericTypeMaskWheres(LoquiInterfaceType.IGetter, MaskType.Normal, MaskType.NormalGetter));
                    args.Add($"{baseClass.Interface(getter: true)} item");
                    args.Add($"{nameof(FormKey)} formKey");
                    args.Add($"TranslationCrystal? copyMask");
                }
                using (sb.CurlyBrace())
                {
                    using (var args = sb.Call(
                               $"return this.Duplicate"))
                    {
                        args.Add($"item: ({obj.Interface(getter: true)})item");
                        args.AddPassArg("formKey");
                        args.AddPassArg("copyMask");
                    }
                }
                sb.AppendLine();
            }
        }
    }

    /// <summary>
    /// Fields of a major record that hold other major records (Cell refs, Quest scenes, DialogTopic responses, etc)
    /// </summary>
    private static async Task<List<TypeGeneration>> GetNestedMajorRecordFields(ObjectGeneration obj)
    {
        var ret = new List<TypeGeneration>();
        foreach (var field in obj.IterateFields(includeBaseClass: true))
        {
            if (field.GetFieldData().Circular) continue;
            LoquiType loqui = field switch
            {
                LoquiType l => l,
                ContainerType cont => cont.SubTypeGeneration as LoquiType,
                DictType dict => dict.ValueTypeGen as LoquiType,
                _ => null
            };
            if (loqui == null) continue;
            if (await MajorRecordModule.HasMajorRecords(loqui, includeBaseClass: true) != Case.Yes) continue;
            ret.Add(field);
        }
        return ret;
    }
}