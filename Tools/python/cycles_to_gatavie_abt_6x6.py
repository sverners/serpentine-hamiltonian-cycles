#!/usr/bin/env python3
# -*- coding: utf-8 -*-

# cd C:\Users\svern\source\repos\serpentinaHC3_6x6\bin\Debug
# python cycles_to_gatavie_abt_6x6.py cikls4abt6x6.hrz gatavie_abt_6x6
# python cycles_to_gatavie_abt_6x6.py cikls4abt6x6.hrz gatavie_abt_6x6 --saglabat-starpposmu

"""
APVIENOTS skripts (agrāk 2 atsevišķi: cycles_to_base_abt_6x6.py +
generate_ready_abt_6x6.py) - VIENĀ solī pārvērš savāktos SLĒGTOS 6x6
Hamiltona ciklus par GATAVU .abt katalogu (ar manifest.txt), ko tieši
lieto serpentina_hc_v12.py.

KĀPĒC APVIENOTS: starpposma "bāzes" turp/atpak pāri (agrāk atsevišķi
rakstīti baze_abt_6x6/ direktorijā) NEKUR CITUR netiek izmantoti - tie
kalpoja TIKAI kā ievade nākamajam solim. Apvienojot, tie paliek ATMIŅĀ
(nevis diskā), kas ir ātrāk un vienkāršāk - VIENA komanda, ne divas.

Ja tomēr vajag redzēt/pārbaudīt starpposmu (piem. diagnostikas nolūkos,
kā tas noderēja šīs sistēmas kļūdu atrašanai) - lieto --saglabat-starpposmu
karodziņu, kas tos TOMĒR ieraksta diskā (tādā pašā formātā/nosaukumos,
kāds bija baze_abt_6x6/), NEMAINOT galveno darbību.

DIVI SOĻI (tagad iekšēji, bez starpposma faila):
  1. GRIEŠANA: katru savākto slēgto ciklu normalizē un sadala turp/atpak
     pāros TIKAI pie derīgajām hub->satelīts (VIRZIENA!) griezuma malām -
     skat. SPECIAL_HUB_PAIRS un find_special_edge_positions().
  2. ORIENTĀCIJU IZPĒTE: katram pārim izrēķina 8 rotācijas/spoguļojumus
     (BEZ apgriešanas - apgriešana sagrautu derīgo hub->satelīts
     virzienu, skat. piezīmi pie TRANSFORMS cilpas), sagrupē pēc precīza
     (turp_ieeja,turp_izeja,atpak_ieeja,atpak_izeja) paraksta, raksta
     VISUS viena avota cikla variantus VIENĀ izvades failā (diska vietas
     taupīšanai) un izveido manifest.txt.

LIETOŠANA:
    python3 cycles_to_gatavie_abt_6x6.py [cikls_fails] [gatavo_izvades_mape] [--saglabat-starpposmu [bazes_mape]]
    (noklusējumi: cikls4abt6x6.hrz -> gatavie_abt_6x6)
"""

import os
import re
import sys
from itertools import combinations

KNIGHT_MOVES = {
    '1': (1, 2),  '2': (2, 1),  '3': (2, -1),   '4': (1, -2),
    '5': (-1, -2), '6': (-2, -1), '7': (-2, 1), '8': (-1, 2)
}
INVERSE_MOVES = {v: k for k, v in KNIGHT_MOVES.items()}

BLOCK_SIZE = 6
S = BLOCK_SIZE - 1  # 5
TOTAL_CELLS = BLOCK_SIZE * BLOCK_SIZE  # 36

CIKLS_FILE_DEFAULT = "cikls4abt6x6.hrz"
OUT_DIR_DEFAULT = "gatavie_abt_6x6"

# Lietotāja precīzi norādītās hub-satelīts malas. GRIEŠANAI derīgs TIKAI
# VIRZIENS HUB -> SATELĪTS (nevis abi virzieni!) - derīgai "plēstai"
# daļai jāSĀKAS SATELĪTĀ (mala, koordināte 0 vai max) UN jāBEIDZAS HUB
# punktā (koordināte 1 vai max-1). Pretējā virzienā (satelīts->hub)
# griezums dotu NEPAREIZI tipizētu daļu.
SPECIAL_HUB_PAIRS = [
    ((1, 1), (0, 3)), ((1, 1), (3, 0)),
    ((4, 1), (2, 0)), ((4, 1), (5, 3)),
    ((4, 4), (5, 2)), ((4, 4), (2, 5)),
    ((1, 4), (0, 2)), ((1, 4), (3, 5)),
]
SPECIAL_EDGES_DIRECTED = set(SPECIAL_HUB_PAIRS)

TRANSFORMS = [
    ("0", lambda x, y, s: (x, y)),
    ("90", lambda x, y, s: (s - y, x)),
    ("180", lambda x, y, s: (s - x, s - y)),
    ("270", lambda x, y, s: (y, s - x)),
    ("FlnHRZ", lambda x, y, s: (s - x, y)),
    ("FlnVRT", lambda x, y, s: (x, s - y)),
    ("FlnDp90", lambda x, y, s: (y, x)),
    ("FlnDm90", lambda x, y, s: (s - y, s - x)),
]


# ---------------------------------------------------------------------
# 1. daļa: GRIEŠANA (agrāk cycles_to_base_abt_6x6.py)
# ---------------------------------------------------------------------

def moves_to_path(move_str, start=(0, 0)):
    path = [start]
    cx, cy = start
    for ch in move_str:
        dx, dy = KNIGHT_MOVES[ch]
        cx, cy = cx + dx, cy + dy
        path.append((cx, cy))
    return path


def path_to_moves(path):
    chars = []
    for i in range(len(path) - 1):
        dx = path[i + 1][0] - path[i][0]
        dy = path[i + 1][1] - path[i][1]
        chars.append(INVERSE_MOVES[(dx, dy)])
    return "".join(chars)


def load_cycles(fname):
    """Nolasa ciklus no faila. ATBALSTA ABUS formātus: katrs cikls savā
    rindā, VAI cikli atdalīti ar tukšu rindu."""
    if not os.path.exists(fname):
        raise FileNotFoundError(f"Nav atrasts {fname} - vispirms jāsavāc cikli ar zirgaGaljumsGen.")
    with open(fname, "r", encoding="utf-8") as f:
        raw = f.read()
    raw_lines = raw.replace("\r\n", "\n").split("\n")
    cycles = []
    for raw_line in raw_lines:
        cleaned = re.sub(r'[^1-8]', '', raw_line)
        if not cleaned:
            continue
        if len(cleaned) != TOTAL_CELLS:
            print(f"[BRĪDINĀJUMS] izlaižu ciklu ar {len(cleaned)} virzieniem "
                  f"(gaidīti {TOTAL_CELLS}): {cleaned[:20]}...")
            continue
        cycles.append(cleaned)
    return cycles


def normalized_cells(moves_str):
    """Izseko ciklu no (0,0) UN normalizē (nobīda, lai robežlodziņa min
    nonāktu (0,0))."""
    path = moves_to_path(moves_str)
    cells = path[:-1]
    if len(set(cells)) != TOTAL_CELLS or path[-1] != path[0]:
        return None, False
    xs = [c[0] for c in cells]; ys = [c[1] for c in cells]
    minx, miny = min(xs), min(ys)
    norm = [(x - minx, y - miny) for x, y in cells]
    if max(c[0] for c in norm) > BLOCK_SIZE - 1 or max(c[1] for c in norm) > BLOCK_SIZE - 1:
        return None, False
    return norm, True


def find_special_edge_positions(norm_cells):
    """Atgriež indeksus i, kur mala (cells[i] -> cells[i+1]) iet TIEŠI
    hub->satelīts VIRZIENĀ - vienīgie derīgie griešanas punkti."""
    N = len(norm_cells)
    positions = []
    for i in range(N):
        edge = (norm_cells[i], norm_cells[(i + 1) % N])
        if edge in SPECIAL_EDGES_DIRECTED:
            positions.append(i)
    return positions


def split_at_two_points(norm_cells, i, j):
    N = len(norm_cells)
    arc1 = norm_cells[i + 1: j + 1]
    arc2 = norm_cells[j + 1:] + norm_cells[:i + 1]
    return arc1, arc2


def renormalize(arc):
    ox, oy = arc[0]
    return [(x - ox, y - oy) for x, y in arc]


def cycles_to_pairs_by_source(cycles):
    """Katram avota ciklam (indekss i) atgriež TĀ derīgo (turp,atpak)
    pāru sarakstu (atmiņā, BEZ starpposma faila rakstīšanas). Atgriež
    dict {source_name: [(turp_str,atpak_str), ...]} un statistiku."""
    result = {}
    skipped_invalid = 0
    skipped_no_edges = 0
    edge_count_histogram = {}

    for i, moves_str in enumerate(cycles):
        norm_cells, ok = normalized_cells(moves_str)
        if not ok:
            skipped_invalid += 1
            continue

        special_positions = find_special_edge_positions(norm_cells)
        edge_count_histogram[len(special_positions)] = edge_count_histogram.get(len(special_positions), 0) + 1

        if len(special_positions) < 2:
            skipped_no_edges += 1
            continue

        pairs_for_this_cycle = []
        for pi, pj in combinations(special_positions, 2):
            lo, hi = (pi, pj) if pi < pj else (pj, pi)
            arc_a, arc_b = split_at_two_points(norm_cells, lo, hi)
            if len(arc_a) == 0 or len(arc_b) == 0:
                continue
            for swap in (False, True):
                turp_arc, atpak_arc = (arc_b, arc_a) if swap else (arc_a, arc_b)
                turp_str = path_to_moves(renormalize(turp_arc))
                atpak_str = path_to_moves(renormalize(atpak_arc))
                pairs_for_this_cycle.append((turp_str, atpak_str))

        if pairs_for_this_cycle:
            source_name = f"{i:08d}.abt"
            result[source_name] = pairs_for_this_cycle

    stats = {
        "total_cycles": len(cycles),
        "skipped_invalid": skipped_invalid,
        "skipped_no_edges": skipped_no_edges,
        "edge_count_histogram": edge_count_histogram,
        "sources_written": len(result),
        "pairs_written": sum(len(v) for v in result.values()),
    }
    return result, stats


# ---------------------------------------------------------------------
# 2. daļa: ORIENTĀCIJU IZPĒTE (agrāk generate_ready_abt_6x6.py)
# ---------------------------------------------------------------------

def align_pair_geometry(turp_str, atpakal_str):
    """Atrod nobīdi (shift_x,shift_y), lai 'turp' un 'atpakaļ' fragmenti
    kopā (nepārklājoties) precīzi segtu visas TOTAL_CELLS šūnas
    BLOCK_SIZE x BLOCK_SIZE rāmī."""
    path_a = moves_to_path(turp_str)
    path_b_base = moves_to_path(atpakal_str)
    set_a = set(path_a)
    a_xs = [p[0] for p in path_a]; a_ys = [p[1] for p in path_a]
    b_xs = [p[0] for p in path_b_base]; b_ys = [p[1] for p in path_b_base]
    amin_x, amax_x = min(a_xs), max(a_xs)
    amin_y, amax_y = min(a_ys), max(a_ys)
    bmin_x, bmax_x = min(b_xs), max(b_xs)
    bmin_y, bmax_y = min(b_ys), max(b_ys)
    slack_x = S - max(amax_x - amin_x, bmax_x - bmin_x)
    slack_y = S - max(amax_y - amin_y, bmax_y - bmin_y)
    if slack_x < 0 or slack_y < 0:
        return None, None, None, None
    shift_x_lo = amin_x - bmax_x - slack_x
    shift_x_hi = amax_x - bmin_x + slack_x
    shift_y_lo = amin_y - bmax_y - slack_y
    shift_y_hi = amax_y - bmin_y + slack_y
    for shift_x in range(shift_x_lo, shift_x_hi + 1):
        for shift_y in range(shift_y_lo, shift_y_hi + 1):
            shifted_b = [(x + shift_x, y + shift_y) for x, y in path_b_base]
            set_b = set(shifted_b)
            if len(set_b) != len(shifted_b):
                continue
            if set_a.isdisjoint(set_b):
                combined_min_x = min(amin_x, bmin_x + shift_x)
                combined_max_x = max(amax_x, bmax_x + shift_x)
                combined_min_y = min(amin_y, bmin_y + shift_y)
                combined_max_y = max(amax_y, bmax_y + shift_y)
                if (combined_max_x - combined_min_x == S and
                        combined_max_y - combined_min_y == S and
                        len(set_a) + len(set_b) == TOTAL_CELLS):
                    norm_x, norm_y = combined_min_x, combined_min_y
                    norm_a = [(x - norm_x, y - norm_y) for x, y in path_a]
                    norm_b = [(x - norm_x, y - norm_y) for x, y in shifted_b]
                    return norm_a, norm_b, shift_x, shift_y
    return None, None, None, None


def fast_align_with_known_shift(turp_str, atpakal_str, shift_x, shift_y):
    path_a = moves_to_path(turp_str)
    path_b_base = moves_to_path(atpakal_str)
    shifted_b = [(x + shift_x, y + shift_y) for x, y in path_b_base]
    set_a = set(path_a)
    set_b = set(shifted_b)
    if len(set_b) != len(shifted_b) or not set_a.isdisjoint(set_b):
        return None, None
    all_x = [p[0] for p in path_a] + [p[0] for p in shifted_b]
    all_y = [p[1] for p in path_a] + [p[1] for p in shifted_b]
    min_x, max_x = min(all_x), max(all_x)
    min_y, max_y = min(all_y), max(all_y)
    if max_x - min_x != S or max_y - min_y != S or len(set_a) + len(set_b) != TOTAL_CELLS:
        return None, None
    norm_a = [(x - min_x, y - min_y) for x, y in path_a]
    norm_b = [(x - min_x, y - min_y) for x, y in shifted_b]
    return norm_a, norm_b


def edge_classes_str(p, s=S):
    """Malas, kam punkts pieskaras, kā teksta forma (piem. 'BL' stūrim,
    'B' parastai malai) - der TIEŠI TĀ paša formātā manifesta kolonnās."""
    x, y = p
    labels = set()
    if x <= 1: labels.add('L')
    if x >= s - 1: labels.add('R')
    if y <= 1: labels.add('T')
    if y >= s - 1: labels.add('B')
    if not labels:
        labels.add('M')
    return "".join(sorted(labels))


def generate_for_source(source_name, pairs, out_dir):
    """Apstrādā VIENA avota cikla visus turp/atpak pārus (jau ATMIŅĀ,
    nevis no faila) un atgriež manifesta rindas. Visi šī avota unikālie
    ģeometrijas varianti tiek rakstīti VIENĀ izvades failā."""
    if not pairs:
        return []

    shift = None
    for turp_str, atpak_str in pairs:
        norm_a, norm_b, sx, sy = align_pair_geometry(turp_str, atpak_str)
        if norm_a is not None:
            shift = (sx, sy)
            break
    if shift is None:
        print(f"[BRĪDINĀJUMS] {source_name}: nevienam pārim neizdevās atrast nobīdi!")
        return []

    groups = {}
    for turp_str, atpak_str in pairs:
        norm_a, norm_b = fast_align_with_known_shift(turp_str, atpak_str, *shift)
        if norm_a is None:
            norm_a, norm_b, _, _ = align_pair_geometry(turp_str, atpak_str)
            if norm_a is None:
                continue
        for rot_name, transform in TRANSFORMS:
            t_a = [transform(x, y, S) for x, y in norm_a]
            t_b = [transform(x, y, S) for x, y in norm_b]
            min_x = min(min(x for x, y in t_a), min(x for x, y in t_b))
            min_y = min(min(y for x, y in t_a), min(y for x, y in t_b))
            n_a = [(x - min_x, y - min_y) for x, y in t_a]
            n_b = [(x - min_x, y - min_y) for x, y in t_b]
            # NEDRĪKST apgriezt (a_rev/b_rev) - griešana JAU garantē
            # hub->satelīts virzienu; apgriešana to sagrautu.
            turp_path = n_a
            atpak_path = n_b
            sig = (turp_path[0], turp_path[-1], atpak_path[0], atpak_path[-1])
            turp_moves = path_to_moves(turp_path)
            atpak_moves = path_to_moves(atpak_path)
            groups.setdefault(sig, []).append((turp_moves, atpak_moves))

    base = os.path.splitext(source_name)[0]
    out_name = f"{base}.abt"
    out_path = os.path.join(out_dir, out_name)
    manifest_rows = []
    pair_offset = 0
    with open(out_path, "w", encoding="utf-8", newline="") as f:
        for sig, rows in sorted(groups.items()):
            turp_entry, turp_exit, atpak_entry, atpak_exit = sig
            for turp_moves, atpak_moves in rows:
                f.write(turp_moves + "\r\n")
                f.write(atpak_moves + "\r\n")
            manifest_rows.append({
                "file": out_name, "source": source_name, "pair_offset": pair_offset,
                "turp_entry": turp_entry, "turp_exit": turp_exit,
                "atpak_entry": atpak_entry, "atpak_exit": atpak_exit,
                "turp_entry_edge": edge_classes_str(turp_entry), "turp_exit_edge": edge_classes_str(turp_exit),
                "atpak_entry_edge": edge_classes_str(atpak_entry), "atpak_exit_edge": edge_classes_str(atpak_exit),
                "pairu_skaits": len(rows),
            })
            pair_offset += len(rows)
    return manifest_rows


# ---------------------------------------------------------------------
# main
# ---------------------------------------------------------------------

def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    save_intermediate = "--saglabat-starpposmu" in sys.argv
    intermediate_dir = "baze_abt_6x6"
    if save_intermediate:
        idx = sys.argv.index("--saglabat-starpposmu")
        if idx + 1 < len(sys.argv) and not sys.argv[idx + 1].startswith("--"):
            intermediate_dir = sys.argv[idx + 1]

    cikls_file = args[0] if len(args) > 0 else CIKLS_FILE_DEFAULT
    out_dir = args[1] if len(args) > 1 else OUT_DIR_DEFAULT

    cycles = load_cycles(cikls_file)
    print(f"[INFO] Ielasīti {len(cycles)} derīgi {BLOCK_SIZE}x{BLOCK_SIZE} slēgtie cikli no {cikls_file}")

    pairs_by_source, stats = cycles_to_pairs_by_source(cycles)
    print(f"[INFO] Sagriezti {stats['sources_written']} derīgi avota cikli "
          f"({stats['pairs_written']} turp/atpakaļ pāri kopā)")
    if stats["skipped_invalid"]:
        print(f"[BRĪDINĀJUMS] Izlaisti {stats['skipped_invalid']} nederīgi cikli.")
    if stats["skipped_no_edges"]:
        print(f"[BRĪDINĀJUMS] Izlaisti {stats['skipped_no_edges']} cikli ar mazāk par 2 derīgām griezuma malām.")
    print(f"[INFO] Sadalījums pēc atrasto īpašo malu skaita: {dict(sorted(stats['edge_count_histogram'].items()))}")

    if save_intermediate:
        os.makedirs(intermediate_dir, exist_ok=True)
        for source_name, pairs in pairs_by_source.items():
            with open(os.path.join(intermediate_dir, source_name), "w", encoding="utf-8", newline="") as f:
                for turp_str, atpak_str in pairs:
                    f.write(turp_str + "\r\n")
                    f.write(atpak_str + "\r\n")
        print(f"[INFO] Starpposma bāzes faili SAGLABĀTI (pēc pieprasījuma) direktorijā {intermediate_dir}/")

    os.makedirs(out_dir, exist_ok=True)
    all_manifest = []
    for source_name, pairs in pairs_by_source.items():
        rows = generate_for_source(source_name, pairs, out_dir)
        all_manifest.extend(rows)

    manifest_path = os.path.join(out_dir, "manifest.txt")
    with open(manifest_path, "w", encoding="utf-8") as f:
        f.write("fails;avots;paru_nobide;turp_ieeja;turp_izeja;atpak_ieeja;atpak_izeja;"
                "turp_ieeja_mala;turp_izeja_mala;atpak_ieeja_mala;atpak_izeja_mala;pairu_skaits\n")
        for row in all_manifest:
            f.write(f"{row['file']};{row['source']};{row['pair_offset']};"
                    f"{row['turp_entry']};{row['turp_exit']};"
                    f"{row['atpak_entry']};{row['atpak_exit']};"
                    f"{row['turp_entry_edge']};{row['turp_exit_edge']};"
                    f"{row['atpak_entry_edge']};{row['atpak_exit_edge']};"
                    f"{row['pairu_skaits']}\n")

    print(f"\n[OK] Kopā izveidoti {len(all_manifest)} ģeometrijas ieraksti, "
          f"{stats['sources_written']} izvades faili {out_dir}/ direktorijā.")
    print(f"Uzziņas fails: {manifest_path}")


if __name__ == "__main__":
    main()
