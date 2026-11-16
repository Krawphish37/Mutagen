using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Starfield.Internals;
using Noggog;
using System.Buffers.Binary;

namespace Mutagen.Bethesda.Starfield;

public partial class LegendaryItem
{
	[Flags]
	public enum StarSlot
	{
		First,
		Second,
		Third,
		Fourth,
		Fifth,
	}
}

partial class LegendaryItemBinaryCreateTranslation
{
	public static partial void FillBinaryLnamEntriesCustom(
		MutagenFrame frame,
		ILegendaryItemInternal item,
		PreviousParse lastParsed)
	{
		// The frame is bounded to the LNAM subrecord, but frame.Reader is the
		// shared parent stream. Read LNAM from the frame, then read CITC/CTDA
		// blocks directly from the Reader.
		var lnamHeader = frame.ReadSubrecordHeader(RecordTypes.LNAM);
		var entryCount = lnamHeader.ContentLength / 8;

		var entries = new ExtendedList<LegendaryItemLnamEntry>();
		// C60: the entries' values sit in LNAM, their conditions in the CITC/CTDA blocks after it
		if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.LnamEntries));
		var lnamAt = ValuePositionRecorder.Here(frame);

		// Read LNAM entries (8 bytes each: slot + unknown)
		for (int i = 0; i < entryCount; i++)
		{
			entries.Add(new LegendaryItemLnamEntry
			{
				Slot = (LegendaryItem.StarSlot)frame.Reader.ReadUInt32(),
				ReferenceMod = frame.Reader.ReadUInt32(),
			});
			if (lnamAt >= 0)
			{
				ValuePositionRecorder.At($"[{i}].Slot", lnamAt + 8 * i, 4);
				ValuePositionRecorder.At($"[{i}].ReferenceMod", lnamAt + 8 * i + 4, 4);
			}
		}

		// Read one CITC/CTDA condition block per LNAM entry from the shared Reader
		var reader = frame.Reader;
		for (int i = 0; i < entryCount; i++)
		{
			var citcAt = ValuePositionRecorder.Here(reader);
			var citcHeader = reader.ReadSubrecord(RecordTypes.CITC);
			var condCount = BinaryPrimitives.ReadInt32LittleEndian(citcHeader.Content);

			var outerFrame = new MutagenFrame(reader);
			if (ValuePositionRecorder.On)
			{
				ValuePositionRecorder.ItemFrom(citcAt, i);
				ValuePositionRecorder.List(outerFrame, "Conditions");
				ValuePositionRecorder.CountAt(citcAt + citcHeader.HeaderLength, 4);
			}
			entries[i].Conditions = Mutagen.Bethesda.Plugins.Binary.Translations.ListBinaryTranslation<Condition>.Instance.Parse(
					reader: outerFrame,
					amount: condCount,
					transl: Condition.TryCreateFromBinary)
				.CastExtendedList<Condition>();
			if (ValuePositionRecorder.On)
			{
				ValuePositionRecorder.End(outerFrame);
				ValuePositionRecorder.End(outerFrame);
			}
		}
		if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);

		item.LnamEntries = entries;
	}
}

partial class LegendaryItemBinaryWriteTranslation
{
	public static partial void WriteBinaryLnamEntriesCustom(
		MutagenWriter writer,
		ILegendaryItemGetter item)
	{
		if (item.LnamEntries is not { Count: > 0 } entries) return;

		// Write single LNAM subrecord with all entries packed
		using (HeaderExport.Subrecord(writer, RecordTypes.LNAM))
		{
			foreach (var entry in entries)
			{
				writer.Write((uint)entry.Slot);
				writer.Write(entry.ReferenceMod);
			}
		}

		// Write CITC/CTDA blocks, one per entry
		foreach (var entry in entries)
		{
			Mutagen.Bethesda.Plugins.Binary.Translations.ListBinaryTranslation<IConditionGetter>.Instance.WriteWithCounter(
				writer: writer,
				items: entry.Conditions,
				counterType: RecordTypes.CITC,
				counterLength: 4,
				transl: (MutagenWriter subWriter, IConditionGetter subItem, TypedWriteParams conv) =>
				{
					subItem.WriteToBinary(subWriter);
				});
		}
	}
}

partial class LegendaryItemBinaryOverlay
{
	public IReadOnlyList<ILegendaryItemLnamEntryGetter>? LnamEntries { get; private set; }

	partial void LnamEntriesCustomParse(
		OverlayStream stream,
		int finalPos,
		int offset,
		RecordType type,
		PreviousParse lastParsed)
	{
		var lnamSubrecord = stream.ReadSubrecord(RecordTypes.LNAM);
		var entryCount = lnamSubrecord.ContentLength / 8;

		var entries = new List<LegendaryItemLnamEntry>();

		for (int i = 0; i < entryCount; i++)
		{
			var entryOffset = i * 8;
			entries.Add(new LegendaryItemLnamEntry
			{
				Slot = (LegendaryItem.StarSlot)BinaryPrimitives.ReadUInt32LittleEndian(lnamSubrecord.Content.Slice(entryOffset)),
				ReferenceMod = BinaryPrimitives.ReadUInt32LittleEndian(lnamSubrecord.Content.Slice(entryOffset + 4)),
			});
		}

		for (int i = 0; i < entryCount; i++)
		{
			var citcHeader = stream.ReadSubrecord(RecordTypes.CITC);
			var condCount = BinaryPrimitives.ReadInt32LittleEndian(citcHeader.Content);

			var mutagenFrame = new MutagenFrame(new MutagenInterfaceReadStream(stream, _package.MetaData));
			entries[i].Conditions = Mutagen.Bethesda.Plugins.Binary.Translations.ListBinaryTranslation<Condition>.Instance.Parse(
					reader: mutagenFrame,
					amount: condCount,
					transl: Condition.TryCreateFromBinary)
				.CastExtendedList<Condition>();
		}

		LnamEntries = entries;
	}
}
