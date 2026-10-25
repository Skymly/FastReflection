# Public shape reuses the existing concepts

1.0 does not pre-admit a glossary noun. A scope ticket may mark a capability In only when its contract is true in the existing language — Access, Closed type, Member, Ordinal, Type accessor, Object accessor, and Ref — or when that ticket admits a new noun in the same change. Admission requires that an existing term would otherwise become false, that the noun is not a general programming concept, and that no existing definition is rewritten. A live reference return would rewrite Access, so it cannot be In. New public API follows the shape below. A write still updates the boxed value the caller passed (ADR-0001); a write that cannot do that fails instead of dropping the change.

## Shape

Extension methods exist only on `MethodInfo`, `ConstructorInfo`, `PropertyInfo`, `FieldInfo`, and `Type`. `Type` gains no further extension beyond `CreateFactory`. There is no extension on `object`, `string`, `Delegate`, or `MemberInfo`. A satellite does not add public types to the `FastReflection` namespace and does not receive `InternalsVisibleTo`. Anything that is not "adapt this already-held reflection object" is a type obtained from `Create`. The core has one public namespace, `FastReflection`.

`Create*` builds an access or an accessor and does not look up by name. Two 0.1 methods are grandfathered and must not be copied: `CreateInvoker<TDelegate>` prefers an exact bind and falls back to an access; `CreateFactory(Type)` is an access over a parameterless constructor, or a value-type default, which is not an access. `Unreflect` stays the exact bind. It is not renamed and it gains no synonym. `Get*` returns null on a miss and does not throw for that miss. `Require*` throws when missing. `Try*` is the bool form. The indexer is not a `Get`. Which exception that throw uses, and how coercion and `TargetInvocationException` behave, stay with the exception-model decision.

A public member exists on every shipped target framework, or it needs a BCL type that exists on only one framework and its name says so, as `CreateSpanInvoker` does. It must not be the only way to do something the other frameworks can already do. Which frameworks ship is a later decision.

Callers cannot subclass a type accessor or an object accessor. Before the 1.0 freeze those types get a constructor that derived code outside this assembly cannot call. They stay abstract, and `Create` remains the only way to obtain one. `Ref<T>` keeps a public constructor. `Member` stays sealed.

## Thread safety

`TypeAccessor.Create`, a published type accessor, an object accessor, and an access delegate are safe for concurrent callers. The cache may build a discarded duplicate; callers observe only the published instance. The library does not lock the target. There is no public cache flush. The cache holds `Type` strongly. This decision does not promise unload.

## Freeze fixes

`ObjectAccessor.Equals` is symmetric. An accessor equals another accessor when their targets are equal, and it does not equal the raw target. `GetHashCode` follows that. `ToString` may still forward to the target. The 0.1 test that asserts `Equals` against the raw target is not the 1.0 contract. Nothing has been published, so there is no compatibility shim.

## Considered options

- Pre-admit a noun for each reference library. Rejected: that recreates four libraries inside one glossary, which is what 1.0 is pruning.
- Split `CreateInvoker<TDelegate>` so that `Create*` is always an access. Rejected: the exact bind is what still works when dynamic code is unavailable, and `Unreflect` already names that path.
- Freeze the `Equals` asymmetry because 0.1 tests it. Rejected: it breaks the `Equals` contract, and nothing has been published.
- Promise a weak cache here. Rejected: unload is a separate investigation, and a flush races.

## Consequences

A scope ticket that cannot state its contract in the existing language, and that cannot admit a noun under the test above, does not say In. It may still be Satellite, post-1.0, or wontfix. A new public type does not by itself require a glossary noun. `CONTEXT.md` is unchanged by this decision; non-goal sentences stay with the scope tickets that own them.
