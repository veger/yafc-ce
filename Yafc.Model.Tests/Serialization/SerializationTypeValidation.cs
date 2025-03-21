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
    }

    public static TheoryData<Type> ModelObjectTypes => [.. AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
        .Where(t => typeof(ModelObject).IsAssignableFrom(t) && !t.IsAbstract)];

    [Theory]
    [MemberData(nameof(SerializableTypes))]
    // Ensure that all [Serializable] types in the Yafc namespace obey the serialization rules,
    // except compiler-generated types and those in Yafc.Blueprints.
    public void Serializables_AreSerializable(Type serializableType) {
        ConstructorInfo constructor = FindConstructor(serializableType);

        AssertConstructorParameters(serializableType, constructor.GetParameters());
        AssertSettableProperties(serializableType);
        AssertDictionaryKeys(serializableType);
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
        Assert.True(constructor != null, $"Could not find the constructor for type {MakeTypeName(type)}.");
        return constructor;
    }


    private static void AssertConstructorParameters(Type type, IEnumerable<ParameterInfo> parameters) {
        foreach (ParameterInfo parameter in parameters) {
            // Constructor parameters must be IsValueSerializerSupported
            Assert.True(ValueSerializer.IsValueSerializerSupported(parameter.ParameterType),
                $"Constructor of type {MakeTypeName(type)} parameter '{parameter.Name}' should be a supported value type.");

            // and must have a matching property
            PropertyInfo property = type.GetProperty(parameter.Name);
            Assert.True(property != null, $"Constructor of type {MakeTypeName(type)} parameter '{parameter.Name}' does not have a matching property.");
            Assert.True(parameter.ParameterType == property.PropertyType,
                $"Constructor of type {MakeTypeName(type)} parameter '{parameter.Name}' does not have the same type as its property.");
        }
    }

    private static void AssertSettableProperties(Type type) {
        foreach (PropertyInfo property in type.GetProperties().Where(p => p.GetSetMethod() != null)) {
            if (property.GetCustomAttribute<SkipSerializationAttribute>() == null) {
                // Properties with a public setter must be IsValueSerializerSupported
                Assert.True(ValueSerializer.IsValueSerializerSupported(property.PropertyType),
                    $"The type of property {MakeTypeName(type)}.{property.Name} should be a supported value type.");
            }
        }
    }

    private void AssertDictionaryKeys(Type type) {
        foreach (PropertyInfo property in type.GetProperties().Where(p => p.GetSetMethod() == null)) {
            if (property.GetCustomAttribute<SkipSerializationAttribute>() == null) {
                Type iDictionary = property.PropertyType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

                if (iDictionary != null) {
                    if (ValueSerializer.IsValueSerializerSupported(iDictionary.GenericTypeArguments[0])
                        && ValueSerializer.IsValueSerializerSupported(iDictionary.GenericTypeArguments[1])) {

                        object serializer = typeof(ValueSerializer<>).MakeGenericType(iDictionary.GenericTypeArguments[0]).GetField("Default").GetValue(null);
                        MethodInfo getJsonProperty = serializer.GetType().GetMethod(nameof(ValueSerializer<string>.GetJsonProperty));
                        // Dictionary keys must be serialized by an overridden ValueSerializer<T>.GetJsonProperty() method.
                        Assert.True(getJsonProperty != getJsonProperty.GetBaseDefinition(),
                            $"In {MakeTypeName(type)}.{property.Name}, the dictionary keys are of an unsupported type.");
                    }
                }
            }
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
