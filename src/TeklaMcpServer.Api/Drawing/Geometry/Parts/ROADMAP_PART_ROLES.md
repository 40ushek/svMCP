# Roadmap: what a part is for

## Why

Four separate problems turned out to be one. Each needs to know whether a part
defines the size of the assembly, is attached to it, or takes no part in
dimensions at all:

- the structural extent an overall dimension measures — on two real walls the
  assembly outline ran 10 mm past the last chain point, because an outer layer
  overhangs the frame;
- `StructuralParts` in the defect detector, where insulation counted as timber
  and two genuine overall dimensions were reported as removable and deleted;
- a door or window built into an assembly, which fills an opening the empty-run
  rule would otherwise not see;
- which candidate points are worth locating at all.

The interpretation already exists in the code, three times in one file and not
agreeing. Two places read the mark prefix; the third reads `MATERIAL_TYPE`, and
that one is wrong: insulation reports 5, the same as timber.

So this is mostly not new data: every input but one is already read and sits on
`PartInView`. The exception is `IsMainPart`, which is not on it yet. It is one
interpretation in one place, instead of three.

## Shape

```
PartRole
  Defining   fixes the assembly's size and the positions inside it
  Attached   fastened to it; does not fix its size
  Ignored    takes no part in dimensions
  Unknown    no rule matched
```

`Unknown` is deliberately not `Ignored`. "Takes no part" and "nobody said" are
different, and the second has to be visible: a result computed over a set with
unknowns is a guess, not a fact, and callers that derive an extent from it need
to be able to say so. Same reasoning as `ViewContactsResult.IsComplete` — an
absence that might be a gap in the data must never read as a finding.

One role does not answer every question, and consumers must not read it as if it
did. It says what a part is to the assembly's size; what a part is worth for a
given job is the job's business:

```
overall extent      Defining only
opening occupancy   Defining and Attached - a door built into an assembly fills
                    an opening the empty-run rule cannot see, without defining
                    any extent
candidate emission  the layer's own policy; an Attached part's edge can still be
                    the right point for a dimension of that layer
```

So the role narrows a question, it does not decide one. A consumer that takes
`Defining` and discards the rest will be wrong about openings on the first
assembly with a factory-hung door.

The vocabulary is about the relationship to the assembly's size, not about the
material. Timber maps onto it — frame, insulation, fixings — and so does steel:
main part and stiffeners define, splice plates are attached, bolts are ignored.
Naming the roles after timber would have made the steel case a rewrite.

## Rules

An ordered list, first match wins.

```
prefix T                -> Defining
prefix R                -> Attached
prefix M                -> Ignored
                        -> Unknown
```

Matched on the prefix alone, so prefixes must be unique and a table claiming one
twice is refused. Ordering exists for later, when rules carry conditions beyond
the prefix; today it cannot be observed, and calling a shadowed duplicate "an
exception appended later" would only hide dead configuration behind a rule about
precedence. Ordered rather than weighted when that day comes: weights read worse
and debug worse.

**`IsMainPart` is not a rule yet.** It is a fact from Tekla, but that it means
"defines the extent on an assembly drawing" is an assumption nobody has checked.
On a timber wall the main part may well be a plate that runs the full length -
background along X, not the thing that fixes positions. Treat it as unvalidated
evidence: read it, report it in the reason, and promote it to a rule only after
it has been checked against several real assemblies of more than one kind.

**`MATERIAL_TYPE` is not a rule at all**, and not a tie-breaker either. It is
already proven to lie about the case that matters: insulation reports 5, the
same as timber, which is how it came to inflate the structural extent. Left as a
rule of last resort it would quietly become the deciding one again on every part
whose prefix is unfamiliar. It goes into the reason attached to `Unknown`, so a
person can see what was known, and nowhere else.

## Where the table lives

Undecided, and deliberately so. Defaults in code, matching this plant's
prefixes, until a second model needs different ones. A file or a setting can be
added then, when it is known who edits it — the engineer or the fitter on site.
Building the override before there is a second set of rules would be a layer
without a consumer.

## Inputs

Properties only. No geometry, no view, no coordinates:

```
IsMainPart, PartPrefix, Profile, Material, MaterialType, Name
```

That is what makes it a pure function, testable on a list of marks with no Tekla
running, and independent of whether `PartInView` and
`PartSolidGeometryInViewResult` are ever merged.

## Where the answer is stored

On `PartInView`, filled when the part is read, beside the prefix and material it
is derived from. One classification per read, the same for every consumer, and
visible in the JSON.

It carries the reason as well as the role:

```
Role     Defining | Attached | Ignored | Unknown
RuleId   which rule matched, or none
Reason   what was known - prefix, profile, material type, main-part flag
```

Without it `Unknown` is visible but useless: a person looking at a report needs
to know whether the part had no prefix, an unfamiliar one, or a prefix that no
rule covers yet. The reason is also where `MATERIAL_TYPE` and `IsMainPart`
appear, since neither decides anything.

## Selecting by role has to report what it did not know

Whatever picks parts by role returns two things: the parts, and how many were
`Unknown`. An extent computed over a set containing unknowns is a guess, and the
caller has to be able to say so.

Neither silent choice is acceptable. Including unknowns puts insulation back in
the extent, which is the bug this exists to fix. Excluding them shortens the
assembly without a word, which is worse, because a short overall looks exactly
like a correct one.

`StructuralParts` today falls back to every part when its filter comes up empty,
so that an extent of zero does not silently switch the overall test off. That
fallback stays and gains a companion: empty is one thing, incomplete is another,
and both have to reach the report.

This is the third place in this work with the same shape — `ViewContactsResult`
carries `Unread` and `IsComplete`, the outline carries the parts it could not
read. Three times is enough to call it the house rule: a result derived from a
set says what was missing from the set.

## Order of work

1. `PartRole`, the classifier and its tests. Prefix rules only.
2. `PartRoleResult` on `PartInView` — role, rule and reason, filled at read time.
3. Replace the three inline interpretations in the former defect detector, and
   decide what an `Unknown` does to the structural extent. Completed before that
   detector was removed; the structural-outline path now owns the decision.
4. Structural extent for the assembly outline, over `Defining` alone, reporting
   the unknowns alongside it.
5. `IsMainPart` on `PartInView` — read it, put it in the reason, and check on
   several assemblies whether it tracks "defines the extent" before it becomes a
   rule.

## Not this

- Guessing when no rule matches. `Unknown` is an answer.
- Reading the role as "is this part needed for dimensions". It is not that
  question, and the consumer contract above says what each job actually takes.
- A role vocabulary per material. One vocabulary, rules per model.
- Assembly type as a dimension of the table, until a steel assembly shows that
  the same prefix means something else there.
