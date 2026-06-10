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
using SDL2;
using SharpCompress.Compressors;
using SharpCompress.Compressors.LZMA;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc.Parser;

public static partial class FactorioDataSource {
    private abstract class Cache {
        // All cache files start with a 4-byte format version and an 8-byte last-access time. They then have an LZip-compressed stream of cached data.
        // The CRC (also the filename) is written at the end of the compressed stream and also after the compressed stream. If either CRC is missing,
        // the cached data will not be used.

        /// <summary>
        /// The number of cache files of each type that are allowed to exist before YAFC starts deleting old ones.
        /// </summary>
        private const int MAX_CACHE_COUNT = 20;

        /// <summary>
        /// The version of the cache container. Increment this if the container changes from:
        /// [version] [access time] LZip { [cache data] [crc] } [crc]
        /// This will invalidate all caches.
        /// </summary>
        private const int CONTAINER_VERSION = 0;

        /// <summary>
        /// When implemented in a derived class, gets the current data version for caches of this type. Derived types should update this when bugs
        /// that generated incorrect cache data are fixed, or changes to the reader prevent it from reading old cache files. Incrementing this will
        /// invalidate all caches of that type.
        /// </summary>
        protected abstract int DataVersion { get; }

        /// <summary>
        /// When implemented in a derived class, gets the extension, excluding the '.', used by cache files of this type.
        /// </summary>
        protected abstract string FileExtension { get; }

        /// <summary>
        /// When implemented in a derived class, finishes calculating the content hash, given an in-progress hash of content from the loaded mods.
        /// </summary>
        protected abstract uint FinishHash(Crc32 partialHash);

        /// <summary>
        /// When implemented in a derived class, writes the cache data to the supplied <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">The writable stream that should receive the cache data.</param>
        /// <remarks>Cache container concerns are handled automatically by <see cref="Cache"/>.</remarks>
        protected abstract void Write(Stream stream);

        /// <summary>
        /// When implemented in a derived class, reads the cache data from the supplied <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">The readable stream that contains the cache data. This stream will contain trailing data that was not written by
        /// <see cref="Write"/>, and implementing classes must not read that data.</param>
        /// <param name="progress">An <see cref="IProgress{T}"/> that receives two strings describing the current loading state.</param>
        /// <returns><see langword="true"/> if the data was loaded successfully, or <see langword="false"/> if an error occurred.</returns>
        /// <remarks>Cache container concerns are handled automatically by <see cref="Cache"/>.</remarks>
        protected abstract bool Read(Stream stream, IProgress<(string, string)> progress);

        /// <summary>
        /// Writes data into the appropriate cache file from the storage maintained by <see cref="Database"/> and <see cref="Analysis"/>.
        /// (<see cref="ObjectWithQuality"/> must be initialized by the cache reader, but that only requires the data in <see cref="Database"/>.)
        /// </summary>
        /// <param name="hash">The hash that was received from <see cref="ReadCSharp"/>.</param>
        /// <returns>An <see cref="Action"/> that should be executed asynchronously to write the cache file.</returns>
        public static Action WriteCSharp(uint hash) => () => {
            try {
                CSharpCache cache = new();
                using var stream = cache.OpenWriteCache(hash);
                cache.Write(stream);
                // We might have created a new file. Check to see if we need to delete any old ones.
                cache.DeleteOldFiles();
            }
            catch (Exception ex) {
                logger.Warning(ex, "Could not write data cache.");
            }
        };

        /// <summary>
        /// Tries to load data from the appropriate cache file into the storage maintained by <see cref="ObjectWithQuality"/>, <see cref="Database"/>,
        /// and <see cref="Analysis"/>.
        /// </summary>
        /// <param name="modData">A <see cref="Crc32"/> that has been initialized with data dependent on the active mods, mod versions, and mod
        /// settings.</param>
        /// <param name="progress">An <see cref="IProgress{T}"/> that receives two strings describing the current loading state.</param>
        /// <param name="hash">If not <see langword="null"/>, the calculated hash that was used to locate the cache file. Pass this to
        /// <see cref="WriteCSharp"/> if the read fails. If <see langword="null"/>, do not attempt to write a cache file.</param>
        /// <returns><see langword="true"/> if the data was successfully loaded from the cache file, or <see langword="false"/> if the CRC did not
        /// match or an error occurred.</returns>
        public static bool ReadCSharp(Crc32 modData, IProgress<(string, string)> progress, out uint? hash) {
            hash = null;
            try {
                CSharpCache cache = new();
                using var stream = cache.OpenReadCache(modData, out hash);
                if (stream == null) {
                    // OpenReadCache already logged the problem.
                    return false;
                }

                return cache.Read(stream, progress);
            }
            catch (Exception ex) {
                logger.Information(ex, "Could not load data cache.");
                return false;
            }
        }

        /// <summary>
        /// Writes icon data from <see cref="IconCollection"/> and <see cref="FactorioObject.iconId"/> into the appropriate cache file.
        /// </summary>
        /// <param name="hash">The hash that was received from <see cref="ReadIcons"/>.</param>
        public static Action WriteIcons(uint hash) => () => {
            try {
                IconCache cache = new(Database.objects.all);
                using var stream = cache.OpenWriteCache(hash);
                cache.Write(stream);
                // We might have created a new file. Check to see if we need to delete any old ones.
                cache.DeleteOldFiles();
            }
            catch (Exception ex) {
                logger.Warning(ex, "Could not write icon cache.");
            }
        };

        /// <summary>
        /// Tries to load icon data from the appropriate cache file into <see cref="IconCollection"/> and <see cref="FactorioObject.iconId"/>.
        /// </summary>
        /// <param name="modData">A <see cref="Crc32"/> that has been initialized with data dependent on the active built-in mods.</param>
        /// <param name="objects">The <see cref="FactorioObject"/>s that will receive the loaded icon data. (<see cref="Database.objects"/> may not
        /// have been initialized yet.)</param>
        /// <param name="progress">An <see cref="IProgress{T}"/> that receives two strings describing the current loading state.</param>
        /// <param name="hash">If not <see langword="null"/>, the calculated hash that was used to locate the cache file. Pass this to
        /// <see cref="WriteIcons"/> if the read fails. If <see langword="null"/>, do not attempt to write a cache file.</param>
        /// <returns><see langword="true"/> if the data was successfully loaded from the cache file, or <see langword="false"/> if the CRC did not
        /// match or an error occurred.</returns>
        public static bool ReadIcons(Crc32 modData, IReadOnlyList<FactorioObject> objects, IProgress<(string, string)> progress, out uint? hash) {
            hash = null;
            try {
                IconCache cache = new(objects);
                using var stream = cache.OpenReadCache(modData, out hash);
                if (stream != null) {
                    return cache.Read(stream, progress);
                }
            }
            catch (Exception ex) {
                logger.Information(ex, "Could not load icon cache.");
                IconCollection.ClearCustomIcons();
            }
            return false;
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
        private string GetCacheFile(uint hash) => Path.Combine(GetCacheDirectory(), $"{hash:X8}.{FileExtension}");

        /// <summary>
        /// Opens a cache file for write, and returns a stream that will accept the cached data.
        /// </summary>
        /// <param name="hash">The hash used for file lookup and cache coherency checks.</param>
        /// <returns>A stream that can store data in the appropriate file.</returns>
        private CompressAndStampStream OpenWriteCache(uint hash) {
            // This will throw if the cache is open in any other process, either for read or write.
            FileStream file = File.Open(GetCacheFile(hash), FileMode.Create, FileAccess.Write, FileShare.Read);
            file.Write(BitConverter.GetBytes(DataVersion + CONTAINER_VERSION));
            file.Write(BitConverter.GetBytes(DateTime.UtcNow.ToBinary()));
            return new(hash, file);
        }

        /// <summary>
        /// Attempts to open a cache file for read, and returns a stream that contains the previously cached data.
        /// This may throw or return <see langword="null"/>
        /// </summary>
        /// <param name="partialHash">The partially computed hash of mod data, that will have cache-specific data appended.</param>
        /// <param name="hash">When this method returns, the fully-computed hash used for cache lookup and coherency.</param>
        /// <returns>A stream that contains the previously cached data. If the crc written by <see cref="OpenWriteCache"/> is not present at the end
        /// of this stream, it will <b>throw when closed</b>.</returns>
        private DecompressAndVerifyStream? OpenReadCache(Crc32 partialHash, out uint? hash) {
            hash = FinishHash(partialHash.Clone());
            // This will throw if the cache is open in any other process for write.
            FileStream? file = File.Open(GetCacheFile(hash.Value), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

            try { // conditional using block; dispose file if something fails before we transfer ownership
                Span<byte> bytes = stackalloc byte[4];
                if (file.Read(bytes) != 4 || BitConverter.ToUInt32(bytes) != DataVersion + CONTAINER_VERSION) {
                    logger.Information($"Cache file has unrecognized data version {BitConverter.ToUInt32(bytes)}.");
                    return null;
                }

                file.Seek(-4, SeekOrigin.End);
                if (file.Read(bytes) != 4 || BitConverter.ToUInt32(bytes) != hash) {
                    logger.Information($"Cache file is missing its terminating uncompressed CRC.");
                    return null;
                }

                file.Position = 12;

                DecompressAndVerifyStream result = new(hash.Value, file);
                file = null; // ownership has been transfered to result.
                return result;
            }
            finally {
                file?.Dispose();
            }
        }

        /// <summary>
        /// Delete old files in the <see cref="GetCachePath">cache path</see>. Files are 'old' if there are at least <see cref="MAX_CACHE_COUNT"/>
        /// files with the same extension and newer last-accessed times. Files are also old if <see cref="DataVersion"/> or
        /// <see cref="CONTAINER_VERSION"/> has changed since they were written.
        /// </summary>
        protected void DeleteOldFiles() {
            PriorityQueue<string, ulong> queue = new();
            foreach (string file in Directory.EnumerateFiles(GetCacheDirectory(), $"*.{FileExtension}")) {
                try {
                    using BinaryReader reader = new(File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));

                    if (reader.ReadInt32() != DataVersion + CONTAINER_VERSION) {
                        // Delete files with an invalid version; they won't be useful unless you roll back to an older version of YAFC.
                        // The delete works even though we still have the file open; FileShare.Delete allows deletion before we close the file.
                        // It'll fail in the unlikely case that another process has the file open.
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

        private sealed class CompressAndStampStream(uint crc, FileStream file) : Stream {
            private bool _disposed;
            private readonly LZipStream lZip = LZipStream.Create(file, CompressionMode.Compress, true);
            public override bool CanRead => lZip.CanRead;
            public override bool CanSeek => lZip.CanSeek;
            public override bool CanWrite => lZip.CanWrite;
            public override long Length => lZip.Length;
            public override long Position { get => lZip.Position; set => lZip.Position = value; }

            public override void Flush() => lZip.Flush();
            public override int Read(byte[] buffer, int offset, int count) => lZip.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => lZip.Seek(offset, origin);
            public override void SetLength(long value) => lZip.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => lZip.Write(buffer, offset, count);

            protected override void Dispose(bool disposing) {
                if (disposing && !_disposed) {
                    // Write the CRC to both the compressed and non-compressed streams, ignoring errors.
                    try {
                        lZip.Write(BitConverter.GetBytes(crc));
                    }
                    catch { }
                    lZip.Dispose();

                    try {
                        file.Write(BitConverter.GetBytes(crc));
                    }
                    catch { }
                    file.Dispose();

                    _disposed = true;
                }
                base.Dispose(disposing);
            }
        }

        private sealed class DecompressAndVerifyStream(uint crc, FileStream file) : Stream {
            private bool _disposed;
            private readonly LZipStream lZip = LZipStream.Create(file, CompressionMode.Decompress, true);
            public override bool CanRead => lZip.CanRead;
            public override bool CanSeek => lZip.CanSeek;
            public override bool CanWrite => lZip.CanWrite;
            public override long Length => lZip.Length;
            public override long Position { get => lZip.Position; set => lZip.Position = value; }

            public override void Flush() => lZip.Flush();
            public override int Read(byte[] buffer, int offset, int count) => lZip.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => lZip.Seek(offset, origin);
            public override void SetLength(long value) => lZip.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => lZip.Write(buffer, offset, count);

            protected override void Dispose(bool disposing) {
                bool crcMismatch = false;
                if (disposing && !_disposed) {
                    Span<byte> bytes = stackalloc byte[4];
                    try {
                        crcMismatch = lZip.Read(bytes) != 4 || BitConverter.ToUInt32(bytes) != crc;
                    }
                    catch {
                        crcMismatch = true;
                    }
                    lZip.Dispose();

                    if (!crcMismatch) {
                        try {
                            file.Position = 4;
                            file.Write(BitConverter.GetBytes(DateTime.UtcNow.ToBinary())); // Update the atime (ignoring errors)
                        }
                        catch { }
                    }
                    file.Dispose();

                    _disposed = true;
                }

                base.Dispose(disposing);

                if (crcMismatch) {
                    throw new Exception("Compressed cache data did not end with with the expected CRC.");
                }
            }
        }
    }

    private sealed class CSharpCache : Cache {
        // The C# cache data is:
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

        // Increment the version when a change to FactorioDataDeserializer changes "Lua is loaded successfully but incorrectly" to  "Lua is loaded
        // successfully and correctly", and the change is not accompanied by a change to the Mod-fixes folder or the types, fields, or
        // loadDataParameters static fields.
        // If the Lua previously didn't load at all, the version should not be updated.
        protected override int DataVersion => 0;
        protected override string FileExtension => "csharpcache";

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
        protected override uint FinishHash(Crc32 hash) {
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

        protected override void Write(Stream cacheStream) {
            using BinaryWriter writer = new(cacheStream);

            // Write the type of each object.
            foreach (FactorioObject obj in Database.objects.all) {
                int index = typeDictionary[obj.GetType()];
                // If this fires, we have at least 256 concrete types derived from FactorioObject. Should some of them be abstract or removed?
                // If not, read and write ushorts instead of bytes. The hash will change, so DataVersion does not need to be updated.
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
        }

        protected override bool Read(Stream cacheStream, IProgress<(string, string)> progress) {
            using BinaryReader reader = new(cacheStream);
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
    }

    private sealed class IconCache(IReadOnlyList<FactorioObject> objects) : Cache {
        // The icon cache data is:
        // The iconId for each object. Non-zero IDs are offset so they start at 1, not Icon.FirstCustom. (Changing the value of FirstCustom will not
        //     invalidate the cache).
        // The number of custom icons.
        // A list of icons, encoded as WW HH followed by the 32-bit value for each pixel. All icons are currently 32x32, meaning 4098 bytes per icon.
        // The icon IDs and icon count use .NET's 7-bit integer encoding.

        // Increment the version whenever FactorioDataDeserializer.RenderIcons gets a rendering fix.
        protected override int DataVersion => 0;
        protected override string FileExtension => "iconcache";

        /// <summary>
        /// Computes the icon specification hash.
        /// </summary>
        /// <returns>The fully-computed hash to be used for file lookup and cache coherency checks.</returns>
        protected override uint FinishHash(Crc32 hash) {
            byte[] empty = [0];
            HashSet<string> seenImages = [];

            foreach (var obj in objects) {
                if (obj.iconSpec?.Length > 0) {
                    foreach (var layer in obj.iconSpec) {
                        hash.Append(BitConverter.GetBytes(layer.a));
                        hash.Append(BitConverter.GetBytes(layer.r));
                        hash.Append(BitConverter.GetBytes(layer.g));
                        hash.Append(BitConverter.GetBytes(layer.b));
                        hash.Append(BitConverter.GetBytes(layer.x));
                        hash.Append(BitConverter.GetBytes(layer.y));
                        hash.Append(BitConverter.GetBytes(layer.size));
                        hash.Append(BitConverter.GetBytes(layer.scale));
                        hash.Append(Encoding.UTF8.GetBytes(layer.path));

                        var (mod, path) = ResolveModPath("", layer.path);
                        if (mod is not "core" and not "base" and not "quality" and not "elevated-rails" and not "space-age" and not "."
                            && seenImages.Add(layer.path)) {

                            var (size, crc) = getImageData(mod, path);
                            hash.Append(BitConverter.GetBytes(size));
                            hash.Append(BitConverter.GetBytes(crc));
                        }
                    }
                }
                else {
                    hash.Append(empty);
                }
            }

            return hash.GetCurrentHashAsUInt32();

            static (long, uint) getImageData(string modName, string path) {
                if (allMods.TryGetValue(modName, out var mod) && mod.zipArchive?.GetEntry(mod.folder + path) is { } entry) {
                    return (entry.Length, entry.Crc32);
                }

                // It's not a zip or the mod/image is missing. Let ReadModFile try a filesystem read, or fall back to an empty array.
                byte[] bytes = ReadModFile(modName, path);
                return (bytes.Length, Crc32.HashToUInt32(bytes));
            }
        }

        protected override unsafe void Write(Stream stream) {
            using BinaryWriter writer = new(stream);

            Dictionary<int, FactorioId> iconIdIndex = [];
            foreach (var obj in objects) {
                if (obj.iconId == 0) {
                    writer.Write((byte)0);
                }
                else {
                    writer.Write7BitEncodedInt(obj.iconId - (int)Icon.FirstCustom + 1);
                }
            }

            writer.Write7BitEncodedInt(IconCollection.IconCount - (int)Icon.FirstCustom);
            for (int i = (int)Icon.FirstCustom; i < IconCollection.IconCount; i++) {
                nint surface = IconCollection.GetIconSurface((Icon)i);
                ref var sdlSurface = ref RenderingUtils.AsSdlSurface(surface);
                if (SDL.SDL_MUSTLOCK(surface)) {
                    throw new Exception("MUSTLOCK surfaces are not supported for icon caching.");
                }

                checked {
                    writer.Write((byte)sdlSurface.w);
                    writer.Write((byte)sdlSurface.h);
                }

                ref var format = ref Unsafe.AsRef<SDL.SDL_PixelFormat>((void*)sdlSurface.format);
                if (format.BytesPerPixel != 4) {
                    throw new Exception("Non-RGBA surfaces are not supported for icon caching.");
                }
                writer.Write(new ReadOnlySpan<byte>((void*)sdlSurface.pixels, sdlSurface.w * sdlSurface.h * format.BytesPerPixel));
            }
        }

        protected override unsafe bool Read(Stream stream, IProgress<(string, string)> progress) {
            using BinaryReader reader = new(stream);
            foreach (var obj in objects) {
                int id = reader.Read7BitEncodedInt();
                if (id > 0) {
                    obj.iconId = id + (int)Icon.FirstCustom - 1;
                }
            }

            int iconCount = reader.Read7BitEncodedInt();
            IconCollection.ClearCustomIcons();

            for (int i = 0; i < iconCount; i++) {
                if (i % 1000 == 0) {
                    progress.Report((LSs.ProgressLoadingCacheIcons, LSs.ProgressRenderingXOfY.L(i, iconCount)));
                }
                int width = reader.ReadByte();
                int height = reader.ReadByte();

                nint surface = SDL.SDL_CreateRGBSurfaceWithFormat(0, width, height, 0, SDL.SDL_PIXELFORMAT_RGBA8888);
                IconCollection.AddIcon(surface);

                ref var sdlSurface = ref RenderingUtils.AsSdlSurface(surface);
                ref var format = ref Unsafe.AsRef<SDL.SDL_PixelFormat>((void*)sdlSurface.format);
                int expectedBytes = width * height * format.BytesPerPixel;
                if (reader.Read(new Span<byte>((void*)sdlSurface.pixels, expectedBytes)) != expectedBytes) {
                    logger.Information("Unexpected end of data reading icon cache.");
                    return false;
                }
            }

            return true;
        }
    }
}
