# CESDK1003: A Cheat Engine-owned value is being destroyed

`[CEOwned]` is a borrowed-ownership annotation. A return value, property, or parameter carrying it is owned by Cheat
Engine, not by the plugin. Calling `Dispose()` or `DisposeAsync()` directly on that value can leave the host with a
dangling object.

Keep the value borrowed, or use an API that explicitly transfers ownership into `Owned<T>`. The rule deliberately
checks only direct provenance: an annotated parameter, property access, or method return used immediately as the
receiver. It does not guess ownership through locals, fields, or arbitrary helper methods.
