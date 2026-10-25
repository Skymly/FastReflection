# One exception contract for emit and reflection completion

One contract. Emit completion and reflection completion must produce the same success value, the same write result, and the same exception type. Existing differences are contract violations, not a documented second behavior. The contract promises the exception type and the trigger condition only, not message text, `ParamName`, or localized text. When several trigger conditions hold on the same call, the Trigger order section selects the first that holds. That order is part of the contract.

## Lookup

- A missing name, including the empty string, a case mismatch, or a name absent from this visibility, is `MissingMemberException`.
- A Member that exists but cannot be read or written is `InvalidOperationException`. A read-only field and a `ref readonly` property both count as not writable.
- An ordinal outside this type accessor is `ArgumentOutOfRangeException`.
- The three name-lookup forms: `Get*` turns a miss into null, `Try*` turns a miss into false, and `Require*` throws `MissingMemberException` on a miss. Those three families change only the miss outcome. A conversion failure, an illegal target, a wrong argument count, and a callee exception are not swallowed. `Require*` throws that operation's own exception, with no extra wrapper.
- `Get*` means only a name-lookup member that returns a member, such as `GetOrdinal` or `GetMember`. Value members — the indexer, `GetValue`, and `SetValue` — are not `Get*`. They resolve a name with Require semantics, so a miss remains `MissingMemberException`.
- A null `Type`, `MemberInfo`, or name, including `TryGetOrdinal(null)`, is `ArgumentNullException`.

## Target

- A null target on an instance access is `ArgumentNullException`. A runtime type that is not that type, a subtype, or an implementation of it is `InvalidCastException`.
- A static access has no target. Null or a wrong type is ignored.
- An object accessor forwards to its type accessor and throws the same exceptions.

## Value

- A successful conversion is the CLR `castclass` / `unbox.any` conversion: reference assignment, `Nullable<T>` and `T` in either direction, an enum and its underlying integral type, and enums that share that underlying type. No numeric widening or narrowing.
- A by-value argument, or a member-write value, that cannot be converted, including null written to a non-nullable value type, throws `InvalidCastException`. The callee is not invoked. Nothing is written.
- A non-null value in a `ref`, `in`, or `out` slot is converted by that same rule before the call. Conversion failure throws `InvalidCastException`. The callee is not invoked. Nothing is written back.
- A null slot for `ref`, `out`, or `in` is not a conversion failure. The call proceeds, and the local starts at `default(T)`. Only `ref` and `out` write back. `in` does not write back. If the callee throws, nothing is written back.
- An exception thrown by the callee — a method, a property or field accessor, or a constructor — propagates unwrapped, not inside `TargetInvocationException`. A library `InvalidCastException` and a callee `InvalidCastException` are therefore indistinguishable. That limit is accepted.

## Arguments

- A null `object[]` is `ArgumentNullException`. The length must equal the parameter count. Too many or too few is `ArgumentException`. Constructor invokers follow the same rule.
- `Span<object?>` uses the same length rule. The span itself is never null.

## Creating an access

- An open generic, a missing getter, or a create-write of a read-only field or a `ref readonly` property is `ArgumentException`.
- An indexer property is `NotSupportedException`. Call it through `CreateInvoker` on the accessor method.
- A mutable ref-returning property with no setter can be given a write access when dynamic code is available. That does not make it a writable Member. When dynamic code is unavailable, creation throws `NotSupportedException`. On the type accessor, `CanWrite` stays false, and `SetValue` still throws `InvalidOperationException`.
- `CreateFactory` failing to produce a factory is `ArgumentException`. `CreateNew` when `CreateNewSupported` is false is `InvalidOperationException`. A value type's default value is not an access and does not throw.
- A typed-delegate mismatch, or `Ref<T>` passed by value, is `ArgumentException`.
- This contract does not decide whether a ref struct, a pointer, or `TypedReference` enters 1.0. If one of those is ultimately unsupported, creation throws `NotSupportedException`. Do not disguise that as a conversion failure.

## Trigger order

When several conditions hold on the same call, take the first that holds. Both implementations agree.

Creation:

1. A null parameter → `ArgumentNullException`.
2. An unsupported item — an indexer, or a ref struct, pointer, or `TypedReference` if unsupported — → `NotSupportedException`.
3. An open generic, no corresponding accessor, a create-write of a read-only or `ref readonly` member, cannot create, a typed-delegate mismatch, or `Ref<T>` passed by value → `ArgumentException`.
4. Dynamic code is required and unavailable → `NotSupportedException`.

Call:

1. Name and ordinal resolution: a null name → `ArgumentNullException`; a missing name → `MissingMemberException`; an ordinal out of range → `ArgumentOutOfRangeException`.
2. The Member cannot be read or written → `InvalidOperationException`.
3. `args` is null → `ArgumentNullException`; the length does not match → `ArgumentException`.
4. `target` is null → `ArgumentNullException`; the runtime type does not match → `InvalidCastException`.
5. Per-slot and member-write conversion → `InvalidCastException`.
6. The callee's own exception → propagates unwrapped, and `ref` / `out` are not written back.

## Documentation

`<exception>` is written on the member the caller actually invokes. A factory method states what the returned delegate throws when invoked. A README may repeat this. It does not replace the XML.

## Continuity, not amendments

- This trigger order governs access creation, access invocation, and type-accessor and object-accessor operations. `Unreflect` and the invocation of an exact-bind delegate are not this order. They remain `CreateDelegate` or a direct call, as decided by the concept-model ticket. This contract does not reverse that.
- Call step 4 applies to an instance access only. A static access ignores its target, as the Target section says.
- A mutable ref-return write with no setter is creation step 4, not step 3. Step 3's "no accessor" is a missing getter, a read-only field, or `ref readonly`.
- The concept-model rule that `Get*` returns null on a miss applies to name-lookup members. `GetValue`, `SetValue`, and the indexer are not `Get*`.

## Checked, and therefore defects against this contract

Probed on net8.0 and net472. Both paths must change. These fixes wait for the public-API spec.

- Emit writes `int` to `long` as `InvalidCastException`, writes null to `int` as `NullReferenceException` without writing, throws `NullReferenceException` for a null instance target, leaves callee exceptions unwrapped, and ignores extra arguments.
- Reflection completion widens `int` to `long`, writes 0 for null to `int`, throws `ArgumentException` on narrowing, wraps callee exceptions in `TargetInvocationException`, throws `TargetException` for a null or wrong-type target, throws `TargetParameterCountException` for a null, short, or long argument array, and writes a boxed default back into a null `in` slot.
- Both paths throw `MissingMemberException` for an unwritable Member. The message looks like "not found" and is localized. A ref-return write with no setter throws `ArgumentException` when dynamic code is unavailable.

Required observable fixes:

- A null instance target throws `ArgumentNullException`.
- Null written to a non-nullable value type throws `InvalidCastException` and does not write.
- Widening fails with `InvalidCastException` and does not write. Narrowing's `ArgumentException` becomes `InvalidCastException`.
- A callee exception is not wrapped.
- A null argument array throws `ArgumentNullException`. A wrong length throws `ArgumentException`. Extra arguments are not ignored.
- An unwritable Member throws `InvalidOperationException`.
- A null `in` slot is not written back.
- Creating a ref-return write with no setter, when dynamic code is unavailable, throws `NotSupportedException`.
