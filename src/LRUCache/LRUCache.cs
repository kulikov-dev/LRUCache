using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Caching
{
    /// <summary>
    /// An LRU Cache implementation
    /// </summary>
    /// <typeparam name="K"> The key type </typeparam>
    /// <typeparam name="V"> The value type </typeparam>
    /// <remarks> Supports IDisposable values </remarks>
    public class LRUCache<K, V> : IEnumerable<K>, IDisposable
    {
        /// <summary>
        /// Entries in the cache
        /// </summary>
        private readonly Dictionary<K, CacheNode> entries;

        /// <summary>
        /// The maximum number of entries in the cache
        /// </summary>
        private readonly int capacity;

        /// <summary>
        /// Linked list head
        /// </summary>
        private CacheNode head;

        /// <summary>
        /// Linked list tail
        /// </summary>
        private CacheNode tail;
        private TimeSpan ttl;
        private Timer _timer;
        private int _count;
        private bool _refreshEntries;

        /// <summary>
        /// A least recently used cache with a time to live.
        /// </summary>
        /// <param name="capacity">
        /// The number of entries the cache will hold
        /// </param>
        /// <param name="hours">The number of hours in the TTL</param>
        /// <param name="minutes">The number of minutes in the TTL</param>
        /// <param name="seconds">The number of seconds in the TTL</param>
        /// <param name="refreshEntries">
        /// Whether the TTL should be refreshed upon retrieval
        /// </param>
        public LRUCache(
            int capacity,
            int hours = 0,
            int minutes = 0,
            int seconds = 0,
            bool refreshEntries = true)
        {
            this.capacity = capacity;
            entries = new Dictionary<K, CacheNode>(this.capacity);
            head = null;
            tail = null;
            _count = 0;
            ttl = new TimeSpan(hours, minutes, seconds);
            _refreshEntries = refreshEntries;
            if (ttl > TimeSpan.Zero)
            {
                _timer = new Timer(
                    Purge,
                    null,
                    (int)ttl.TotalMilliseconds,
                    5000); // 5 seconds
            }
        }

        /// <summary>
        /// Gets the current number of entries in the cache.
        /// </summary>
        public int Count
        {
            get { return entries.Count; }
        }

        /// <summary>
        /// Gets the maximum number of entries in the cache.
        /// </summary>
        public int Capacity
        {
            get { return capacity; }
        }

        /// <summary>
        /// Gets whether or not the cache is full.
        /// </summary>
        public bool IsFull
        {
            get { return _count == capacity; }
        }

        /// <summary>
        /// Gets the item being stored.
        /// </summary>
        /// <returns>The cached value at the given key.</returns>
        public bool TryGetValue(K key, out V value)
        {
            CacheNode entry;
            value = default(V);

            if (!entries.TryGetValue(key, out entry))
            {
                return false;
            }

            if (_refreshEntries)
            {
                MoveToHead(entry);
            }

            lock (entry)
            {
                value = entry.Value;
            }

            return true;
        }

        /// <summary>
        /// Sets the item being stored to the supplied value.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The value to set in the cache.</param>
        public void Add(K key, V value)
        {
            TryAdd(key, value);
        }

        /// <summary>
        /// Sets the item being stored to the supplied value.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The value to set in the cache.</param>
        /// <returns>True if the set was successful. False otherwise.</returns>
        public bool TryAdd(K key, V value)
        {
            CacheNode entry;
            if (!entries.TryGetValue(key, out entry))
            {
                // Add the entry
                lock (this)
                {
                    if (!entries.TryGetValue(key, out entry))
                    {
                        if (IsFull)
                        {
                            // Re-use the CacheNode entry
                            (entry as IDisposable)?.Dispose();
                            entry = tail;
                            entries.Remove(tail.Key);

                            // Reset with new values
                            entry.Key = key;
                            entry.Value = value;
                            entry.LastAccessed = DateTime.UtcNow;

                            // Next and Prev don't need to be reset.
                            // Move to front will do the right thing.
                        }
                        else
                        {
                            _count++;
                            entry = new CacheNode()
                            {
                                Key = key,
                                Value = value,
                                LastAccessed = DateTime.UtcNow
                            };
                        }
                        entries.Add(key, entry);
                    }
                }
            }
            else
            {
                // If V is a nonprimitive Value type (struct) then sets are
                // not atomic, therefore we need to lock on the entry.
                lock (entry)
                {
                    entry.Value = value;
                }
            }

            MoveToHead(entry);

            // We don't need to lock here because two threads at this point
            // can both happily perform this check and set, since they are
            // both atomic.
            if (null == tail)
            {
                tail = head;
            }

            return true;
        }

        /// <summary>
        /// Removes the stored data.
        /// </summary>
        /// <returns>True if the removal was successful. False otherwise.</returns>
        public bool Clear()
        {
            lock (this)
            {
                if (typeof(IDisposable).IsAssignableFrom(typeof(V)))
                {
                    foreach (var value in entries.Values)
                    {
                        (value.Value as IDisposable)?.Dispose();
                    }
                }

                entries.Clear();
                head = null;
                tail = null;
                return true;
            }
        }

        /// <summary>
        /// Get least used cache key
        /// </summary>
        /// <returns>  The cache least used key  </returns>
        public K GetLeastUsedValue()
        {
            if (Count == 0 || tail == null)
            {
                return default;
            }

            return tail.Key;
        }

        /// <summary>
        /// Remove item from cache
        /// </summary>
        /// <param name="key"> An cache key </param>
        public void Remove(K key)
        {
            if (!entries.ContainsKey(key))
            {
                return;
            }

            Remove(entries[key]);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns> Enumerator </returns>
        public IEnumerator<K> GetEnumerator()
        {
            return entries.Keys.GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns> Enumerator </returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return entries.Keys.GetEnumerator();
        }

        /// <summary>
        /// Dispose cache
        /// </summary>
        public void Dispose()
        {
            Clear();
        }

        /// <summary>
        /// Moved the provided entry to the head of the list.
        /// </summary>
        /// <param name="entry">The CacheNode entry to move up.</param>
        private void MoveToHead(CacheNode entry)
        {
            if (entry == head)
            {
                return;
            }

            // We need to lock here because we're modifying the entry
            // which is not thread safe by itself.
            lock (this)
            {
                RemoveFromList(entry);
                AddToHead(entry);
            }
        }

        private void Purge(object state)
        {
            if (ttl <= TimeSpan.Zero || _count == 0)
            {
                return;
            }

            lock (this)
            {
                var current = tail;
                var now = DateTime.UtcNow;

                while (null != current
                    && (now - current.LastAccessed) > ttl)
                {
                    Remove(current);
                    // Going backwards
                    current = current.Prev;
                }
            }
        }

        /// <summary>
        /// Add entry to the cache list
        /// </summary>
        /// <param name="entry"> Entry </param>
        private void AddToHead(CacheNode entry)
        {
            entry.Prev = null;
            entry.Next = head;

            if (null != head)
            {
                head.Prev = entry;
            }

            head = entry;
        }

        /// <summary>
        /// Remove entry from the cache list
        /// </summary>
        /// <param name="entry"> Entry </param>
        private void RemoveFromList(CacheNode entry)
        {
            var next = entry.Next;
            var prev = entry.Prev;

            if (null != next)
            {
                next.Prev = entry.Prev;
            }
            if (null != prev)
            {
                prev.Next = entry.Next;
            }

            if (head == entry)
            {
                head = next;
            }

            if (tail == entry)
            {
                tail = prev;
            }
        }

        /// <summary>
        /// Remove entry from the cache list
        /// </summary>
        /// <param name="entry"> Entry </param>
        private void Remove(CacheNode entry)
        {
            // Only to be called while locked from Purge
            RemoveFromList(entry);
            entries.Remove(entry.Key);
            _count--;
        }

        /// <summary>
        /// Linked list entry
        /// </summary>
        private class CacheNode
        {
            /// <summary>
            /// Next entry
            /// </summary>
            public CacheNode Next { get; set; }

            /// <summary>
            /// Previous entry
            /// </summary>
            public CacheNode Prev { get; set; }

            /// <summary>
            /// Key
            /// </summary>
            public K Key { get; set; }

            /// <summary>
            /// Value
            /// </summary>
            public V Value { get; set; }

            /// <summary>
            /// Last accessed date
            /// </summary>
            public DateTime LastAccessed { get; set; }
        }
    }
}