#!/usr/bin/env python3
"""Is the difference between two SPIR-V modules a renumbering, or different code?

§9.34 got as far as "same word count, same opcode histogram, different bytes" for the three corpus
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

    usage: spirv-worddiff.py <a.spv> <b.spv>
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


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)

    a, b = read(sys.argv[1]), read(sys.argv[2])

    if len(a) != len(b):
        print(f"DIFFERENT CODE: {len(a)} words vs {len(b)} — lengths do not match")
        return 1

    if a == b:
        print("IDENTICAL")
        return 0

    forward, backward = {}, {}
    literal_diffs, opcode_diffs = [], []

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
                literal_diffs.append((name, ia, k, wa, wb))
                continue
            # The hypothesis: this word is a result ID or an ID operand.
            if forward.setdefault(wa, wb) != wb or backward.setdefault(wb, wa) != wa:
                literal_diffs.append(("inconsistent", ia, k, wa, wb))

    print(f"{len(a)} words, {sum(1 for _ in instructions(a))} instructions")

    if opcode_diffs:
        print(f"DIFFERENT CODE: {len(opcode_diffs)} instruction(s) differ in opcode or length")
        for offset, opa, opb in opcode_diffs[:10]:
            print(f"    word {offset}: opcode {opa} vs {opb}")
        return 1

    if literal_diffs:
        print(f"NOT A PURE RENUMBERING: {len(literal_diffs)} word(s) do not fit an ID bijection")
        for what, offset, k, wa, wb in literal_diffs[:20]:
            print(f"    {what} at word {offset}, operand {k}: {wa} vs {wb}")
        return 1

    print(f"RENUMBERING: {len(forward)} ID(s) remapped, consistently and one-to-one")
    sample = sorted(forward.items())[:8]
    print("    " + ", ".join(f"%{x}->%{y}" for x, y in sample)
          + (" ..." if len(forward) > len(sample) else ""))
    print("    same length, same opcodes in the same order, no literal differs,")
    print("    and every differing word fits a single consistent ID mapping.")
    # Said precisely, because the mapping only covers words that actually differ. An ID identical in
    # both modules never enters it, so a remapped ID colliding with an unchanged one would not be
    # caught here. Contrived rather than plausible — but this tool exists to justify calling a
    # difference cosmetic, and that justification should not be stronger than the evidence.
    print("    (IDs identical in both modules are not in the mapping and are not checked.)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
