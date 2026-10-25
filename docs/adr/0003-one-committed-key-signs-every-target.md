# One committed key signs every shipped target

Every shipped assembly is strong-named with one key pair committed to source control, on every target framework. The key is the assembly identity, not a security boundary; publisher identity stays with Authenticode and NuGet signing at release. After the first publish the key never changes and the `FastReflection` assembly name never changes — on .NET Framework either is a runtime `FileLoadException` for compiled consumers, and a major bump does not license it.

`AssemblyVersion` is the package major only — `1.0.0.0` across all of 1.x, bumped only at a major release. `FileVersion` carries the full package version; `InformationalVersion` stays with SourceLink. `<Version>` remains the SemVer source of truth and stops driving `AssemblyVersion`. .NET Framework binds strong-named assemblies exactly, so a package-version `AssemblyVersion` would force a binding redirect on every minor and patch.

The test assembly signs with the same key on all legs, and the `InternalsVisibleTo` item names the full public key through its `Key` metadata. Any new friend assembly signs and keys the same way. A satellite package signs with the same key pair on all of its targets and, per ADR-0002, receives no `InternalsVisibleTo`; one that ever needs internals reopens this through a new ADR. A satellite compiled against the signed core pins the core's key token and `AssemblyVersion`, and major-only versioning keeps that pin valid across minors.

No strong-named and unsigned twin of the package is ever published; a `*.Signed` package id is not a satellite. The emit path needs no friend dynamic assembly — it uses `DynamicMethod` with `skipVisibility`, and no `AssemblyBuilder` exists in `src`.

## Considered options

- Ship unsigned. Rejected: the targets include .NET Framework and .NET Standard, where strong naming is viral — unsigned excludes strong-named consumers, which is the audience net462 exists for. Microsoft's library guidance says to sign every target framework in that case.
- Public-sign, or commit only the public key. Rejected for shipped binaries: on .NET Framework a public-signed assembly is supported for debugging and testing only, and Dapper.StrongName 2.1.86 shipped one by mistake and failed consumers with 0x80131045.
- Keep the key uncommitted in CI secrets. Rejected: the key is identity, not a secret; an uncommitted key can be lost, which kills the identity, and it buys no protection while costing a CI secret.
- Ship a `Skymly.FastReflection.Signed` twin beside an unsigned core. Rejected: the guidance forbids twins, referencing both fails with CS0433, and the only thing a twin avoids — changing a published unsigned identity — does not exist, since nothing has been published.
- `AssemblyVersion` equal to the full package version (Namotion.Reflection, DotNext, Dynamitey). Rejected: every minor and patch would force .NET Framework consumers to add a binding redirect.
- A permanently fixed `AssemblyVersion`. Rejected: the guidance says not to freeze it, and a major release must be able to move it.

## Consequences

- The key file (for example `FastReflection.snk`) lives in the repo; `*.snk` is already not ignored while `*.pfx` stays ignored, which matches the split between assembly identity and publisher signing.
- The project files gain `SignAssembly`, `AssemblyOriginatorKeyFile`, and an `AssemblyVersion` property; these mechanics are implementation at the spec or quality-gate hop, not part of this decision's landing.
- Signing the library breaks the current unsigned `InternalsVisibleTo` grant with CS1726 until the test project signs and the item carries `Key=`; that is a required consequence, not a side effect to avoid.
- Reproducible builds are unaffected: `Deterministic` and `ContinuousIntegrationBuild` already stand, the committed key adds no secrets, and symbol packages are unchanged. The quality-gate spec takes a packed-assembly identity check — strong-name flag set and one public key token across every packed assembly — as input.
- After the first publish, adding, removing, or changing the key, renaming the assembly, or shipping a twin is a breaking change with no acceptable vehicle; the practical rule is never.
