# FastReflection

Adapts a runtime-known property, field, method, or constructor into a reusable access, and names the instance properties and fields of a closed type. It does not include dynamic binding, nullability or XML-doc meaning, or compile-time generated access.

## Language

**Access**:
An adapted, reusable call over a runtime-known non-indexer property, field, method, or constructor: no target when static, otherwise a non-null target compatible by the type-accessor rule, and an object return yields a ref return's current value rather than a live reference. It is still that access when ordinary reflection completes it, and it fails when reflection cannot; a no-adaptation runtime bind, a value type's default value, and a write of a ref readonly property are not accesses.
_Avoid_: Fast path, reflection cache, dynamic call, compiled delegate, exact bind, Unreflect, factory, indexer

**Closed type**:
A type with no unbound generic parameters. An open generic type has no type accessor. An open generic method is not yet a method an access can cover.
_Avoid_: Generic type, template, open type

**Member**:
An instance property or field in one type accessor, identified by a case-sensitive name. Not a method, event, indexer, const, or static member; it remains a Member when that type accessor cannot read or write it, and writable means that type accessor can write it, including an init-only property after construction.
_Avoid_: MemberInfo, symbol, column, write-through, init-only, required

**Ordinal**:
The index of one Member in the type accessor you hold. Not a name, not shared across visibilities, and not an identity outside that type accessor; an index outside it is not a missing Member.
_Avoid_: Index, slot, position, column, schema id

**Type accessor**:
The Members of one closed type at one visibility, including Members of interfaces it extends, applied only to a non-null target whose runtime type is that type, a subtype, or an implementation of it, or to a box of that value type. Public holds the public instance properties and fields of that type and its bases; non-public also holds non-public instance members declared on that type and inherited non-private ones; a name identifies at most one Member, which declaration wins is not promised, and instance creation is not part of it.
_Avoid_: Catalog, member map, property accessor

**Object accessor**:
A type accessor, at one visibility, of one object's runtime type, bound to that object. Not a type accessor of the variable's declared type.
_Avoid_: Wrapper, instance accessor

**Ref**:
A mutable holder standing in for one by-ref parameter. An access reads and writes its value in place. It is not the parameter itself.
_Avoid_: Out slot, boxed by-ref, pointer