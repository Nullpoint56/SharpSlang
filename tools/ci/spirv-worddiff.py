#!/usr/bin/env python3
"""Is the difference between two SPIR-V modules a renumbering, or different code?

An earlier round got as far as "same word count, same opcode histogram, different bytes" for the three corpus
shaders that use SV_VertexID, and concluded a renumbering was much the most likely explanation. That
is evidence rather than proof, and the difference matters: a renumbering is cosmetic, a differing
literal is a behavioural change wearing the same shape.

This settles it. Two modules that are a pure renumbering are related by a *bijection on result IDs*:
every differing word is an ID, the same source ID always maps to the same target ID, and no two map
to the same place. Anything that does not fit — a differing literal, a differing opcode, a differing
length — is reported as exactly that.

No operand tables are needed, which is the point. Rather than decoding every opcode's signature, it
tests the hypothesis: assume each differing word is an ID, and see whether the resulting mapping is
consistent and injective. A literal that happened to differ would almost certainly break consistency
(the same value appearing elsewhere unchanged) or injectivity.

The answer, the first time it was pointed at the real thing: **not a renumbering**, and not different
code either. Nothing is renumbered — x64 and aarch64 use the same IDs. Slang creates the two builtin
inputs behind `SV_VertexID` in the opposite order, so which variable is `VertexIndex` and which is
`BaseVertex` swaps, the two entries swap places in the OpEntryPoint interface list, and the operands
of the compensating OpISub swap with them. Both modules compute `VertexIndex - BaseVertex`. AGENTS.md#cross-platform-behaviour.

That is why the report below lists *every* differing word once the verdict is negative, not only the
words that failed the bijection. The OpISub swap fit the mapping perfectly — a transposition is a
bijection — and so went unmentioned by the first version of this tool, which made the difference look
like two stray decorations rather than a consistent reordering. A verdict of "not cosmetic" is only
useful next to the evidence for what it *is*.

    usage: spirv-worddiff.py <a.spv> <b.spv>
           spirv-worddiff.py --selftest
"""

import struct
import sys

MAGIC = 0x07230203

# Where each opcode's *literal* operands begin. Below that index the operands are IDs, so a
# difference there is part of a renumbering; from it onwards a difference is a changed value and has
# to be reported as one. OpConstant is the reason this is an index rather than a flag: its first two
# operands are the result type and the result ID, and only the third is the number.
#
# Deliberately partial. An opcode absent from this table is treated as all-IDs, so a literal it
# carries that happens to differ is reported as an inconsistent ID rather than as a literal. That
# fails in the safe direction — "not a pure renumbering", with the word printed — and never the
# other way, which is what matters for a tool whose job is to justify calling a difference cosmetic.
LITERAL_FROM = {
    5: (2, "OpName"), 6: (2, "OpMemberName"), 7: (2, "OpString"),
    11: (2, "OpExtInstImport"), 15: (3, "OpEntryPoint"), 16: (2, "OpExecutionMode"),
    21: (2, "OpTypeInt"), 22: (2, "OpTypeFloat"), 23: (3, "OpTypeVector"),
    24: (3, "OpTypeMatrix"), 25: (3, "OpTypeImage"),
    43: (3, "OpConstant"), 50: (3, "OpSpecConstant"),
    71: (2, "OpDecorate"), 72: (2, "OpMemberDecorate"),
}


def read(path):
    with open(path, "rb") as handle:
        data = handle.read()
    words = list(struct.unpack(f"<{len(data) // 4}I", data[: len(data) // 4 * 4]))
    if not words or words[0] != MAGIC:
        sys.exit(f"{path}: not little-endian SPIR-V")
    return words


def instructions(words):
    """(offset, opcode, length) for each instruction after the 5-word header."""
    i = 5
    while i < len(words):
        length = words[i] >> 16
        if length == 0:
            sys.exit("malformed module: zero-length instruction")
        yield i, words[i] & 0xFFFF, length
        i += length


def assemble(instructions):
    """A module of hand-written instructions. Enough SPIR-V to be read, not to be run."""
    words = [MAGIC, 0x00010500, 0, 99, 0]
    for opcode, operands in instructions:
        words.append(((len(operands) + 1) << 16) | opcode)
        words.extend(operands)
    return words


# OpTypeInt(21) is %result Width Signedness. OpVariable(59) is %type %result StorageClass.
# OpLoad(61) is %type %result %pointer. OpIAdd(128) is %type %result %a %b, all IDs.
#
# The definitions are the point. A renumbering moves an ID everywhere, its definition included, so a
# fixture whose IDs are never defined cannot tell a renumbering from a swap of two uses. The first
# set of fixtures written for this tool was exactly that, and passed a version of the tool that got
# the distinction wrong — an earlier mistake, made twice in the same file.
_BASE = [
    (21, [1, 32, 1]),        # %1 = OpTypeInt 32 1
    (59, [1, 4, 1]),         # %4 = OpVariable %1 Input
    (59, [1, 5, 1]),         # %5 = OpVariable %1 Input
    (61, [1, 6, 4]),         # %6 = OpLoad %1 %4
    (61, [1, 7, 5]),         # %7 = OpLoad %1 %5
    (128, [1, 8, 6, 7]),     # %8 = OpIAdd %1 %6 %7
]

SELFTEST = [
    ("identical", _BASE, _BASE, "IDENTICAL"),
    # %6 and %7 traded everywhere, definitions included.
    ("renumbering", _BASE, [(21, [1, 32, 1]), (59, [1, 4, 1]), (59, [1, 5, 1]),
                            (61, [1, 7, 4]), (61, [1, 6, 5]), (128, [1, 8, 7, 6])],
     "RENUMBERING"),
    # The definitions stay put and only the two uses swap: %7 + %6 against %6 + %7.
    ("uses swapped, definitions unmoved", _BASE, _BASE[:5] + [(128, [1, 8, 7, 6])],
     "NOT A PURE RENUMBERING"),
    ("changed literal", _BASE, [(21, [1, 16, 1])] + _BASE[1:], "NOT A PURE RENUMBERING"),
    ("different code", _BASE, _BASE[:5] + [(132, [1, 8, 6, 7])], "DIFFERENT CODE"),
]


def selftest():
    """Every verdict this tool can reach, against modules whose answer is known."""
    import io
    import contextlib

    bad = 0
    for name, left, right, expected in SELFTEST:
        captured = io.StringIO()
        with contextlib.redirect_stdout(captured):
            compare(assemble(left), assemble(right))
        verdict = captured.getvalue().splitlines()
        headline = next((line for line in verdict if line[:1].isupper()), "<nothing printed>")
        ok = headline.startswith(expected)
        bad += 0 if ok else 1
        print(f"[{'ok ' if ok else 'BAD'}] {name}: {headline}")
        if not ok:
            print(f"       expected {expected}")

    print(f"{len(SELFTEST) - bad}/{len(SELFTEST)} verdicts as expected")
    return 1 if bad else 0


def main():
    if len(sys.argv) == 2 and sys.argv[1] == "--selftest":
        return selftest()

    if len(sys.argv) != 3:
        sys.exit(__doc__)

    return compare(read(sys.argv[1]), read(sys.argv[2]))


def compare(a, b):

    if len(a) != len(b):
        print(f"DIFFERENT CODE: {len(a)} words vs {len(b)} — lengths do not match")
        return 1

    if a == b:
        print("IDENTICAL")
        return 0

    forward, backward = {}, {}
    differences, opcode_diffs = [], []

    for (ia, opa, la), (ib, opb, lb) in zip(instructions(a), instructions(b)):
        if ia != ib or opa != opb or la != lb:
            opcode_diffs.append((ia, opa, opb))
            continue

        for k in range(1, la):
            wa, wb = a[ia + k], b[ib + k]
            if wa == wb:
                continue
            first_literal, name = LITERAL_FROM.get(opa, (1 << 30, None))
            if k >= first_literal:
                differences.append((name or f"op{opa}", ia, k, wa, wb, False))
                continue
            # The hypothesis: this word is a result ID or an ID operand.
            fits = (forward.setdefault(wa, wb) == wb and backward.setdefault(wb, wa) == wa)
            differences.append(("inconsistent" if not fits else f"op{opa}", ia, k, wa, wb, fits))

    print(f"{len(a)} words, {sum(1 for _ in instructions(a))} instructions")

    if opcode_diffs:
        print(f"DIFFERENT CODE: {len(opcode_diffs)} instruction(s) differ in opcode or length")
        for offset, opa, opb in opcode_diffs[:10]:
            print(f"    word {offset}: opcode {opa} vs {opb}")
        return 1

    # A renumbering moves *every* occurrence of an ID. So if an ID the mapping moves also sits
    # somewhere the two modules already agree, the mapping is not describing a renumbering — it is
    # describing operands that traded places while their definitions stayed where they were. That is
    # The SV_VertexID case: `%22 = OpLoad …` at the same word in both, and an OpISub below it using %22
    # where the other module uses %25.
    #
    # Without this the tool answers RENUMBERING to a bare operand swap, which is the one direction
    # it is not allowed to be wrong in — the whole point of it is to justify calling a difference
    # cosmetic. The cost is false negatives: a literal in an opcode missing from LITERAL_FROM whose
    # value happens to equal a moved ID reads as an anchor. That direction is the safe one and it
    # names the word it tripped on, so it can be judged rather than trusted.
    moved = set(forward) | set(backward)
    anchored = []
    if moved:
        for (ia, opa, la), (ib, _, _) in zip(instructions(a), instructions(b)):
            first_literal, _ = LITERAL_FROM.get(opa, (1 << 30, None))
            for k in range(1, min(la, first_literal)):
                if a[ia + k] == b[ib + k] and a[ia + k] in moved:
                    anchored.append((ia, k, a[ia + k]))

    failures = [d for d in differences if not d[5]]

    if failures or anchored:
        reasons = []
        if failures:
            reasons.append(f"{len(failures)} word(s) do not fit an ID bijection")
        if anchored:
            reasons.append(f"{len(anchored)} occurrence(s) of a remapped ID are unchanged")
        print("NOT A PURE RENUMBERING: " + "; ".join(reasons))

        if anchored:
            print("    the IDs did not move, their uses did:")
            for offset, k, value in anchored[:12]:
                print(f"        %{value} is itself at word {offset}, operand {k}, in both")
            print("        while the mapping moves it: "
                  + ", ".join(f"%{x}->%{y}" for x, y in sorted(forward.items())[:8]))

        # Every differing word, not only the failures. The words that *fit* the mapping are the
        # ones that made this difference look smaller than it is the first time: a two-ID swap is a
        # perfectly good bijection, so an OpISub whose operands had traded places passed without
        # comment while the decorations above it were reported. Whoever reads this verdict is
        # deciding whether the difference is safe, and half the difference is no basis for that.
        print(f"    {len(differences)} differing word(s) in total, marked * where they do not fit:")
        for what, offset, k, wa, wb, fits in differences[:40]:
            mark = " " if fits else "*"
            print(f"   {mark}{what} at word {offset}, operand {k}: {wa} vs {wb}")
        if len(differences) > 40:
            print(f"    ... and {len(differences) - 40} more")
        return 1

    print(f"RENUMBERING: {len(forward)} ID(s) remapped, consistently and one-to-one")
    sample = sorted(forward.items())[:8]
    print("    " + ", ".join(f"%{x}->%{y}" for x, y in sample)
          + (" ..." if len(forward) > len(sample) else ""))
    print("    same length, same opcodes in the same order, no literal differs,")
    print("    every differing word fits a single consistent ID mapping, and every")
    print("    remapped ID moved everywhere it appears.")
    # The remaining hole, stated because this tool exists to justify calling a difference cosmetic
    # and that justification must not read stronger than the evidence. An opcode missing from
    # LITERAL_FROM has all of its operands treated as IDs, so a literal of its that differed would
    # be absorbed into the mapping as though it were a renumbered ID. The check above catches that
    # whenever the same value also appears unchanged somewhere; a value appearing exactly once does
    # not get caught. Add the opcode to LITERAL_FROM if that ever matters.
    print("    (a literal in an opcode absent from LITERAL_FROM, differing and appearing")
    print("     nowhere else, would still be read as a renumbered ID.)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
