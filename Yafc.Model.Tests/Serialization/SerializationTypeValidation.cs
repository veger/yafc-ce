using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Yafc.Model.Serialization.Tests;

public class SerializationTypeValidation {
    [Theory]
    [MemberData(nameof(ModelObjectTypes))]
    // Ensure that all concrete types derived from ModelObject obey the serialization rules.
    // For details about the serialization rules and how to approach changes and test failures, see Docs/Architecture/Serialization.md.
    public void ModelObjects_AreSerializable(Type modelObjectType) {
        ConstructorInfo constructor = FindConstructor(modelObjectType);

        PropertyInfo ownerProperty = modelObjectType.GetProperty("owner");
        if (ownerProperty != null) {
            // If derived from ModelObject<T> (as tested by "There's an 'owner' property"), the first constructor parameter must be T.
            Assert.True(constructor.GetParameters().Length > 0, $"The first constructor parameter for type {MakeTypeName(modelObjectType)} should be the parent object.");
            Type baseType = typeof(ModelObject<>).MakeGenericType(ownerProperty.PropertyType);
            Assert.True(baseType.IsAssignableFrom(modelObjectType), $"The first constructor parameter for type {MakeTypeName(modelObjectType)} is not the parent type.");
        }

        // Cheating a bit here: Project is the only ModelObject that is not a ModelObject<T>, and its constructor has no parameters.
        // So we're just skipping a parameter that doesn't exist.
        AssertConstructorParameters(modelObjectType, constructor.GetParameters().Skip(1));
        AssertSettableProperties(modelObjectType);
        AssertDictionaryKeys(modelObjectType);
        AssertCollectionValues(modelObjectType);
        AssertNoArraysOfSerializableValues(modelObjectType);
    }

    public static TheoryData<Type> ModelObjectTypes => [.. AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
        .Where(t => typeof(ModelObject).IsAssignableFrom(t) && !t.IsAbstract)];

    [Theory]
    [MemberData(nameof(SerializableTypes))]
    // Ensure that all [Serializable] types in the Yafc namespace obey the serialization rules,
    // except compiler-generated types and those in Yafc.Blueprints.
    // For details about the serialization rules and how to approach changes and test failures, see Docs/Architecture/Serialization.md.
    public void Serializables_AreSerializable(Type serializableType) {
        ConstructorInfo constructor = FindConstructor(serializableType);

        AssertConstructorParameters(serializableType, constructor.GetParameters());
        AssertSettableProperties(serializableType);
        AssertDictionaryKeys(serializableType);
        AssertCollectionValues(serializableType);
        AssertNoArraysOfSerializableValues(serializableType);
    }

    public static TheoryData<Type> SerializableTypes => [.. AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
        .Where(t => t.FullName.StartsWith("Yafc.") && !t.FullName.StartsWith("Yafc.Blueprints")
            && t.GetCustomAttribute<SerializableAttribute>() != null && t.GetCustomAttribute<CompilerGeneratedAttribute>() == null)];

    internal static ConstructorInfo FindConstructor(Type type) {
        BindingFlags flags = BindingFlags.Instance;
        if (type.GetCustomAttribute<DeserializeWithNonPublicConstructorAttribute>() != null) {
            flags |= BindingFlags.NonPublic;
        }
        else {
            flags |= BindingFlags.Public;
        }

        // The constructor (public or non-public, depending on the attribute) must exist
        ConstructorInfo constructor = type.GetConstructors(flags).FirstOrDefault();
        Assert.True(constructor != null, $"Could not find the constructor for type {MakeTypeName(type)}. Consider adding or removing [DeserializeWithNonPublicConstructor].");
        return constructor;
    }


    private static void AssertConstructorParameters(Type type, IEnumerable<ParameterInfo> parameters) {
        foreach (ParameterInfo parameter in parameters) {
            // Constructor parameters must be IsValueSerializerSupported
            Assert.True(ValueSerializer.IsValueSerializerSupported(parameter.ParameterType),
                $"In the {MakeTypeName(type)} constructor, parameter '{parameter.Name}' should be a type supported for writable properties.");
            Assert.True(ValueSerializerExists(parameter.ParameterType, out _),
                $"For the {MakeTypeName(type)} constructor, parameter '{parameter.Name}', IsValueSerializerSupported claims type {MakeTypeName(parameter.ParameterType)} is supported for writable properties (also collection/dictionary values), but ValueSerializer<>.CreateValueSerializer did not create a serializer.");

            // and must have a matching property
            PropertyInfo property = type.GetProperty(parameter.Name);
            Assert.True(property != null, $"Constructor of type {MakeTypeName(type)} parameter '{parameter.Name}' does not have a matching property.");
            Assert.True(parameter.ParameterType == property.PropertyType,
                $"Constructor of type {MakeTypeName(type)} parameter '{parameter.Name}' does not have the same type as its property.");
        }
    }

    private static void AssertSettableProperties(Type type) {
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetSetMethod() != null)) {
            if (property.GetCustomAttribute<SkipSerializationAttribute>() == null) {
                // Properties with a public setter must be IsValueSerializerSupported.
                Assert.True(ValueSerializer.IsValueSerializerSupported(property.PropertyType),
                    $"The type of property {MakeTypeName(type)}.{property.Name} should be a type supported for writable properties.");
                Assert.True(ValueSerializerExists(property.PropertyType, out _),
                    $"For the property {MakeTypeName(type)}.{property.Name}, IsValueSerializerSupported claims the type {MakeTypeName(property.PropertyType)} is supported for writable properties (also collection/dictionary values), but ValueSerializer<>.CreateValueSerializer did not create a serializer.");
            }
        }
    }

    private void AssertDictionaryKeys(Type type) {
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetSetMethod() == null)) {
            if (property.GetCustomAttribute<SkipSerializationAttribute>() == null) {
                Type iDictionary = property.PropertyType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

                if (iDictionary != null) {
                    if (ValueSerializer.IsKeySerializerSupported(iDictionary.GenericTypeArguments[0])
                        && ValueSerializer.IsValueSerializerSupported(iDictionary.GenericTypeArguments[1])) {

                        Assert.True(ValueSerializerExists(iDictionary.GenericTypeArguments[0], out object serializer),
                            $"For the property {MakeTypeName(type)}.{property.Name}, IsKeySerializerSupported claims type {MakeTypeName(iDictionary.GenericTypeArguments[0])} is supported for dictionary keys, but ValueSerializer<>.CreateValueSerializer did not create a serializer.");
                        MethodInfo getJsonProperty = serializer.GetType().GetMethod(nameof(ValueSerializer<string>.GetJsonProperty));
                        // Dictionary keys must be serialized by an overridden ValueSerializer<T>.GetJsonProperty() method.
                        Assert.True(getJsonProperty != getJsonProperty.GetBaseDefinition(),
                            $"In {MakeTypeName(type)}.{property.Name}, the dictionary keys claim to be supported, but {MakeTypeName(serializer.GetType())} does not have an overridden {nameof(ValueSerializer<int>.GetJsonProperty)} method.");

                        Assert.True(ValueSerializerExists(iDictionary.GenericTypeArguments[1], out _),
                            $"For the property {MakeTypeName(type)}.{property.Name}, IsValueSerializerSupported claims the type {MakeTypeName(iDictionary.GenericTypeArguments[1])} is supported for dictionary values (also writable properties), but ValueSerializer<>.CreateValueSerializer did not create a serializer.");
                    }
                }
            }
        }
    }

    private static void AssertCollectionValues(Type type) {
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetSetMethod() == null)) {
            if (property.GetCustomAttribute<SkipSerializationAttribute>() == null) {
                Type iDictionary = property.PropertyType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
                Type iCollection = property.PropertyType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>));

                if (iDictionary == null && iCollection != null && ValueSerializer.IsKeySerializerSupported(iCollection.GenericTypeArguments[0])) {
                    Assert.True(ValueSerializerExists(iCollection.GenericTypeArguments[0], out object serializer),
                        $"For the property {MakeTypeName(type)}.{property.Name}, IsValueSerializerSupported claims the type {MakeTypeName(iCollection.GenericTypeArguments[0])} is supported for collection values (also writable properties), but ValueSerializer<>.CreateValueSerializer did not create a serializer.");
                }
            }
        }
    }

    private static void AssertNoArraysOfSerializableValues(Type type) {
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetSetMethod() == null)) {
            if (property.GetCustomAttribute<SkipSerializationAttribute>() == null) {
                if (property.PropertyType.IsArray && ValueSerializer.IsValueSerializerSupported(property.PropertyType.GetElementType())) {
                    // Public properties without a public setter must not be arrays of writable-property values.
                    // (and properties with a public setter typically can't implement ICollection<>, as checked by AssertSettableProperties.)
                    Assert.Fail($"The non-writable property {MakeTypeName(type)}.{property.Name} is an array of writable-property values, which is not supported.");
                }
            }
        }
    }

    private static bool ValueSerializerExists(Type type, out object serializer) {
        try {
            serializer = typeof(ValueSerializer<>).MakeGenericType(type).GetField("Default").GetValue(null);
            return serializer != null;
        }
        catch {
            serializer = null;
            return false;
        }
    }

    internal static string MakeTypeName(Type type) {
        if (type.IsGenericType) {
            if (type.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                return MakeTypeName(type.GetGenericArguments()[0]) + '?';
            }
            StringBuilder result = new(type.Name.Split('`')[0]);
            result.Append('<').AppendJoin(", ", type.GenericTypeArguments.Select(MakeTypeName)).Append('>');
            return result.ToString();
        }

        if (type == typeof(bool)) { return "bool"; }
        if (type == typeof(byte)) { return "byte"; }
        if (type == typeof(sbyte)) { return "sbyte"; }
        if (type == typeof(char)) { return "char"; }
        if (type == typeof(short)) { return "short"; }
        if (type == typeof(ushort)) { return "ushort"; }
        if (type == typeof(int)) { return "int"; }
        if (type == typeof(uint)) { return "uint"; }
        if (type == typeof(long)) { return "long"; }
        if (type == typeof(ulong)) { return "ulong"; }
        if (type == typeof(nint)) { return "nint"; }
        if (type == typeof(nuint)) { return "nuint"; }
        if (type == typeof(float)) { return "float"; }
        if (type == typeof(double)) { return "double"; }
        if (type == typeof(decimal)) { return "decimal"; }
        if (type == typeof(string)) { return "string"; }
        if (type == typeof(object)) { return "object"; }

        return type.Name;
    }
}
