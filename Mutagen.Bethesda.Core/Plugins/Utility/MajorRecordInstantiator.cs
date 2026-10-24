using Loqui;
using Mutagen.Bethesda.Plugins.Records;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Mutagen.Bethesda.Plugins.Utility;

/// <summary>
/// A static class encapsulating the job of creating a new Major Record in a generic context
/// </summary>
/// <typeparam name="TMajor">
/// Type of Major Record to instantiate.  Can be the direct class, or one of its interfaces.
/// </typeparam>
public static class MajorRecordInstantiator<TMajor>
    where TMajor : IMajorRecordGetter
{
    /// <summary>
    /// Function to call to retrieve a new Major Record of type T.<br />
    /// If T's class is abstract, the function throws an <see cref="InvalidOperationException"/> when called
    /// </summary>
    public static readonly Func<FormKey, GameRelease, TMajor> Activator;

    static MajorRecordInstantiator()
    {
        Warmup.Init();
        if (!LoquiRegistration.TryGetRegister(typeof(TMajor), out var regis))
        {
            throw new ArgumentException("Tried to instantiate a record not registered in Loqui");
        }

        if (regis.ClassType.IsAbstract)
        {
            // Throwing here would poison this type initializer for the rest of the process,
            // so defer the error until someone actually tries to construct one
            var classType = regis.ClassType;
            Activator = (_, _) => throw MajorRecordInstantiator.AbstractRecordException(classType);
            return;
        }

        var ctorInfo = regis.ClassType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(c =>
            {
                var param = c.GetParameters();
                if (param.Length != 2) return false;
                if (param[0].ParameterType != typeof(FormKey)) return false;
                if (param[1].ParameterType != typeof(GameRelease)) return false;
                return true;
            })
            .First();
        ParameterExpression formKey = Expression.Parameter(typeof(FormKey), "formKey");
        ParameterExpression gameRelease = Expression.Parameter(typeof(GameRelease), "gameRelease");
        NewExpression newExp = Expression.New(ctorInfo, formKey, gameRelease);
        LambdaExpression lambda = Expression.Lambda(typeof(Func<FormKey, GameRelease, TMajor>), newExp, formKey, gameRelease);
        Activator = (Func<FormKey, GameRelease, TMajor>)lambda.Compile();
    }
}
    
/// <summary>
/// A static class encapsulating the job of creating a new Major Record in a generic context
/// </summary>
public static class MajorRecordInstantiator
{
    private static ImmutableDictionary<Type, Func<FormKey, GameRelease, IMajorRecord>> _activators = ImmutableDictionary<Type, Func<FormKey, GameRelease, IMajorRecord>>.Empty;

    /// <summary>
    /// Function to call to retrieve a new Major Record of type T
    /// </summary>
    public static IMajorRecord Activator(FormKey formKey, GameRelease release, Type recordType)
    {
        if (!LoquiRegistration.TryGetRegister(recordType, out var regis))
        {
            throw new ArgumentException("Tried to instantiate a record not registered in Loqui", nameof(recordType));
        }

        if (!_activators.TryGetValue(regis.ClassType, out var activator))
        {
            activator = GetActivatorFor(regis.ClassType);
            _activators = _activators.SetItem(regis.ClassType, activator);
        }

        return activator(formKey, release);
    }

    /// <summary>
    /// Throws an exception if the given record type is abstract, and so cannot be instantiated
    /// </summary>
    /// <param name="recordType">Class type of the record to check</param>
    /// <exception cref="InvalidOperationException">If the record type is abstract</exception>
    internal static void ThrowIfAbstract(Type recordType)
    {
        if (recordType.IsAbstract)
        {
            throw AbstractRecordException(recordType);
        }
    }

    internal static InvalidOperationException AbstractRecordException(Type recordType)
    {
        var concrete = GetConcreteSubtypes(recordType);
        var example = concrete.Count > 0 ? concrete[0] : "SubType";
        var message = $"Cannot create a new {recordType.Name} because it is an abstract record type. "
            + $"Construct a concrete subtype instead and add it to the group, for example: group.Add(new {example}(mod)).";
        if (concrete.Count > 0)
        {
            message += $" Concrete subtypes: {string.Join(", ", concrete)}";
        }
        return new InvalidOperationException(message);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only used to list subtypes in an error message")]
    private static IReadOnlyList<string> GetConcreteSubtypes(Type recordType)
    {
        Type?[] types;
        try
        {
            types = recordType.Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        return types
            .Where(t => t != null
                && t.IsPublic
                && !t.IsAbstract
                && t.IsSubclassOf(recordType)
                && LoquiRegistration.TryGetRegister(t, out _))
            .Select(t => t!.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static Func<FormKey, GameRelease, IMajorRecord> GetActivatorFor(Type type)
    {
        if (type.IsAbstract)
        {
            return (_, _) => throw AbstractRecordException(type);
        }

        var ctorInfo = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(c =>
            {
                var param = c.GetParameters();
                if (param.Length != 2) return false;
                if (param[0].ParameterType != typeof(FormKey)) return false;
                if (param[1].ParameterType != typeof(GameRelease)) return false;
                return true;
            })
            .First();
        ParameterExpression formKey = Expression.Parameter(typeof(FormKey), "formKey");
        ParameterExpression gameRelease = Expression.Parameter(typeof(GameRelease), "gameRelease");
        NewExpression newExp = Expression.New(ctorInfo, formKey, gameRelease);
        LambdaExpression lambda = Expression.Lambda(typeof(Func<FormKey, GameRelease, IMajorRecord>), newExp, formKey, gameRelease);
        return (Func<FormKey, GameRelease, IMajorRecord>)lambda.Compile();
    }
}