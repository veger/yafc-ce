using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SDL2;

namespace Yafc.UI;

/// <summary>
/// A base class for resources that are cached between render cycles.
/// </summary>
/// <typeparam name="T">The concrete derived type of the cached resource.</typeparam>
/// <typeparam name="TKey">The type from which to derive and uniquely identify the resource.</typeparam>
/// <example>
/// <code>
/// public sealed class CacheableItem : ImGuiCache<CacheableItem, string> {
///     public string DerivedData { get; }
///
///     private CacheableItem(string key) {
///         // Perform expensive operations here to derive the resource from the key.
///         DerivedData = $"Processed_{key}";
///     }
///
///     protected override CacheableItem CreateForKey(string key) => new CacheableItem(key);
///
///     public override void Dispose() {
///         // Cleanup resources when the item is purged.
///     }
/// }
///
/// // Usage during a render frame:
/// var item = itemCache.GetCached("my_key");
/// // item.DerivedData will be "Processed_my_key"
///
/// // At the end of the render frame:
/// itemCache.PurgeUnused(); // Disposes items that were not retrieved this frame
/// </code>
/// </example>
public abstract class ImGuiCache<T, TKey> : IDisposable where T : ImGuiCache<T, TKey> where TKey : IEquatable<TKey> {
    private static readonly T Constructor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    /// <summary>
    /// Represents a pool of cached items for a cacheable resource type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call <see cref="GetCached"/> to return an existing item from the cache, or cache and return a new one
    /// constructed from the provided key.
    /// </para>
    /// <para>
    /// At the end of a cache cycle, call <see cref="PurgeUnused"/> to dispose of any items from the preceding cycle
    /// that were not used on the current cycle.
    /// </para>
    /// </remarks>
    public sealed class Cache : IDisposable {
        private readonly Dictionary<TKey, T> activeCached = [];
        private readonly HashSet<TKey> unused = [];

        /// <summary>
        /// Retrieves an existing item from the cache, or constructs a new one from the provided key and caches it.
        /// </summary>
        public T GetCached(TKey key) {
            if (activeCached.TryGetValue(key, out var value)) {
                _ = unused.Remove(key);
                return value;
            }

            return activeCached[key] = Constructor.CreateForKey(key);
        }

        /// <summary>
        /// Disposes of any cached items that were not accessed via <see cref="GetCached"/> during the current cycle.
        /// </summary>
        public void PurgeUnused() {
            foreach (var key in unused) {
                if (activeCached.Remove(key, out var value)) {
                    value.Dispose();
                }
            }
            unused.Clear();
            unused.UnionWith(activeCached.Keys);
        }

        /// <summary>
        /// Disposes of all items in the cache and clears the pool.
        /// </summary>
        public void Dispose() {
            foreach (var item in activeCached) {
                item.Value.Dispose();
            }

            activeCached.Clear();
            unused.Clear();
        }
    }

    protected abstract T CreateForKey(TKey key);
    public abstract void Dispose();
}

public sealed class TextCache : ImGuiCache<TextCache, (FontFile.FontSize size, string text, uint wrapWidth)>, IRenderable {
    public TextureHandle texture;
    private IntPtr surface;
    internal SDL.SDL_Rect texRect;
    private SDL.SDL_Color curColor = RenderingUtils.White;

    private TextCache((FontFile.FontSize size, string text, uint wrapWidth) key) {
        surface = key.wrapWidth == uint.MaxValue
            ? SDL_ttf.TTF_RenderUNICODE_Blended(key.size.handle, key.text, RenderingUtils.White)
            : SDL_ttf.TTF_RenderUNICODE_Blended_Wrapped(key.size.handle, key.text, RenderingUtils.White, key.wrapWidth);

        ref var surfaceParams = ref RenderingUtils.AsSdlSurface(surface);
        texRect = new SDL.SDL_Rect { w = surfaceParams.w, h = surfaceParams.h };
    }

    protected override TextCache CreateForKey((FontFile.FontSize size, string text, uint wrapWidth) key) => new TextCache(key);

    public override void Dispose() {
        if (surface != IntPtr.Zero) {
            SDL.SDL_FreeSurface(surface);
            surface = IntPtr.Zero;
        }

        texture = texture.Destroy();
    }

    public void Render(DrawingSurface surface, SDL.SDL_Rect position, SDL.SDL_Color color) {
        if (!texture.valid || texture.surface != surface) {
            texture = texture.Destroy();
            texture = surface.CreateTextureFromSurface(this.surface);
            curColor = RenderingUtils.White;
        }

        if (color.r != curColor.r || color.g != curColor.g || color.b != curColor.b) {
            _ = SDL.SDL_SetTextureColorMod(texture.handle, color.r, color.g, color.b);
        }

        if (color.a != curColor.a) {
            _ = SDL.SDL_SetTextureAlphaMod(texture.handle, color.a);
        }

        curColor = color;
        _ = SDL.SDL_RenderCopy(surface.renderer, texture.handle, ref texRect, ref position);
    }
}
