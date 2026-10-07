#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Serpentiņa Hamiltona Cikla (HC) ģenerators - v12.

IZMAIŅAS pret v11 (kas strādāja TIKAI ar 16x16 blokiem):
  - Gatavo failu direktorija VAIRS NAV cieti iešūta ("gatavie_abt") -
    tagad tā ir komandrindas parametrs (noklusējums "gatavie_abt", lai
    saglabātu iepriekšējo uzvedību bez izmaiņām, ja neko nenorāda).
  - Bloka izmērs (agrāk cieti iešūts 16) TAGAD TIEK AUTOMĀTISKI NOTEIKTS
    no paša manifest.txt satura (skat. detect_block_size()) - VAIRS NAV
    jānorāda manuāli, un VAIRS NAV riska, ka tas klusi paliek nepareizs
    (kā tas notiktu, ja v11 mēģinātu izmantot 6x6 katalogu, kur
    find_serpentine_ring_from_shelf/build_hrz_from_shelf_choice būtu
    klusi izmantojušas noklusējuma block_size=16!).
  - Tā rezultātā VIENS UN TAS PATS skripts der jebkura izmēra blokiem
    (6x6, 16x16, un jebkuram citam) - tikai jānorāda pareizā gatavo
    failu direktorija.

VISS PĀRĒJAIS IDENTISKS v11: NEKĀDA ģeometrijas aprēķināšana reālajā
laikā (nav align_pair_geometry, TRANSFORMS, rotācijas) - tikai manifesta
izvēle un pārbaude, vai jau ZINĀMIE punkti savienojas.

LIETOŠANA:
    python3 serpentina_hc_v12.py [gatavo_failu_direktorija] [FFseciba_fails]
    (noklusējumi: "gatavie_abt", "FFseciba.txt" - tātad iepriekšējā v11
    izsaukuma sintakse "python3 serpentina_hc_v12.py" turpina strādāt
    identiski, kā strādāja ar v11, 16x16 gadījumam)

    Piemēri:
        python3 serpentina_hc_v12.py gatavie_abt_6x6
        python3 serpentina_hc_v12.py gatavie_abt          (16x16, kā v11)
"""

import os
import re
import sys
import random
import time

KNIGHT_MOVES = {
    '1': (1, 2),  '2': (2, 1),  '3': (2, -1),   '4': (1, -2),
    '5': (-1, -2),  '6': (-2, -1),  '7': (-2, 1), '8': (-1, 2)
}
INVERSE_MOVES = {v: k for k, v in KNIGHT_MOVES.items()}
KNIGHT_VECTORS = list(KNIGHT_MOVES.values())

GATAVIE_DIR = "gatavie_abt"  # tiek pārrakstīts main() sākumā no argv
MANIFEST_PATH = os.path.join(GATAVIE_DIR, "manifest.txt")  # tāpat


def parse_point(s):
    """'(6, 1)' -> (6, 1) - tikai teksta parsēšana, NAV ģeometrijas aprēķins."""
    s = s.strip().strip("()")
    x_str, y_str = s.split(",")
    return (int(x_str), int(y_str))


def load_manifest():
    """
    Nolasa <GATAVIE_DIR>/manifest.txt - katrs ieraksts jau satur PRECĪZI
    ZINĀMAS (turp_ieeja, turp_izeja, atpak_ieeja, atpak_izeja) koordinātes
    UN faila nosaukumu, no kura šīs koordinātes ir tieši tādas (bez
    jebkādas pārrēķināšanas nepieciešamības).

    ATBALSTA ABUS formātus (atpazīst pēc lauku skaita):
      - JAUNAIS (12 lauki, ar 'paru_nobide') - vairāki ģeometrijas varianti
        DALĀS VIENĀ kopīgā failā (diska vietas taupīšanai); "pair_offset"
        pasaka, no kuras PĀRA vietas šī konkrētā grupa sākas tajā failā.
      - VECAIS (11 lauki, bez nobīdes) - katrai grupai SAVS atsevišķs
        fails, "pair_offset" tad vienmēr ir 0 (visa faila saturs pieder
        šai vienai grupai).
    """
    if not os.path.exists(MANIFEST_PATH):
        raise FileNotFoundError(
            f"Nav atrasts {MANIFEST_PATH} - vispirms jāpalaiž generate_ready_abt.py "
            f"(vai generate_ready_abt_6x6.py u.tml.), lai izveidotu gatavo failu katalogu.")
    entries = []
    with open(MANIFEST_PATH, "r", encoding="utf-8") as f:
        header = f.readline()
        new_format = "paru_nobide" in header
        for line in f:
            line = line.rstrip("\n")
            if not line:
                continue
            parts = line.split(";")
            if new_format:
                (fname, source, pair_offset, turp_entry, turp_exit, atpak_entry, atpak_exit,
                 te_edge, tx_edge, ae_edge, ax_edge, pair_count) = parts
            else:
                (fname, source, turp_entry, turp_exit, atpak_entry, atpak_exit,
                 te_edge, tx_edge, ae_edge, ax_edge, pair_count) = parts
                pair_offset = 0
            entries.append({
                "file": fname,
                "source": source,
                "pair_offset": int(pair_offset),
                "turp_entry": parse_point(turp_entry),
                "turp_exit": parse_point(turp_exit),
                "atpak_entry": parse_point(atpak_entry),
                "atpak_exit": parse_point(atpak_exit),
                "turp_entry_edge": te_edge,
                "turp_exit_edge": tx_edge,
                "atpak_entry_edge": ae_edge,
                "atpak_exit_edge": ax_edge,
                "pair_count": int(pair_count),
            })
    return entries


def detect_block_size(manifest):
    """Nosaka bloka izmēru AUTOMĀTISKI no manifesta satura - bloka
    koordinātes vienmēr ir robežās [0, block_size-1], un tā kā edge_class
    klasifikācija (L/R/T/B) garantē, ka katrā ierakstā VISMAZ viens punkts
    pieskaras kādai malai, MAKSIMĀLĀ sastaptā koordināte pāri VISIEM
    ierakstiem ir tieši (block_size - 1). Nekāda manuāla norādīšana nav
    vajadzīga - der jebkuram bloka izmēram (6, 16, vai jebkuram citam)."""
    max_coord = 0
    for e in manifest:
        for pt in (e["turp_entry"], e["turp_exit"], e["atpak_entry"], e["atpak_exit"]):
            max_coord = max(max_coord, pt[0], pt[1])
    return max_coord + 1


def edge_facing_neighbor(dx, dy):
    if dx == -1 and dy == 0:
        return 'L'
    if dx == 1 and dy == 0:
        return 'R'
    if dx == 0 and dy == -1:
        return 'T'
    if dx == 0 and dy == 1:
        return 'B'
    return None


def required_turp_edges(positions, level):
    N = len(positions)
    entry = None
    exitd = None
    if level > 0:
        dx = positions[level - 1][0] - positions[level][0]
        dy = positions[level - 1][1] - positions[level][1]
        entry = edge_facing_neighbor(dx, dy)
    if level < N - 1:
        dx = positions[level + 1][0] - positions[level][0]
        dy = positions[level + 1][1] - positions[level][1]
        exitd = edge_facing_neighbor(dx, dy)
    return entry, exitd


def required_atpak_edges(positions, level):
    N = len(positions)
    entry = None
    exitd = None
    if level < N - 1:
        dx = positions[level + 1][0] - positions[level][0]
        dy = positions[level + 1][1] - positions[level][1]
        entry = edge_facing_neighbor(dx, dy)
    if level > 0:
        dx = positions[level - 1][0] - positions[level][0]
        dy = positions[level - 1][1] - positions[level][1]
        exitd = edge_facing_neighbor(dx, dy)
    return entry, exitd


def parse_ff_seciba_grid(filename="FFseciba.txt"):
    if not os.path.exists(filename):
        print(f"[KĻŪDA] Fails {filename} netika atrasts!")
        return None, None
    grid_positions = {}
    with open(filename, "r", encoding="utf-8") as f:
        lines = f.readlines()
    row_idx = 0
    for line in lines:
        line = line.rstrip("\n").rstrip("\r")
        if line.strip() == "":
            continue
        fields = line.split(",")
        for col_idx, field in enumerate(fields):
            field = field.strip()
            if field == "" or not re.fullmatch(r"-?\d+", field):
                continue
            step_id = int(field)
            if step_id in grid_positions:
                print(f"[KĻŪDA] Vērtība '{step_id}' parādās vairāk kā vienu reizi failā {filename}!")
                return None, None
            grid_positions[step_id] = (col_idx, row_idx)
        row_idx += 1
    if not grid_positions:
        print("[KĻŪDA] FFseciba.txt ir tukšs vai satur nederīgus datus!")
        return None, None

    # SVARĪGI (labots pēc lietotāja lūguma): VAIRS NEPRASA, lai vērtības
    # būtu TIEŠI 0..N-1 - tā vietā meklē no MAZĀKĀS klātesošās vērtības un
    # izmanto TIKAI RELATĪVO (sakārtoto) secību. Tā der arī nobīdītai vai
    # citādi nesekvenciālai numerācijai (piem. sākas ar 602, nevis 0) -
    # SVARĪGA ir tikai vērtību SAVSTARPĒJĀ secība, ne absolūtās vērtības.
    # Attēlošanai (izvadē) SAGLABĀJAM oriģinālās vērtības (piem. "Bloks
    # #602"), lai tās sakristu ar to, ko lietotājs redz savā failā.
    sorted_values = sorted(grid_positions.keys())
    if sorted_values[0] != 0:
        print(f"[INFO] Vērtības nesākas ar 0 (mazākā klātesošā vērtība: {sorted_values[0]}) - "
              f"meklēju TIEŠI no tās, izmantojot relatīvo secību.")
    ordered_steps = sorted_values
    positions = [grid_positions[v] for v in sorted_values]
    return ordered_steps, positions


def check_chain_adjacency(positions, require_closing=True):
    N = len(positions)
    problems = []
    steps = list(range(N - 1)) if not require_closing else list(range(N))
    for i in steps:
        a = positions[i]
        b = positions[(i + 1) % N]
        dx, dy = b[0] - a[0], b[1] - a[1]
        adjacent = (abs(dx) == 1 and dy == 0) or (abs(dy) == 1 and dx == 0)
        if not adjacent:
            problems.append((i, (i + 1) % N, a, b, dx, dy))
    return problems


def is_valid_knight_move(p1, p2):
    """Vienkārša aritmētika - PĀRBAUDA, vai divi JAU ZINĀMI punkti
    savienojas, NEMEKLĒ un NEAPRĒĶINA nekādu jaunu ģeometriju."""
    dx, dy = p1[0] - p2[0], p1[1] - p2[1]
    return (dx * dx + dy * dy) == 5


def build_level_candidate_lists(manifest, positions):
    """
    Katram FFseciba.txt blokam VIENU REIZI atlasa (no MANIFESTA, bez
    aprēķina) tos gatavos failus, kuru mala atbilst šai pozīcijai
    vajadzīgajai lomai. Šis ir TIKAI TEKSTA/SARAKSTA FILTRĒŠANA
    (salīdzina jau zināmus 'L'/'R'/'T'/'B' burtus), nevis ģeometrija.

    SVARĪGI: katra ieraksta "*_edge" lauks tagad var saturēt VAIRĀKAS
    malas (piem. "BL" stūra punktam), nevis tikai vienu - tāpēc pārbaude
    ir PIEDERĪBA (vai vajadzīgā mala IR ŠAJĀ KOPĀ), nevis PRECĪZA
    vienādība. Tas nepieciešams, jo 6x6 blokiem stingra "viena mala uz
    punktu" klasifikācija padara dažus zirdziņa-gājiena savienojumus
    (piem. B->B) ĢEOMETRISKI NEIESPĒJAMUS, lai gan tie patiesībā strādā -
    stūra punkti vienkārši jāļauj izmantot VAIRĀKĀM lomām.

    ĀTRDARBĪBA: liela kataloga (simtiem tūkstošu ierakstu) gadījumā
    pilna manifesta pārmeklēšana KATRAI no N pozīcijām (t.i. N pilni
    caurgājieni) kļūst lēna. Tāpēc VIENU REIZI (nevis N reizes)
    uzbūvējam indeksu pēc turp_exit_edge rakstzīmes - katrai pozīcijai
    tad meklējam TIKAI atbilstošajā (jau daudz mazākajā) apakškopā,
    nevis visā manifestā no jauna."""
    by_turp_exit_char = {}
    for e in manifest:
        for ch in e["turp_exit_edge"]:
            by_turp_exit_char.setdefault(ch, []).append(e)

    N = len(positions)
    level_candidates = []
    for level in range(N):
        _, turp_exit = required_turp_edges(positions, level)
        atpak_entry, atpak_exit = required_atpak_edges(positions, level)
        pool = by_turp_exit_char.get(turp_exit, []) if turp_exit is not None else manifest
        matching = []
        for e in pool:
            if turp_exit is not None and turp_exit not in e["turp_exit_edge"]:
                continue
            if atpak_entry is not None and atpak_entry not in e["atpak_entry_edge"]:
                continue
            if atpak_exit is not None and atpak_exit not in e["atpak_exit_edge"]:
                continue
            matching.append(e)
        level_candidates.append(matching)
    return level_candidates


def find_serpentine_ring_from_shelf(manifest, positions, block_size=16,
                                     first_tries=2000, seed=None,
                                     max_expansions=200000, require_closing=True,
                                     progress_callback=None):
    """
    Tieši tāda pati ķēdes/noslēguma loģika kā iepriekš, TIKAI kandidāti
    nāk TIEŠI no manifesta (jau zināmi punkti) - nekāda align_pair_geometry,
    nekādas TRANSFORMS/rotācijas šeit netiek sauktas.

    ITERATĪVA versija (nevis rekursīva) - izmanto SAVU steku, nevis Python
    izsaukumu steku, tāpēc nav ierobežota ar Python noklusēto rekursijas
    dziļumu (~1000) - der jebkuram bloku skaitam (arī tūkstošiem).

    progress_callback(expansions, depth): pēc izvēles, izsauc periodiski
    (ik pa dažām sekundēm) VIENA seed mēķinājuma LAIKĀ - lai lielam
    katalogam/daudz blokiem process NEKAD nešķistu "sastindzis" pat tad,
    ja PATS VIENS mēģinājums (pirms tas izdodas vai izsmeļas) ilgst
    ilgāk par tipisko starp-mēģinājumu progresa ziņojumu intervālu.
    """
    if seed is not None:
        random.seed(seed)
    N = len(positions)
    expansions = [0]

    level_candidates = build_level_candidate_lists(manifest, positions)

    def abs_pt(local_pt, pos):
        return (local_pt[0] + pos[0] * block_size, local_pt[1] + pos[1] * block_size)

    last_progress_time = [time.time()]

    def budget_ok():
        expansions[0] += 1
        if progress_callback is not None and expansions[0] % 20000 == 0:
            now = time.time()
            if now - last_progress_time[0] >= 3.0:
                progress_callback(expansions[0], len(stack))
                last_progress_time[0] = now
        return expansions[0] <= max_expansions

    def verify_atpak_chain(choice, start_last_end_abs):
        """ATPAKAĻ posmam nav izvēles (katra bloka tile jau noteikts TURP
        posmā) - tā ir tikai VIENKĀRŠA (bez zarošanās) savienojumu
        pārbaude, tāpēc te pietiek ar parastu ciklu, ne backtracking."""
        last_end_abs = start_last_end_abs
        for back_level in range(1, N + 1):
            if back_level == N:
                if not require_closing:
                    return True
                first_entry = choice[0]
                turp0_start_abs = abs_pt(first_entry["turp_entry"], positions[0])
                return is_valid_knight_move(last_end_abs, turp0_start_abs)
            block_idx = N - 1 - back_level
            pos = positions[block_idx]
            entry = choice[block_idx]
            entry_abs = abs_pt(entry["atpak_entry"], pos)
            if not is_valid_knight_move(last_end_abs, entry_abs):
                return False
            last_end_abs = abs_pt(entry["atpak_exit"], pos)
        return True

    idxs0 = list(level_candidates[0])
    if require_closing:
        # KRITISKS PAĀTRINĀJUMS: pašnoslēgšanās (atpak_izeja -> paša
        # turp_ieeja) ir ĪPAŠĪBA, kas pilnībā nosaka, jau IZVĒLOTIES
        # bloku #0 - bet BEZ šī filtra tā tiktu pārbaudīta TIKAI pašās
        # beigās (pēc VISAS turp+atpak ķēdes izveides!). Ja izvēlētais
        # bloks #0 pats sevī nesavienojas, VISA pārējā ķēde (lai kā arī
        # izvēlētos pārējos blokus) ir LEMTA NEVEIKSMEI - bet to atklātu
        # tikai pēc dārgas, pilnas meklēšanas. Filtrējot UZREIZ (pirms
        # jebkāda mēģinājuma), garantējam, ka VIENMĒR sāksim ar derīgu
        # bloka #0 izvēli - novēršot šo izšķērdību pilnībā.
        idxs0 = [e for e in idxs0 if is_valid_knight_move(
            abs_pt(e["atpak_exit"], positions[0]), abs_pt(e["turp_entry"], positions[0]))]
    random.shuffle(idxs0)
    idxs0 = idxs0[:first_tries]

    # Iteratīvs (steka bāzēts) backtracking TURP ķēdei - katrs steka
    # ieraksts: {level, candidates, idx, last_end_abs} - 'last_end_abs' ir
    # STĀVOKLIS AR KO ŠIS LĪMENIS SĀK (iepriekšējā bloka izeja).
    stack = [{"level": 0, "candidates": idxs0, "idx": 0, "last_end_abs": None}]
    choice = []

    while stack:
        if not budget_ok():
            return None
        frame = stack[-1]
        lvl = frame["level"]

        if lvl == N:
            # Visi TURP bloki izvēlēti - pārbaudām pāreju uz ATPAKAĻ un
            # visu ATPAKAĻ ķēdi (bez zarošanās, vienkārša pārbaude).
            last_entry = choice[-1]
            atpak_start_abs = abs_pt(last_entry["atpak_entry"], positions[N - 1])
            if is_valid_knight_move(frame["last_end_abs"], atpak_start_abs):
                atpak_exit_abs = abs_pt(last_entry["atpak_exit"], positions[N - 1])
                if verify_atpak_chain(choice, atpak_exit_abs):
                    return list(choice)
            stack.pop()
            if choice:
                choice.pop()
            continue

        if frame["idx"] >= len(frame["candidates"]):
            stack.pop()
            if choice:
                choice.pop()
            continue

        entry = frame["candidates"][frame["idx"]]
        frame["idx"] += 1

        pos = positions[lvl]
        entry_start_abs = abs_pt(entry["turp_entry"], pos)
        if lvl > 0 and not is_valid_knight_move(frame["last_end_abs"], entry_start_abs):
            continue  # mēģina nākamo kandidātu tajā pašā līmenī

        exit_abs = abs_pt(entry["turp_exit"], pos)
        choice.append(entry)

        if lvl + 1 == N:
            next_candidates = []  # N. līmenī pabeigšanas pārbaude notiek augstāk, kandidāti nav vajadzīgi
        else:
            next_pos = positions[lvl + 1]
            next_candidates = [e for e in level_candidates[lvl + 1]
                                if is_valid_knight_move(exit_abs, abs_pt(e["turp_entry"], next_pos))]
            random.shuffle(next_candidates)

        stack.append({"level": lvl + 1, "candidates": next_candidates, "idx": 0, "last_end_abs": exit_abs})

    return None


_file_lines_cache = {}


def pick_random_pair_from_file(fname, pair_offset=0, pair_count=None):
    """Nolasa GATAVU rindu pāri no jau sagatavota faila - bez apstrādes,
    tikai teksta nolasīšana un nejauša indeksa izvēle.

    SVARĪGI: kopš vairāki ģeometrijas varianti VAR dalīties VIENĀ kopīgā
    failā (diska vietas taupīšanai - skat. generate_ready_abt_6x6.py),
    OBLIGĀTI jānorāda `pair_offset` (kuras PĀRA vietas šī grupa sākas) UN
    `pair_count` (cik pāru šai grupai pieder) - CITĀDI tiktu izvēlēts
    NEJAUŠS pāris no VISA faila, kas var piederēt PAVISAM CITAI ģeometrijas
    grupai! Ja pair_count nav dots, pieņem, ka viss fails pieder vienai
    grupai (vecā, vienas-grupas-vienā-failā uzvedība)."""
    if fname not in _file_lines_cache:
        path = os.path.join(GATAVIE_DIR, fname)
        with open(path, "r", encoding="utf-8", newline="") as f:
            raw = f.read()
        lines = raw.split("\r\n")
        if lines and lines[-1] == "":
            lines.pop()
        _file_lines_cache[fname] = lines
    lines = _file_lines_cache[fname]
    if pair_count is None:
        pair_count = len(lines) // 2
    idx = pair_offset + random.randrange(pair_count)
    return lines[idx * 2], lines[idx * 2 + 1]


def moves_to_path(move_str, start=(0, 0)):
    path = [start]
    cx, cy = start
    for char in move_str:
        dx, dy = KNIGHT_MOVES[char]
        cx, cy = cx + dx, cy + dy
        path.append((cx, cy))
    return path


def build_hrz_from_shelf_choice(choice, positions, block_size=16, require_closing=True):
    """
    Katram izvēlētajam kataloga ierakstam paņem NEJAUŠU gatavu rindu pāri
    (jau pareizajā orientācijā, bez pārveidošanas) un salīmē gala .hrz.
    Katra rinda tiek trasēta sākot TIEŠI no manifestā reģistrētā punkta
    (turp_entry / atpak_entry) - tas jau ir zināms, nav jāaprēķina.
    """
    N = len(positions)
    full_path = []

    turp_moves_per_block = []
    atpak_moves_per_block = []
    for level in range(N):
        entry = choice[level]
        turp_line, atpak_line = pick_random_pair_from_file(
            entry["file"], entry.get("pair_offset", 0), entry.get("pair_count"))
        turp_moves_per_block.append(turp_line)
        atpak_moves_per_block.append(atpak_line)

    for level in range(N):
        pos = positions[level]
        offset = (pos[0] * block_size, pos[1] * block_size)
        entry = choice[level]
        local_path = moves_to_path(turp_moves_per_block[level], start=entry["turp_entry"])
        abs_path = [(x + offset[0], y + offset[1]) for x, y in local_path]
        full_path.extend(abs_path)

    for block_idx in range(N - 1, -1, -1):
        pos = positions[block_idx]
        offset = (pos[0] * block_size, pos[1] * block_size)
        entry = choice[block_idx]
        local_path = moves_to_path(atpak_moves_per_block[block_idx], start=entry["atpak_entry"])
        abs_path = [(x + offset[0], y + offset[1]) for x, y in local_path]
        full_path.extend(abs_path)

    hrz_chars = []
    for i in range(len(full_path) - 1):
        dx = full_path[i + 1][0] - full_path[i][0]
        dy = full_path[i + 1][1] - full_path[i][1]
        hrz_chars.append(INVERSE_MOVES.get((dx, dy), '?'))
    if require_closing:
        dx_close = full_path[0][0] - full_path[-1][0]
        dy_close = full_path[0][1] - full_path[-1][1]
        hrz_chars.append(INVERSE_MOVES.get((dx_close, dy_close), '?'))
    return "".join(hrz_chars), full_path


def main():
    # Nodrošinām, ka izvade parādās UZREIZ (nevis tikai pēc pilnīgas
    # pabeigšanas) - citādi garas meklēšanas (daudzi bloki, liels
    # katalogs) var izskatīties "sastingušas", lai gan patiesībā strādā.
    try:
        sys.stdout.reconfigure(line_buffering=True)
    except AttributeError:
        pass  # vecākām Python versijām nav reconfigure - nekas traģisks

    global GATAVIE_DIR, MANIFEST_PATH
    GATAVIE_DIR = sys.argv[1] if len(sys.argv) > 1 else "gatavie_abt"
    ffseciba_file = sys.argv[2] if len(sys.argv) > 2 else "FFseciba.txt"
    # 3. parametrs: "slēgts" (noklusējums, kā līdz šim - mēģina slēgtu
    # ciklu, tad atvērtu ceļu) vai "atvērts" (uzreiz TIKAI atvērts ceļš -
    # DAUDZ ātrāk lielām/nesimetriskām formām, kur SLĒGTA cikla mēģinājums
    # var ievilkties stundām, jo katrs neveiksmīgais mēģinājums piespiež
    # PILNU atkāpšanos no paša sākuma pat pēc simtiem tūkstošu soļu).
    mode = sys.argv[3] if len(sys.argv) > 3 else "slēgts"
    skip_closed = mode.lower().startswith("a")  # "atvērts"/"atverts"/utt.
    MANIFEST_PATH = os.path.join(GATAVIE_DIR, "manifest.txt")

    ordered_steps, positions = parse_ff_seciba_grid(ffseciba_file)
    if not ordered_steps:
        return

    print(f"\n[DIAGNOSTIKA] Nolasīts {len(ordered_steps)} bloku tīkls no {ffseciba_file}:")
    """ vvv
    for step_id, pos in zip(ordered_steps, positions):
        print(f"   * Bloks #{step_id} -> pozīcija laukumā (x={pos[0]}, y={pos[1]})")
    #"""
    print("=" * 60)

    problems_open = check_chain_adjacency(positions, require_closing=False)
    if problems_open:
        print("\n[KĻŪDA] Šī ķēde satur soļus starp NEBLAKUS esošiem laukuma laukiem:")
        for i, j, a, b, dx, dy in problems_open:
            print(f"        * Bloks #{ordered_steps[i]} {a} -> Bloks #{ordered_steps[j]} {b}  (dx={dx}, dy={dy})")
        return

    t0 = time.time()
    manifest = load_manifest()
    block_size = detect_block_size(manifest)
    print(f"[INFO] Nolasīts gatavais katalogs ({GATAVIE_DIR}/): {len(manifest)} ieraksti "
          f"({time.time()-t0:.3f}s) - bloka izmērs AUTOMĀTISKI noteikts: {block_size}x{block_size} - "
          f"NAV veikts NEKĀDS ģeometrijas aprēķins.")
          
    print("[INFO] Gaidi ...")   #vvv
    
    t0 = time.time()
    choice = None
    require_closing = True
    max_seed = 150
    last_print = t0

    def make_progress_cb(seed_try, mode_label):
        def cb(expansions, depth):
            print(f"[INFO] ... seed {seed_try}/{max_seed-1} ({mode_label}) vēl strādā: "
                  f"{expansions:,} soļi izmēģināti, pašreizējais dziļums {depth}/{len(positions)} bloki, "
                  f"kopā pagājušas {time.time()-t0:.1f}s ...", flush=True)
        return cb

    for seed_try in range(1, max_seed):
        if skip_closed:
            break  # lietotājs pieprasījis uzreiz TIKAI atvērtu ceļu - izlaižam šo posmu pilnībā
        choice = find_serpentine_ring_from_shelf(manifest, positions, block_size=block_size,
                                                  seed=seed_try, require_closing=True,
                                                  progress_callback=make_progress_cb(seed_try, "SLĒGTS"))
        if choice:
            print(f"[INFO] Atrasts (SLĒGTS) ar seed={seed_try}")
            break
        now = time.time()
        if now - last_print >= 3.0:  # progresa ziņojums vismaz ik pa 3s, lai NEKAD nešķistu, ka process "karājas"
            print(f"[INFO] ... vēl meklēju SLĒGTU ciklu (seed {seed_try}/{max_seed-1}, "
                  f"pagājušas {now-t0:.1f}s) ...", flush=True)
            last_print = now
    if not choice:
        if skip_closed:
            print(f"[INFO] Izlaižu SLĒGTA cikla mēģinājumu (pēc pieprasījuma) - meklēju TIKAI ATVĒRTU ceļu.")
        else:
            print(f"[INFO] Neizdevās noslēgt pēc {max_seed-1} mēģinājumiem ({time.time()-t0:.1f}s) - "
                  f"mēģinu ATVĒRTU ceļu.")
        require_closing = False
        last_print = time.time()
        for seed_try in range(1, max_seed):
            choice = find_serpentine_ring_from_shelf(manifest, positions, block_size=block_size,
                                                      seed=seed_try, require_closing=False,
                                                      progress_callback=make_progress_cb(seed_try, "ATVĒRTS"))
            if choice:
                print(f"[INFO] Atrasts (ATVĒRTS) ar seed={seed_try}")
                break
            now = time.time()
            if now - last_print >= 3.0:
                print(f"[INFO] ... vēl meklēju ATVĒRTU ceļu (seed {seed_try}/{max_seed-1}, "
                      f"pagājušas {now-t0:.1f}s) ...", flush=True)
                last_print = now
    print(f"[INFO] Izvēle no kataloga: {time.time()-t0:.3f}s")

    if not choice:
        print("\n[KĻŪDA] Kataloga failos nav savienojamas kombinācijas šai FFseciba.txt secībai.")
        return

    print(f"\n[OK] Veiksmīgi izvēlēts {'SLĒGTS' if require_closing else 'ATVĒRTS'} serpentīna ceļš NO PLAUKTA:")
    """ vvv
    for level, entry in enumerate(choice):
        print(f"   * Bloks #{ordered_steps[level]}: {entry['file']} "
              f"(avots {entry['source']}, {entry['pair_count']} pāri pieejami)")
    #"""
    hrz_result, full_path = build_hrz_from_shelf_choice(choice, positions, block_size=block_size,
                                                          require_closing=require_closing)
    print(f"[PĀRBAUDE] unikālas šūnas: {len(set(full_path))}, kopā gājieni: {len(hrz_result)}")

    ffseciba_base = os.path.splitext(os.path.basename(ffseciba_file))[0]
    output_filename = f"{ffseciba_base}.hrz"
    with open(output_filename, "w", encoding="utf-8") as f:
        f.write(hrz_result)
    print(f"[OK] Rezultāts saglabāts: {output_filename}")


if __name__ == "__main__":
    main()
