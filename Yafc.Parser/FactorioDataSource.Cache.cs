using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SharpCompress.Compressors;
using SharpCompress.Compressors.LZMA;
using Yafc.I18n;
using Yafc.Model;

namespace Yafc.Parser;

public static partial class FactorioDataSource {
    private static class Cache {
        // The cache file contains the data version and last access time, uncompressed, and a compressed stream of:
        // A 1-byte index into Cache.types for each object, indicating its concrete type, terminated by the byte FF.
        // The value of each field in each object, in the order fields[objectType], fields[objectType.BaseType], fields[objectType.BT.BT], ...:
        //     00 indicates null if it's possible for the value to be null.
        //     ints and enums use .NET's 7-bit integer encoding.
        //     FactorioObjects are serialized as their id plus one
        //     Lazy<>s are never null and are serialized as their Value. (i.e. 00 indicates a Lazy that returns null or zero, not a null Lazy.)
        //     LocalizableStrings are serialized as their key.
        //     Collections and strings are (length+1)-prefixed, followed by <length> objects or bytes.
        //     Other nested types are 01-prefixed (if needed to indicate not null), then written one field at a time.
        // After the objects are written, the additional data necessary for Database.LoadBuiltData and Analysis.excludedObjects is written in the
        // same format.
        // For probably-paranoia reasons, the CRC is written at the end of the compressed stream and also after the compressed stream. If either CRC
        //     is missing, the cached data will not be used.

        // -----------------------------------------------------------

        // Increment this to forcibly invalidate all existing cached data. This should happen when a change to FactorioDataDeserializer changes
        //     "Lua is loaded successfully but incorrectly" to "Lua is loaded successfully and correctly", and the change is not accompanied by a
        //     change to the Mod-fixes folder or the types, fields, or loadDataParameters static fields.
        // If the Lua previously didn't load at all, this should not be updated.
        // Also increment this if the compressed data format changes, for example if strings are deduplicated.
        private const uint CACHE_DATA_VERSION = 0;

        // The number of cache files that are allowed to exist before YAFC starts deleting old ones.
        private const int MAX_CACHE_COUNT = 20;

        private static readonly List<Type> types = [.. typeof(FactorioObject).Assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(FactorioObject))).OrderBy(t => t.IsAbstract).ThenBy(t => t.Name),
            typeof(FactorioIconPart), typeof(ModuleSpecification), typeof(Ingredient), typeof(Product), typeof(EntityEnergy), typeof(EffectReceiver),
            typeof(Effect), typeof(TemperatureRange)];
        private static readonly Dictionary<Type, int> typeDictionary = new(types.Select((x, i) => KeyValuePair.Create(x, i)));

        private static readonly Dictionary<Type, List<FieldInfo>> fields = types
            .Select(t => KeyValuePair.Create(t, t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.DeclaringType == t && !f.Name.Contains("<iconId>") && !f.Name.Contains("<id>")).OrderBy(f => f.Name)
                .ToList()))
            .ToDictionary();

        private static readonly MethodInfo loadBuiltData = typeof(Database)
            .GetMethod(nameof(Database.LoadBuiltData), BindingFlags.Static | BindingFlags.NonPublic)!;
        private static readonly ParameterInfo[] loadDataParameters = loadBuiltData.GetParameters();

        /// <summary>
        /// Takes the partially-computed hash, and appends data that depends on the capabilities of the current version of Yafc.
        /// </summary>
        /// <returns>The fully-computed hash to be used for file lookup and cache coherency checks.</returns>
        private static uint GetHash(Crc32 hash) {
            hash = hash.Clone();
            foreach (var type in types) {
                hash.Append(Encoding.UTF8.GetBytes(type.Name));
                foreach (var field in fields[type]) {
                    hash.Append(Encoding.UTF8.GetBytes(field.Name));
                }
            }
            foreach (var param in loadDataParameters) {
                hash.Append(Encoding.UTF8.GetBytes(param.Name!));
            }

            return hash.GetCurrentHashAsUInt32();
        }

        /// <summary>
        /// Gets the full path to the platform-dependent folder where cache files should be stored, and creates that folder if necessary.
        /// </summary>
        private static string GetCacheDirectory() {
            string path;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YAFC/cache");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Caches/yafc");
            }
            else { // Anything else, including Linux, *BSD, Solaris, etc.
                path = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ??
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
                path = Path.Combine(path, "yafc");
            }
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Gets the full path to the cache file for C# data.
        /// </summary>
        /// <param name="hash">The hash used for file lookup and cache coherency checks.</param>
        /// <returns></returns>
        private static string GetCSharpCacheFile(uint hash) => Path.Combine(GetCacheDirectory(), $"{hash:X8}.csharpcache");

        /// <summary>
        /// Writes data into the appropriate cache file from the storage maintained by <see cref="Database"/> and <see cref="Analysis"/>.
        /// (<see cref="ObjectWithQuality"/> must be initialized by the cache reader, but that only requires the data in <see cref="Database"/>.)
        /// </summary>
        /// <param name="modData">A <see cref="Crc32"/> that has been initialized with data dependent on the active mods, mod versions, and mod
        /// settings. To load this file, a <see cref="Crc32"/> initialized with the same data must be passed to <see cref="ReadCSharp"/>.</param>
        /// <returns>An <see cref="Action"/> that should be executed asynchronously to write the cache file.</returns>
        public static Action WriteCSharp(Crc32 modData) => () => {
            try {
                uint hash = GetHash(modData);

                // This will throw if the cache is open in any other process, either in ReadCSharp or WriteCSharp.
                using Stream stream = File.Open(GetCSharpCacheFile(hash), FileMode.Create, FileAccess.Write, FileShare.Read);
                stream.Write(BitConverter.GetBytes(CACHE_DATA_VERSION));
                stream.Write(BitConverter.GetBytes(DateTime.UtcNow.ToBinary()));

                using (BinaryWriter writer = new(LZipStream.Create(stream, CompressionMode.Compress, true))) {
                    // Write the type of each object.
                    foreach (FactorioObject obj in Database.objects.all) {
                        int index = typeDictionary[obj.GetType()];
                        // If this fires, we have at least 256 concrete types derived from FactorioObject. Should some of them be abstract or removed?
                        // If not, read and write ushorts instead of bytes. The hash will change, so CACHE_DATA_VERSION does not need to be updated.
                        Debug.Assert(index < 0xFF);
                        writer.Write((byte)index);
                    }
                    writer.Write((byte)0xFF);

                    // Write the field data for each object.
                    foreach (FactorioObject obj in Database.objects.all) {
                        Type currentType = obj.GetType();
                        while (currentType != typeof(object)) {
                            foreach (var field in fields[currentType]) {
                                writeValue(writer, field.FieldType, field.GetValue(obj));
                            }
                            currentType = currentType.BaseType!;
                        }
                    }

                    // Write the additional data needed by Database.LoadBuiltData
                    foreach (ParameterInfo parameter in loadDataParameters) {
                        if (parameter.Name == "formerAliases") {
                            // Recalculate the names that were included in formerAliases
                            var aliases = Database.objectsByTypeName.Where(kvp => kvp.Key != kvp.Value.typeDotName).ToDictionary();
                            writeValue(writer, aliases.GetType(), aliases);
                        }
                        else if (parameter.Name != "allObjects") { // Skip allObjects; it's everything we already wrote for Database.objects.all
                            object? value = typeof(Database).GetProperty(parameter.Name!)!.GetValue(null);
                            if (value is IObjectWithQuality<FactorioObject> objectWithQuality) {
                                value = objectWithQuality.target;
                            }
                            // Write the value stored in the property with the same name.
                            writeValue(writer, parameter.ParameterType, value);
                        }
                    }

                    // Write the additional data stored in Analysis.excludedObjects.
                    var excludedObjects = typeof(Analysis).GetField("excludedObjects", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    foreach (var analysis in (IEnumerable<Analysis>)typeof(Analysis)
                        .GetField("analyses", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!) {

                        writeValue(writer, excludedObjects.FieldType, excludedObjects.GetValue(analysis));
                    }
                    writer.Write(hash);
                } // End compressed stream

                stream.Write(BitConverter.GetBytes(hash));

                // We might have created a new file. Check to see if we need to delete any old ones.
                DeleteOldFiles();
            }
            catch (Exception ex) {
                logger.Warning(ex, "Could not write data cache.");
            }

            // Write one value of the type fieldType, recursing as necessary for complex values.
            static void writeValue(BinaryWriter writer, Type fieldType, object? value) {
                if (value == null) {
                    writer.Write((byte)0);
                }
                // Simple values (bool, float, nullable, string)
                else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                    // Nullable must appear before the value is <type> tests, since (float?)5 matches the type pattern float.
                    writer.Write((byte)1);
                    writeValue(writer, fieldType.GenericTypeArguments[0], value);
                }
                else if (value is bool b) {
                    writer.Write(b);
                }
                else if (fieldType == typeof(int) || (fieldType.IsEnum && fieldType.GetEnumUnderlyingType() == typeof(int))
                    || fieldType == typeof(uint) || (fieldType.IsEnum && fieldType.GetEnumUnderlyingType() == typeof(uint))) {

                    writer.Write7BitEncodedInt((int)value);
                }
                else if (value is float f) {
                    writer.Write(f);
                }
                else if (value is string or LocalizableString) {
                    if (value is not string str) {
                        str = (string)typeof(LocalizableString).GetField("key", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
                    }

                    byte[] bytes = Encoding.UTF8.GetBytes(str);
                    writer.Write7BitEncodedInt(bytes.Length + 1);
                    writer.Write(bytes);
                }

                // Yafc types (FactorioObject, Ingredient, Effect, etc.)
                else if (value is FactorioObject o) {
                    writer.Write7BitEncodedInt((int)o.id + 1);
                }
                else if (fields.TryGetValue(fieldType, out var nestedFields)) {
                    writer.Write((byte)1);
                    foreach (var field in nestedFields) {
                        object? nested = field.GetValue(value);
                        writeValue(writer, field.FieldType, nested);
                    }
                }

                // Collections
                // IDictionary must be before IEnumerable, since all IDictionaries are also IEnumerables
                else if (fieldType.IsGenericType && value is IDictionary dictionary) {
                    Type keyType = fieldType.GenericTypeArguments[0];
                    Type valueType = fieldType.GenericTypeArguments[1];
                    writer.Write7BitEncodedInt(dictionary.Count + 1);
                    foreach (DictionaryEntry entry in dictionary) {
                        writeValue(writer, keyType, entry.Key);
                        writeValue(writer, valueType, entry.Value);
                    }
                }
                else if ((fieldType.IsSZArray || fieldType.IsGenericType) && value is IEnumerable list && getCount(fieldType) is { } countProp) {
                    // HashSet<> doesn't have a non-generic Count property, so we have to call its Count property through reflection.
                    // We might as well do it for everything else too.
                    writer.Write7BitEncodedInt((int)countProp.GetValue(value)! + 1);
                    Type type = fieldType.GetElementType() /* Arrays */ ?? fieldType.GenericTypeArguments[0] /* everything else */;
                    foreach (object? item in list) {
                        writeValue(writer, type, item);
                    }
                }

                // Other weird types
                else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Lazy<>)) {
                    writeValue(writer, fieldType.GenericTypeArguments[0], value.GetType().GetProperty("Value")!.GetValue(value));
                }
                else if (fieldType.FullName!.StartsWith("System.ValueTuple`") && value is ITuple { Length: < 8 } tuple) {
                    // Restricted to System.ValueTuple of up to seven elements.
                    // Lengths of eight or more and System.Tuple values require reader updates (writing Rest and m_Item#)
                    for (int i = 0; i < tuple.Length; i++) {
                        object? nestedValue = tuple[i];
                        writeValue(writer, nestedValue?.GetType()!, nestedValue); // null-forgiving: If value is null, the type is not used.
                    }
                }

                else {
                    throw new Exception($"Don't know how to serialize {fieldType} into the cache. Does it need to be added to '{nameof(types)}'?");
                }
            }

            // Get a .Count property for this type, if at least one exists.
            static PropertyInfo? getCount(Type type) {
                var count = type.GetInterfaces().SelectMany(i => i.GetProperties()).FirstOrDefault(p => p.Name == "Count");
                if (count != null) {
                    return count;
                }

                while (type != null) {
                    count = type.GetProperty("Count");
                    if (count != null) {
                        return count;
                    }
                    type = type.BaseType!;
                }

                return null;
            }
        };

        /// <summary>
        /// Tries to load data from the appropriate cache file into the storage maintained by <see cref="ObjectWithQuality"/>, <see cref="Database"/>,
        /// and <see cref="Analysis"/>.
        /// </summary>
        /// <param name="progress">An <see cref="IProgress{T}"/> that receives two strings describing the current loading state.</param>
        /// <param name="modData">A <see cref="Crc32"/> that has been initialized with data dependent on the active mods, mod versions, and mod
        /// settings. This must match the <see cref="Crc32"/> passed to <see cref="WriteCSharp"/> when this file was written.</param>
        /// <returns><see langword="true"/> if the data was successfully loaded from the cache file, or <see langword="false"/> if the CRC did not
        /// match or an error occurred.</returns>
        public static bool ReadCSharp(IProgress<(string, string)> progress, Crc32 modData) {
            try {
                uint hash = GetHash(modData);

                // This will throw if the file doesn't exist, or if WriteCSharp has the file open in any process.
                using Stream stream = File.Open(GetCSharpCacheFile(hash), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

                byte[] bytes = new byte[4];
                if (stream.Read(bytes) != 4 || BitConverter.ToUInt32(bytes) != CACHE_DATA_VERSION) {
                    logger.Information($"Cache file has unrecognized data version {BitConverter.ToUInt32(bytes)}.");
                    return false;
                }

                stream.Seek(-4, SeekOrigin.End);
                if (stream.Read(bytes) != 4 || BitConverter.ToUInt32(bytes) != hash) {
                    logger.Information($"Cache file is missing its terminating uncompressed CRC.");
                    return false;
                }

                stream.Position = 4;
                stream.Write(BitConverter.GetBytes(DateTime.UtcNow.ToBinary())); // Update the atime

                using BinaryReader reader = new(LZipStream.Create(stream, CompressionMode.Decompress));
                byte typeId;
                // Read the type of each FactorioObject, and construct empty storage.
                List<FactorioObject> objects = [];
                while ((typeId = reader.ReadByte()) != 0xFF) {
                    FactorioObject obj = (FactorioObject)Activator.CreateInstance(types[typeId])!;
                    obj.id = (FactorioId)objects.Count;
                    objects.Add(obj);
                }

                // Read the field data for each object
                foreach (FactorioObject obj in objects) {
                    if ((int)obj.id % 1000 == 0) {
                        progress.Report((LSs.ProgressLoadingCache, LSs.ProgressRenderingXOfY.L((int)obj.id, objects.Count)));
                    }
                    Type currentType = obj.GetType();
                    while (currentType != typeof(object)) {
                        foreach (var field in fields[currentType]) {
                            field.SetValue(obj, readValue(reader, field.FieldType, objects));
                        }
                        currentType = currentType.BaseType!;
                    }
                }

                Quality.Normal = objects.OfType<Quality>().Single(q => q.name == "normal");
                ObjectWithQuality.LoadCache(objects);
                List<object?> parameters = [];

                // Read the additional data needed by Database.LoadBuiltData
                foreach (ParameterInfo parameter in loadDataParameters) {
                    if (parameter.Name == "allObjects") {
                        parameters.Add(objects);
                    }
                    else {
                        // No special case for formerAliases here; just read the Dictionary from the cache
                        parameters.Add(readValue(reader, parameter.ParameterType, objects));
                    }
                }
                // and call Database.LoadBuiltData
                loadBuiltData.Invoke(null, [.. parameters]);

                // Load and store the additional data for Analysis.excludedObjects.
                var excludedObjects = typeof(Analysis).GetField("excludedObjects", BindingFlags.Instance | BindingFlags.NonPublic)!;
                foreach (var analysis in (IEnumerable<Analysis>)typeof(Analysis)
                    .GetField("analyses", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!) {

                    excludedObjects.SetValue(analysis, readValue(reader, excludedObjects.FieldType, objects));
                }

                // Validate the end-of-compression CRC.
                if (reader.ReadUInt32() != hash) {
                    logger.Information("Compressed cache data did not end with with the expected CRC.");
                    return false;
                }
            }
            catch (Exception ex) {
                logger.Information(ex, "Could not load data cache.");
                return false;
            }

            return true;

            // Read and return one value of the type fieldType, recursing as necessary for complex values.
            // Index into objects when loading a FactorioObject.
            static object? readValue(BinaryReader reader, Type fieldType, List<FactorioObject> objects) {
                // We can't preemptively convert 0 to null here, at least without checking for quite a few special cases.
                // Simple values (bool, float, nullable, string)
                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                    if (reader.ReadByte() == 0) {
                        return null;
                    }
                    return readValue(reader, fieldType.GenericTypeArguments[0], objects);
                }
                else if (fieldType == typeof(bool)) {
                    return reader.ReadBoolean();
                }
                else if (fieldType == typeof(int) || (fieldType.IsEnum && fieldType.GetEnumUnderlyingType() == typeof(int))
                    || fieldType == typeof(uint) || (fieldType.IsEnum && fieldType.GetEnumUnderlyingType() == typeof(uint))) {

                    return reader.Read7BitEncodedInt();
                }
                else if (fieldType == typeof(float)) {
                    return reader.ReadSingle();
                }
                else if (fieldType == typeof(string) || fieldType.IsAssignableTo(typeof(LocalizableString))) {
                    int length = reader.Read7BitEncodedInt() - 1;
                    if (length == -1) { return null; }
                    if (length == 0 && fieldType == typeof(string)) { return ""; }

                    string value = Encoding.UTF8.GetString(reader.ReadBytes(length));
                    if (fieldType == typeof(string)) {
                        return value;
                    }

                    if (fieldType == typeof(LocalizableString)) {
                        // We can't create an abstract LocalizableString. Use LS0 as a backup. In the absence of dynamic (its presence is unlikely),
                        // the consumers will never take advantage of the implicit conversion or the .L() method.
                        fieldType = typeof(LocalizableString0);
                    }

                    return Activator.CreateInstance(fieldType, BindingFlags.Instance | BindingFlags.NonPublic, null, [value], null);
                }

                // Yafc types (FactorioObject, Ingredient, Effect, etc.)
                else if (fieldType.IsAssignableTo(typeof(FactorioObject))) {
                    int idx = reader.Read7BitEncodedInt();
                    if (idx == 0) { return null; }
                    return objects[idx - 1];
                }
                else if (fields.TryGetValue(fieldType, out var nestedFields)) {
                    if (reader.ReadByte() == 0) {
                        return null;
                    }

                    // Get an empty object we can populate
                    object resultObject = RuntimeHelpers.GetUninitializedObject(fieldType);

                    foreach (var field in nestedFields) {
                        field.SetValue(resultObject, readValue(reader, field.FieldType, objects));
                    }
                    return resultObject;
                }

                // Collections
                // IDictionary must be before IEnumerable, since all IDictionaries are also IEnumerables
                else if (fieldType.IsGenericType && fieldType.IsAssignableTo(typeof(IDictionary))) {
                    int count = reader.Read7BitEncodedInt() - 1;
                    if (count == -1) { return null; }

                    IDictionary resultObject = (IDictionary)Activator.CreateInstance(fieldType, [count])!;
                    Type keyType = fieldType.GenericTypeArguments[0];
                    Type valueType = fieldType.GenericTypeArguments[1];

                    for (int i = 0; i < count; i++) {
                        object key = readValue(reader, keyType, objects) ?? throw new($"Read a null key for a field of type {fieldType}.");
                        resultObject[key] = readValue(reader, valueType, objects);
                    }
                    return resultObject;
                }
                else if (fieldType.IsSZArray || (fieldType.IsGenericType && fieldType.IsAssignableTo(typeof(IEnumerable)))) {
                    int count = reader.Read7BitEncodedInt() - 1;
                    if (count == -1) { return null; }

                    if (fieldType.IsSZArray) {
                        Type elementType = fieldType.GetElementType()!;
                        IList array = Array.CreateInstance(elementType, count)!;
                        for (int i = 0; i < count; i++) {
                            array[i] = readValue(reader, elementType, objects);
                        }
                        return array;
                    }
                    else {
                        Type elementType = fieldType.GenericTypeArguments[0];
                        Type listType = typeof(List<>).MakeGenericType(elementType);
                        IList list = (IList)Activator.CreateInstance(listType, [count])!;
                        for (int i = 0; i < count; i++) {
                            list.Add(readValue(reader, elementType, objects));
                        }
                        if (listType.IsAssignableTo(fieldType)) {
                            return list;
                        }
                        // HashSet<> doesn't have a non-generic Add method, so construct a List<> first and rebuild it as a HashSet.
                        return Activator.CreateInstance(fieldType, [list])!;
                    }
                }

                // Other weird types
                else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Lazy<>)) {
                    Type valueType = fieldType.GenericTypeArguments[0];
                    var constructor = fieldType.GetConstructor([valueType])!;
                    return constructor.Invoke([readValue(reader, valueType, objects)]);
                }
                else if (fieldType.IsGenericType && fieldType.FullName!.StartsWith("System.ValueTuple`")) {
                    object? tuple = Activator.CreateInstance(fieldType);
                    var valueTypes = fieldType.GenericTypeArguments;
                    for (int i = 0; i < valueTypes.Length; i++) {
                        fieldType.GetField("Item" + (i + 1))!.SetValue(tuple, readValue(reader, valueTypes[i], objects));
                    }
                    return tuple;
                }

                else {
                    throw new Exception($"Don't know how to deserialize {fieldType} from the cache. Does it need to be added to '{nameof(types)}'?");
                }
            }
        }

        /// <summary>
        /// Delete old files matching <paramref name="pattern"/> in the <see cref="GetCachePath">cache path</see>. Files are 'old' if there are at
        /// least <see cref="MAX_CACHE_COUNT"/> files with newer last-accessed times.
        /// </summary>
        private static void DeleteOldFiles() {
            PriorityQueue<string, ulong> queue = new();
            foreach (string file in Directory.EnumerateFiles(GetCacheDirectory(), "*.csharpcache")) {
                try {
                    using BinaryReader reader = new(File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));

                    if (reader.ReadInt32() != CACHE_DATA_VERSION) {
                        // Delete files with an invalid version; they won't be useful unless you roll back to an older version of YAFC.
                        // The delete works even though we still have the file open; FileShare.Delete allows deletion before we close the file.
                        // It'll fail in the unlikely case that another process has the file open in ReadCSharp or WriteCSharp.
                        tryDelete(file);
                    }
                    else if (queue.Count == MAX_CACHE_COUNT) {
                        // We found a twenty-first file with valid version; delete the oldest.
                        tryDelete(queue.EnqueueDequeue(file, reader.ReadUInt64()));
                    }
                    else {
                        queue.Enqueue(file, reader.ReadUInt64());
                    }
                }
                catch { /* Can't check this file for expiry; ignore it. */ }
            }

            static void tryDelete(string path) {
                try {
                    File.Delete(path);
                }
                catch { /* Can't delete this file; ignore it. */ }
            }
        }
    }
}
