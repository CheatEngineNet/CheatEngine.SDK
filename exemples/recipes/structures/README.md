<div align="center">

# Recipe · Structure definitions

**A deliberately deferred recipe: do not manufacture ownership for Cheat Engine structures.**

**Level** `Advanced` · **Time** `5 min` · **Needs** the Lua surface catalogue

[Examples index](../../README.md) · [Recipes](../README.md)

</div>

---

## Status

Cheat Engine documents Lua entry points such as `createStructure`, `getStructure`, and
`addToGlobalStructureList`. That documentation alone does not establish the full public SDK contract required for this
recipe: ownership before and after insertion, deterministic destruction, rollback after a partially-created structure,
thread affinity, and the behaviour of nested element objects.

Consequently, **there is no public `Structure` factory in this SDK slice**. This page intentionally does not show a
raw Lua call converted into an ownership wrapper, and it does not prescribe any ownership-transfer pattern. A handle is
not proof that the plugin may destroy its object.

## What to use today

- Use the typed Engine factories only when they explicitly return an owner—for example,
  `AobScanner.TryScan` and `StringLists.TryCreate` return `Owned<StringList>` because their CE ownership contracts are
  recorded.
- Treat an object obtained from a host/global-list API as borrowed unless its API says otherwise. Do not call
  `Dispose` on it and do not wrap it in `Owned<T>`.
- Keep raw Lua as an advanced escape hatch for a separately authorized, source-backed experiment; it is not a normal
  plugin recipe and cannot make ownership safe by convention.

The existing address-list slice demonstrates the safe borrowed form: `AddressListAccess.TryGetCurrent` and its
`MemoryRecord` results remain owned by Cheat Engine. See [07 · The address list](../../07-address-list/README.md).

## What a future structure slice must prove

Before this recipe can return with code, its Engine factory and documentation must record all of the following against
the exact CE build:

| Contract  | Required proof before a public API                                                                                                     |
|-----------|----------------------------------------------------------------------------------------------------------------------------------------|
| Creation  | Exact global name, arguments, return/null/error form, and protected-call behaviour                                                     |
| Ownership | Whether a fresh structure is caller-owned, and exactly when adding it to a CE list transfers or retains ownership                      |
| Failure   | What CE leaves behind after a failed element update or failed insertion, with a rollback rule that does not destroy a host-owned value |
| Threading | Whether the operation requires the captured enable/GUI boundary, and how it behaves during disable                                     |
| Lifetime  | A factory-issued `Owned<T>` only if deterministic destruction is proven; otherwise a borrowed handle or no API                         |
| Tests     | Fixture tests for stack/cleanup plus an isolated, opt-in CE 7.7 live probe                                                             |

That work belongs to the evidence and capability process, not to a recipe that guesses from an object pointer. Its
disposition belongs to the deferred families of the [Lua surface catalogue](../../../docs/catalog/README.md).

## Before you move on

- [ ] Do not construct `Owned<T>` from a structure handle in consumer code.
- [ ] Do not infer a transfer or rollback rule from the presence of a Lua method named `addToGlobalStructureList`.
- [ ] Keep any future structure feature behind an explicit capability, ownership, and live-test contract.

---

<div align="center">

[Examples index](../../README.md) · [Recipes](../README.md)

</div>
