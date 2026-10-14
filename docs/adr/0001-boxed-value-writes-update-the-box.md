# Writes to a boxed value type update that box

An access or a type accessor writes through the boxed value the caller passed, so the write is still there after the call. Copy-out semantics were rejected: they match a naive unbox, and they silently drop the write, which is what fast-member did.

## Considered Options

- Update the box. Chosen, because the caller handed over that value and expects the write to stick.
- Copy the value, mutate the copy, and leave the box unchanged. Rejected, because the failure is silent.