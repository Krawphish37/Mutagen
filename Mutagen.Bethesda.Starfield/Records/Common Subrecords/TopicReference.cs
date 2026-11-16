using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Starfield.Internals;

namespace Mutagen.Bethesda.Starfield;

public partial class ATopicReference
{
    public enum TopicType
    {
        Ref,
        Subtype
    }
}

partial class ATopicReferenceBinaryCreateTranslation
{
    public static IEnumerable<ATopicReference> Factory(MutagenFrame frame)
    {
        // C60: noted as the items of the list the caller began
        var index = 0;
        while (frame.TryReadSubrecordHeader(RecordTypes.PDTO, out var _))
        {
            var at = ValuePositionRecorder.Here(frame);
            if (at >= 0) ValuePositionRecorder.ItemFrom(at, index);
            index++;
            // The type picks the class
            var type = (ATopicReference.TopicType)frame.ReadInt32();
            switch (type)
            {
                case ATopicReference.TopicType.Ref:
                {
                    var reference = new TopicReference()
                    {
                        Reference = new FormLink<IDialogTopicGetter>(FormLinkBinaryTranslation.Instance.Parse(frame))
                    };
                    if (at >= 0)
                    {
                        ValuePositionRecorder.At(nameof(reference.Reference), at + 4, 4);
                        ValuePositionRecorder.End(frame);
                    }
                    yield return reference;
                    break;
                }
                case ATopicReference.TopicType.Subtype:
                {
                    var subtype = new TopicReferenceSubtype()
                    {
                        Subtype = new RecordType(frame.ReadInt32())
                    };
                    if (at >= 0)
                    {
                        ValuePositionRecorder.At(nameof(subtype.Subtype), at + 4, 4);
                        ValuePositionRecorder.End(frame);
                    }
                    yield return subtype;
                    break;
                }
                default:
                    throw new NotImplementedException();
            }
        }
    }
}

partial class ATopicReferenceBinaryWriteTranslation
{
    public static void Write(MutagenWriter writer, IEnumerable<IATopicReferenceGetter> topics)
    {
        foreach (var item in topics)
        {
            using var header = HeaderExport.Subrecord(writer, RecordTypes.PDTO);
            switch (item)
            {
                case ITopicReferenceGetter refGetter:
                    writer.Write((int)ATopicReference.TopicType.Ref);
                    FormLinkBinaryTranslation.Instance.Write(writer, refGetter.Reference);
                    break;
                case ITopicReferenceSubtypeGetter subType:
                    writer.Write((int)ATopicReference.TopicType.Subtype);
                    writer.Write(subType.Subtype.TypeInt);
                    break;
                default:
                    break;
            }
        }
    }
}