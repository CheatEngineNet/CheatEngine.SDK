# CESDK1001: Plugin startup code calls an enabled-only API

`[RequiresPluginEnabled]` APIs need the host binding and Lua runtime attached by `PluginHost`. That attachment occurs
after the plugin instance is constructed, so direct calls from a plugin constructor, field initializer, property
initializer, or static constructor are invalid.

Move the operation into `OnEnable`, or into a synchronous helper called by `OnEnable`. The analyzer follows real SDK
attribute symbols, including an annotated property accessor, overridden member, or annotated containing type; a
same-named local attribute does not change the contract.
