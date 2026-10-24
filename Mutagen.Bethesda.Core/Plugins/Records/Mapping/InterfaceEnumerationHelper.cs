using Loqui;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals;
using Mutagen.Bethesda.Plugins.Exceptions;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Records.Mapping;

internal static class InterfaceEnumerationHelper
{
    public static bool TryEnumerateLinkRecordsFor<T>(
        GameCategory category,
        T obj, 
        Type linkInterface,
        out IEnumerable<IMajorRecordGetter> interfaces)
        where T : IMajorRecordGetterEnumerable, ILoquiObjectGetter
    {
        var mapping = LinkInterfaceMapping.Instance.InterfaceToObjectTypes(category);
        if (!mapping.TryGetValue(linkInterface, out var inheritingTypes))
        {
            interfaces = [];
            return false;
        }

        if (inheritingTypes.Setter && !obj.Registration.SetterType.IsAssignableFrom(obj.GetType()))
        {
            interfaces = [];
            return true;
        }
        
        interfaces = inheritingTypes.Registrations.SelectMany(t => obj.EnumerateMajorRecords(inheritingTypes.Setter ? t.SetterType : t.GetterType));
        return true;
    }
    
    public static bool TryEnumerateAspectRecordsFor<T>(
        GameCategory category,
        T obj, 
        Type linkInterface,
        out IEnumerable<IMajorRecordGetter> interfaces)
        where T : IMajorRecordGetterEnumerable, ILoquiObjectGetter
    {
        var mapping = AspectInterfaceMapping.Instance.InterfaceToObjectTypes(category);
        if (!mapping.TryGetValue(linkInterface, out var inheritingTypes))
        {
            interfaces = [];
            return false;
        }

        if (inheritingTypes.Setter && !obj.Registration.SetterType.IsAssignableFrom(obj.GetType()))
        {
            interfaces = [];
            return true;
        }
        
        interfaces = inheritingTypes.Registrations.SelectMany(t => obj.EnumerateMajorRecords(inheritingTypes.Setter ? t.SetterType : t.GetterType, throwIfUnknown: false));
        return true;
    }
    
    public static bool TryEnumerateInterfaceRecordsFor<T>(
        GameCategory category,
        T obj, 
        Type linkInterface,
        out IEnumerable<IMajorRecordGetter> interfaces)
        where T : IMajorRecordGetterEnumerable, ILoquiObjectGetter
    {
        if (!MetaInterfaceMapping.Instance.TryGetRegistrationsForInterface(category, linkInterface, out var inheritingTypes))
        {
            interfaces = [];
            return false;
        }

        if (inheritingTypes.Setter && !obj.Registration.SetterType.IsAssignableFrom(obj.GetType()))
        {
            interfaces = [];
            return true;
        }
        
        interfaces = inheritingTypes.Registrations.SelectMany(t =>
        {
            var registrationType = inheritingTypes.Setter ? t.SetterType : t.GetterType;
            var records = obj.EnumerateMajorRecords(registrationType, throwIfUnknown: false);
            if (!RequiresTypeFilter(linkInterface, registrationType)) return records;
            return records.Where(linkInterface.IsInstanceOfType);
        });
        return true;
    }
    
    /// <summary>
    /// Inheriting interface mappings (ex: IWeaponModificationGetter) point to the registration of the
    /// abstract base class that owns the group (ex: AObjectModification).  Records enumerated from such a
    /// registration are not all of the queried type, and need to be filtered
    /// </summary>
    internal static bool RequiresTypeFilter(Type queriedType, Type registrationType)
    {
        return !queriedType.IsAssignableFrom(registrationType);
    }

    /// <summary>
    /// Returns the getter type that records enumerated from a mapping's registration must be filtered to,
    /// or null if every record of that registration already matches
    /// </summary>
    internal static Type? GetGetterTypeFilter(InterfaceMappingResult mapping, ILoquiRegistration registration)
    {
        var queriedType = mapping.Types.Getter;
        return RequiresTypeFilter(queriedType, registration.GetterType) ? queriedType : null;
    }

    public static bool TryEnumerateLinkContextsFor<T, TMod, TModGetter>(
        GameCategory category,
        T obj, 
        Type linkInterface,
        ILinkCache linkCache,
        out IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> interfaces)
        where T : IMajorRecordContextEnumerable<TMod, TModGetter>, ILoquiObjectGetter
        where TModGetter : IModGetter
        where TMod : TModGetter, IMod
    {
        var mapping = LinkInterfaceMapping.Instance.InterfaceToObjectTypes(category);
        if (!mapping.TryGetValue(linkInterface, out var inheritingTypes))
        {
            interfaces = [];
            return false;
        }
    
        if (inheritingTypes.Setter && !obj.Registration.SetterType.IsAssignableFrom(obj.GetType()))
        {
            interfaces = [];
            return true;
        }

        interfaces = inheritingTypes.Registrations
            .SelectMany(t => obj.EnumerateMajorRecordContexts(linkCache, inheritingTypes.Setter ? t.SetterType : t.GetterType));
        return true;
    }
    
    public static bool TryEnumerateAspectContextsFor<T, TMod, TModGetter>(
        GameCategory category,
        T obj, 
        Type linkInterface,
        ILinkCache linkCache,
        Func<T, IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>>> getEnumer,
        out IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> interfaces)
        where T : IMajorRecordContextEnumerable<TMod, TModGetter>, ILoquiObjectGetter
        where TModGetter : IModGetter
        where TMod : TModGetter, IMod
    {
        var mapping = AspectInterfaceMapping.Instance.InterfaceToObjectTypes(category);
        if (!mapping.TryGetValue(linkInterface, out var inheritingTypes))
        {
            interfaces = [];
            return false;
        }
    
        if (inheritingTypes.Setter && !obj.Registration.SetterType.IsAssignableFrom(obj.GetType()))
        {
            interfaces = [];
            return true;
        }
        
        interfaces = inheritingTypes.Registrations
            .SelectMany(t => obj.EnumerateMajorRecordContexts(linkCache, inheritingTypes.Setter ? t.SetterType : t.GetterType, throwIfUnknown: false));
        return true;
    }
    
    public delegate IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> ContextGetter<TMod, TModGetter>(
            ILinkCache linkCache,
            Type type,
            bool throwIfUnknown)
        where TModGetter : IModGetter
        where TMod : TModGetter, IMod;

    public static bool TryEnumerateInterfaceContextsFor<T, TMod, TModGetter>(
        GameCategory category,
        T obj, 
        Type linkInterface,
        ILinkCache linkCache,
        ContextGetter<TMod, TModGetter> getter,
        out IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> interfaces)
        where T : ILoquiObjectGetter
        where TModGetter : IModGetter
        where TMod : TModGetter, IMod
    {
        if (!MetaInterfaceMapping.Instance.TryGetRegistrationsForInterface(category, linkInterface, out var inheritingTypes))
        {
            interfaces = [];
            return false;
        }
    
        if (inheritingTypes.Setter && !obj.Registration.SetterType.IsAssignableFrom(obj.GetType()))
        {
            interfaces = [];
            return true;
        }
        
        interfaces = inheritingTypes.Registrations
            .SelectMany(t =>
            {
                var registrationType = inheritingTypes.Setter ? t.SetterType : t.GetterType;
                var contexts = getter(linkCache, registrationType, throwIfUnknown: false);
                if (!RequiresTypeFilter(linkInterface, registrationType)) return contexts;
                return contexts.Where(x => linkInterface.IsInstanceOfType(x.Record));
            });
        return true;
    }

    public static bool TryEnumerateInterfaceContextsFor<T, TMod, TModGetter>(
        GameCategory category,
        T obj, 
        Type linkInterface,
        ILinkCache linkCache,
        out IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> interfaces)
        where T : IMajorRecordContextEnumerable<TMod, TModGetter>, ILoquiObjectGetter
        where TModGetter : IModGetter
        where TMod : TModGetter, IMod
    {
        return TryEnumerateInterfaceContextsFor<T, TMod, TModGetter>(category, obj, linkInterface, linkCache,
            obj.EnumerateMajorRecordContexts,
            out interfaces);
    }

    public static IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> EnumerateGroupContexts<TMod, TModGetter, TMajor, TMajorTarget>(
            IGroupGetter<TMajorTarget> srcGroup,
            Type type,
            ModKey modKey,
            Func<TMod, IGroup<TMajor>> group,
            Func<TModGetter, IGroupGetter<TMajorTarget>> groupGetter)
        where TModGetter : IModGetter
        where TMod : TModGetter, IMod
        where TMajor : class, IMajorRecordInternal, TMajorTarget
        where TMajorTarget : class, IMajorRecordGetter
    {
        if (!srcGroup.ContainedRecordType.InheritsFrom(type)) yield break;

        foreach (var item in srcGroup.Records.Catch(ex =>
                 {
                     RecordException.EnrichAndThrow(ex, modKey);
                     throw RecordException.Enrich(ex, modKey);
                 }))
        {
            yield return new GroupModContext<TMod, TModGetter, TMajor, TMajorTarget>(
                modKey: modKey,
                record: item,
                group: group,
                groupGetter: groupGetter);
        }
    }
}