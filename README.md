#### LRUCache
========

##### What is it?
-----------

<b>Fork.</b>
A lightweight thread-safe LRU cache for .NET.
* Added supports of IDisposable values.

##### Example Usage
-------------

``` csharp
var cache = new LRUCache<int, string>(capacity: 1000);
var key = 1;
var value = "Hello";
cache.Add(key, value);

string valueInCache;
cache.TryGetValue(key, out valueInCache);
// valueInCache is now "Hello"
```
