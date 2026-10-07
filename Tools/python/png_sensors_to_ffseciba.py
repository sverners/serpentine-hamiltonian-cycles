#!/usr/bin/env python3
# -*- coding: utf-8 -*-


# cd C:\Users\svern\source\repos\serpentinaHC3_6x6\bin\Debug
# python png_sensors_to_ffseciba.py %1 %2 (peec nokluseejuma %1 %2 ir attieciigi testaVirsma2.png un FFseciba.txt)
# python png_sensors_to_ffseciba.py testaVirsma2.png testaVirsma2seciba.txt

"""
PNG (melns taisnstūris = tīrāmā virsma, balti kvadrāti = sensori,
jāizvairās; PLUS tieva melna līnija/spirāle = ARĪ tīrāmā virsma) ->
FFseciba.txt, priekš 6x6 "plēsto" .abt blokiem.

NOTEIKUMS (pēc lietotāja norādes): 4x4 PIKSEĻU bloks attēlā = VIENS
6x6 FF bloks galīgajā HC. T.i., attēls tiek sadalīts rūtiņu tīklā ar
soli 4px, un katra rūtiņa kļūst par vienu FFseciba.txt pozīciju.

ATTĒLA STRUKTŪRA (automātiski atpazīta):
  - Melns TAISNSTŪRIS = tīrāmā virsma (VISAS tā rūtiņas jāapmeklē).
  - Balti "izgriezumi" TAISNSTŪRA IEKŠPUSĒ = sensori - šīs rūtiņas
    ir JĀIZSLĒDZ no ceļa (netiek numurētas FFseciba.txt, ceļš tām
    cauri neiet).
  - TIEVA melna līnija/spirāle CITUR attēlā (piem. pa kreisi no
    taisnstūra) - ARĪ tīrāmā virsma - PIEVIENOJAS taisnstūrim (nevis
    tiek ignorēta, kā agrākajā versijā).

METODE (izlabota):
  1. Nosaka VISU melno pikseļu kopējo robežlodziņu (nevis tikai
     blīvā taisnstūra) - tas aptver GAN taisnstūri, GAN tievo
     līniju/spirāli.
  2. Sadala VISU šo apgabalu rūtiņu tīklā ar soli PIXELS_PER_BLOCK.
  3. Katru rūtiņu klasificē pēc melno pikseļu DAĻAS tajā, salīdzinot
     ar ZEMU slieksni (LINE_FILL_THRESHOLD, noklusējums 0.12) -
     ZEMS slieksnis nepieciešams, jo tieva (~4px) līnija aizpilda
     tikai NELIELU daļu no 4x4 rūtiņas, nevis vairākumu - augsts
     ("vairākuma") slieksnis to VIENMĒR palaistu garām. Šis pats
     zemais slieksnis pareizi izslēdz baltos sensoru kvadrātus
     (kuros melno pikseļu daļa ir 0%) un tukšo fonu.
  4. Meklē Hamiltona CIKLU (ja neizdodas - ATVĒRTU ceļu) pa visām
     "virsmas" rūtiņām (soļi tikai uz tieši blakusesošām - augšup/
     lejup/kreisi/labi), izmantojot Vorsdorfa heiristiku + backtracking.
  5. Raksta rezultātu FFseciba.txt formātā.

LIETOŠANA:
    python3 png_sensors_to_ffseciba.py [attēls.png] [FFseciba_izvade.txt]
    (noklusējumi: testaVirsma.png -> FFseciba.txt)
"""

import sys

try:
    from PIL import Image
except ImportError:
    print("[KĻŪDA] Vajadzīga Pillow bibliotēka: pip install Pillow")
    sys.exit(1)

import numpy as np
from scipy import ndimage

try:
    from skimage.morphology import skeletonize
    HAVE_SKIMAGE = True
except ImportError:
    HAVE_SKIMAGE = False

PIXELS_PER_BLOCK = 4          # 4x4 px attēlā = 1 FF bloks
BLACK_THRESHOLD = 128         # pikselis tumšāks par šo = "melns"
RUN_LENGTH_THRESHOLD = 100    # min. nepārtraukta melna posma garums, lai
                               # to uzskatītu par blīvā taisnstūra malu
FILL_MAJORITY = 0.5           # taisnstūra rūtiņa = "virsma", ja >= šī daļa
                               # no tās pikseļiem ir melni (vairākums)

IMAGE_DEFAULT = "testaVirsma.png"
OUTPUT_DEFAULT = "FFseciba.txt"


def longest_run(bool_row):
    best = cur = 0
    for v in bool_row:
        if v:
            cur += 1
            if cur > best:
                best = cur
        else:
            cur = 0
    return best


def find_rectangle_bounds(black_mask):
    """Atrod blīvā taisnstūra precīzās pikseļu robežas pēc garākā
    nepārtrauktā melnā posma katrā rindā/kolonnā - tieva skiču līnija
    (dažu pikseļu platumā) NEKAD nedod tik garu posmu."""
    H, W = black_mask.shape
    row_ok = np.array([longest_run(black_mask[y, :]) > RUN_LENGTH_THRESHOLD for y in range(H)])
    col_ok = np.array([longest_run(black_mask[:, x]) > RUN_LENGTH_THRESHOLD for x in range(W)])
    ys = np.where(row_ok)[0]
    xs = np.where(col_ok)[0]
    if len(ys) == 0 or len(xs) == 0:
        return None
    return xs.min(), xs.max(), ys.min(), ys.max()


def find_black_bounds(mask):
    """Atrod VISU 'True' pikseļu (jebkurā maskā) kopējo robežlodziņu."""
    ys, xs = np.where(mask)
    if len(ys) == 0:
        raise ValueError("Maskā nav neviena 'True' pikseļa!")
    return xs.min(), xs.max(), ys.min(), ys.max()


def build_grid_majority(black_mask, x0, x1, y0, y1, step):
    """Sadala [x0,x1]x[y0,y1] step x step rūtiņās, klasificējot pēc
    VAIRĀKUMA (der BLĪVAM, aizpildītam taisnstūrim - pareizi izslēdz
    baltos sensoru kvadrātus)."""
    n_cols = (x1 - x0 + 1) // step
    n_rows = (y1 - y0 + 1) // step
    grid = {}
    for r in range(n_rows):
        for c in range(n_cols):
            by0, bx0 = y0 + r * step, x0 + c * step
            block = black_mask[by0:by0 + step, bx0:bx0 + step]
            grid[(r, c)] = block.mean() >= FILL_MAJORITY
    return grid


def build_grid_any(mask, x0, x1, y0, y1, step):
    """Sadala [x0,x1]x[y0,y1] step x step rūtiņās, klasificējot pēc TĀ,
    vai rūtiņā ir KAUT VIENS 'True' pikselis (der TIEVAI, jau IZTIEKNOTAI
    (1px platas) līnijai - jebkura vairākuma prasība to nekad nenoķertu)."""
    n_cols = (x1 - x0 + 1) // step
    n_rows = (y1 - y0 + 1) // step
    grid = {}
    for r in range(n_rows):
        for c in range(n_cols):
            by0, bx0 = y0 + r * step, x0 + c * step
            block = mask[by0:by0 + step, bx0:bx0 + step]
            grid[(r, c)] = bool(block.any())
    return grid


def prune_skeleton_spurs(skel, min_branch_length=20):
    """Iztieknošana bieži rada nelielus, nevēlamus "spuru" atzarojumus
    (dažu pikseļu garus blakuszarus, kas rodas no oriģinālās līnijas
    nelīdzenumiem/pretalisinga) - katrs tāds spurs rada VĒL VIENU
    mākslīgu grāda-1 (strupceļa) punktu, kas sabojā Hamiltona ceļa
    meklēšanu (var pat radīt pilnīgi izolētas šūnas). Šī funkcija
    ATKĀRTOTI atrod ĪSUS (< min_branch_length pikseļu) atzarojumus un
    tos noņem, atstājot tikai "galveno" skeleta struktūru."""
    skel = skel.copy()
    changed = True
    while changed:
        changed = False
        ys, xs = np.where(skel)
        coords = set(zip(ys.tolist(), xs.tolist()))
        if len(coords) < 2:
            break

        def px_neighbors(p):
            y, x = p
            result = []
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if dy == 0 and dx == 0:
                        continue
                    q = (y + dy, x + dx)
                    if q in coords:
                        result.append(q)
            return result

        endpoints = [p for p in coords if len(px_neighbors(p)) <= 1]
        for ep in endpoints:
            if ep not in coords:
                continue  # jau noņemts iepriekšējā šī paša caurgājiena solī
            branch = [ep]
            prev = None
            cur = ep
            while True:
                nbs = [n for n in px_neighbors(cur) if n != prev]
                if len(nbs) != 1:
                    break  # sasniegts zara punkts (>=2 kaimiņi) vai izolēts pikselis
                prev, cur = cur, nbs[0]
                branch.append(cur)
                if len(branch) > min_branch_length:
                    break
            if len(branch) <= min_branch_length:
                for p in branch[:-1]:  # paturam pēdējo (zara/savienojuma) punktu
                    skel[p] = False
                    coords.discard(p)
                changed = True
    return skel


def trace_skeleton_to_grid_cells(skel, x0, y0, step):
    """Izseko TĪRU (grāds<=2 gandrīz visur, skat. prune_skeleton_spurs)
    1-pikseļa platuma skeleta ceļu PĒC SECĪBAS (no viena grāda-1 punkta uz
    otru) un pārvērš to par RŪTIŅU KOPU, ievietojot "tiltiņa" rūtiņas, kur
    divas SECĪGAS ceļa rūtiņas ir tikai DIAGONĀLI (nevis taisnleņķī)
    blakus - citādi diagonāla pikseļu kustība (dabiska iztieknotai
    diagonālai līnijai) radītu MĀKSLĪGUS "zarus" rūtiņu tīklā, jo
    taisnleņķa (rook) blakusesamība to nepamanītu. SVARĪGI: tas atrisina
    to pašu problēmu, ko atklājām ar naivu 'jebkurš pikselis rūtiņā'
    grideēšanu - tā PATI PAR SEVI nekad negarantē rūtiņu ĶĒDES
    savienojamību, pat ja pati pikseļu līnija ir nepārtraukta."""
    ys, xs = np.where(skel)
    if len(ys) == 0:
        return []
    coords = set(zip(ys.tolist(), xs.tolist()))

    def px_nb(p):
        y, x = p
        res = []
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dy == 0 and dx == 0:
                    continue
                q = (y + dy, x + dx)
                if q in coords:
                    res.append(q)
        return res

    endpoints = [p for p in coords if len(px_nb(p)) <= 1]
    start_px = endpoints[0] if endpoints else next(iter(coords))

    # izsekojam ceļu no start_px, ejot pa VIENĪGO (vai pirmo neapmeklēto)
    # nākamo pikseli katrā solī - der TĪRAM (bez zariem) skeletam.
    ordered_px = [start_px]
    seen_px = {start_px}
    prev = None
    cur = start_px
    while True:
        nbs = [n for n in px_nb(cur) if n != prev and n not in seen_px]
        if not nbs:
            break
        nxt = nbs[0]
        ordered_px.append(nxt)
        seen_px.add(nxt)
        prev, cur = cur, nxt

    def to_grid(p):
        py, px = p
        return ((py - y0) // step, (px - x0) // step)

    grid_cells = []
    for p in ordered_px:
        gc = to_grid(p)
        if not grid_cells or grid_cells[-1] != gc:
            grid_cells.append(gc)

    # ievietojam tiltiņa rūtiņas starp diagonāli (nevis taisnleņķī) blakus
    # esošām secīgām rūtiņām
    bridged = [grid_cells[0]] if grid_cells else []
    for i in range(1, len(grid_cells)):
        r1, c1 = bridged[-1]
        r2, c2 = grid_cells[i]
        if abs(r1 - r2) <= 1 and abs(c1 - c2) <= 1 and (r1, c1) != (r2, c2):
            if r1 != r2 and c1 != c2:
                bridged.append((r1, c2))  # jebkurš no diviem "stūra" variantiem der
        bridged.append((r2, c2))
    return bridged


def detect_surface_and_sensors(black_mask, step):
    """
    KOMBINĒTA noteikšana:
      - BLĪVAIS TAISNSTŪRIS (ja atrasts): klasificē pēc VAIRĀKUMA - tas
        pareizi izslēdz baltos sensoru "izgriezumus" tā iekšpusē.
      - VISS PĀRĒJAIS melnais (tievas līnijas/spirāle ĀRPUS taisnstūra):
        vispirms IZTIEKNO (skeletonize) uz 1 pikseļa platumu - tas ir
        SVARĪGI: 4px biezas līnijas naivi grideējot, diagonālas daļas
        rada 2-rūtiņu platu "kāpņveida" joslu, kam ir SLIKTAS Hamiltona
        ceļa īpašības (var padarīt ceļu neiespējamu tieši šai konkrētajai
        kvantizācijai, lai gan pati forma to neprasa) - iztieknošana šo
        problēmu novērš, dodot tīru, vienas-rūtiņas platu joslu. Pēc tam
        klasificē pēc "VAI IR KAUT VIENS" pikselis (nevis vairākuma).
      - Abas daļas apvieno VIENĀ KOPĒJĀ rūtiņu tīklā (pēc pilna attēla
        robežlodziņa), lai tās pareizi savienotos caur kopīgajām
        koordinātēm.

    Atgriež (surface_cells, sensor_cells, n_rows, n_cols).
    """
    rect_bounds = find_rectangle_bounds(black_mask)

    line_mask = black_mask.copy()
    rect_sub_mask = None
    rx0 = ry0 = 0
    if rect_bounds is not None:
        rx0, rx1, ry0, ry1 = rect_bounds
        rect_sub_mask = black_mask[ry0:ry1 + 1, rx0:rx1 + 1]
        line_mask[ry0:ry1 + 1, rx0:rx1 + 1] = False  # izņemam taisnstūri no "līniju" maskas

    if HAVE_SKIMAGE and line_mask.any():
        line_mask = skeletonize(line_mask)
        line_mask = prune_skeleton_spurs(line_mask)
        # paturam TIKAI lielāko savienoto komponenti - jebkuri atlikušie
        # sīkie, izolētie fragmenti ir iztieknošanas troksnis, nevis
        # jēgpilna zīmējuma daļa.
        labeled, num = ndimage.label(line_mask, structure=np.ones((3, 3)))
        if num > 1:
            sizes = ndimage.sum(line_mask, labeled, range(1, num + 1))
            biggest = np.argmax(sizes) + 1
            line_mask = (labeled == biggest)
    # (ja skimage nav pieejams - turpinām ar neapstrādātu līniju masku;
    #  strādās, bet var uzrādīt to pašu "kāpņveida" problēmu)

    full_x0, full_x1, full_y0, full_y1 = find_black_bounds(black_mask | line_mask)

    n_cols = (full_x1 - full_x0 + 1) // step
    n_rows = (full_y1 - full_y0 + 1) // step

    # SVARĪGI: TRACE (nevis naivi grideē) skeleta ceļu, lai izvairītos no
    # diagonāli-blakus (nevis taisnleņķī-blakus) rūtiņām, kas sarautu
    # ķēdes savienojamību (skat. trace_skeleton_to_grid_cells).
    line_cells_ordered = trace_skeleton_to_grid_cells(line_mask, full_x0, full_y0, step)
    surface_cells = set(line_cells_ordered)
    sensor_cells = set()

    if rect_bounds is not None:
        rect_grid_local = build_grid_majority(black_mask, rx0, rx1, ry0, ry1, step)
        # pārrēķinam taisnstūra rūtiņu (rindas,kolonnas) uz TO PAŠU KOPĒJO
        # koordinātu sistēmu, ko izmanto line_grid (nobīde pēc pikseļu
        # starpības / step, jo abi tīkli sākas ar to pašu 'step' soli no
        # SAVA (nevis kopīgā) x0/y0 - jānobīda uz kopīgo sākumpunktu)
        row_offset = (ry0 - full_y0) // step
        col_offset = (rx0 - full_x0) // step
        for (r, c), is_surface in rect_grid_local.items():
            gr, gc = r + row_offset, c + col_offset
            if is_surface:
                surface_cells.add((gr, gc))
            else:
                sensor_cells.add((gr, gc))

    return surface_cells, sensor_cells, n_rows, n_cols


# ---- Hamiltona ceļa meklētājs (tāda pati metode kā shape_to_ffseciba.py) ----

def neighbors(cell, cell_set):
    r, c = cell
    result = []
    for dr, dc in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
        nb = (r + dr, c + dc)
        if nb in cell_set:
            result.append(nb)
    return result


def count_free_neighbors(cell, cell_set, visited):
    return sum(1 for nb in neighbors(cell, cell_set) if nb not in visited)


def find_hamiltonian_path(cell_set, start, require_closed=False, time_budget_steps=3_000_000):
    """ITERATĪVA (steka-balstīta) versija - BEZ rekursijas, tāpēc der
    jebkuram rūtiņu skaitam (tūkstošiem) bez Python rekursijas limita vai
    pavediena steka izmēra pielāgošanas nepieciešamības (kas izrādījās
    neuzticama dažādās platformās, piem. Windows noraidīja lielu manuālu
    steka izmēru)."""
    total = len(cell_set)
    visited = {start}
    path = [start]
    # katram ceļa līmenim: (kandidātu saraksts, tekošais indekss)
    cand_stack = [None]  # cand_stack[i] atbilst path[i] kandidātiem
    idx_stack = [0]
    steps = 0

    while True:
        steps += 1
        if steps > time_budget_steps:
            return None

        if len(path) == total:
            current = path[-1]
            found = (start in neighbors(current, cell_set)) if require_closed else True
            if found:
                return path
            # "atrasts pilns ceļš, bet neslēdzas" - atkāpjamies tāpat kā
            # izsmeltas iespējas gadījumā
        else:
            depth = len(path) - 1
            if cand_stack[depth] is None:
                current = path[-1]
                candidates = [nb for nb in neighbors(current, cell_set) if nb not in visited]
                # Vorsdorfa heiristika: vispirms rūtiņas ar VISMAZĀK
                # atlikušajām brīvajām kaimiņrūtīm.
                candidates.sort(key=lambda nb: count_free_neighbors(nb, cell_set, visited))
                cand_stack[depth] = candidates
                idx_stack[depth] = 0

            candidates = cand_stack[depth]
            if idx_stack[depth] < len(candidates):
                nb = candidates[idx_stack[depth]]
                idx_stack[depth] += 1
                visited.add(nb)
                path.append(nb)
                cand_stack.append(None)
                idx_stack.append(0)
                continue
            # citādi - šis līmenis izsmelts, atkāpjamies zemāk

        # atkāpšanās (backtrack): noņemam pēdējo pozīciju
        if len(path) <= 1:
            return None
        cand_stack.pop()
        idx_stack.pop()
        removed = path.pop()
        visited.remove(removed)


def remaining_reachable_from(cell_set, visited, source):
    """BFS - atgriež kopu ar VISĀM šūnām, kas sasniedzamas no `source`,
    ejot TIKAI caur vēl NEAPMEKLĒTĀM (cell_set - visited) šūnām. Der
    savienojamības pārbaudei grūtos (šaurās/spirālveida) gadījumos -
    skat. find_hamiltonian_path_pruned zemāk."""
    seen = {source}
    stack = [source]
    while stack:
        cur = stack.pop()
        for nb in neighbors(cur, cell_set):
            if nb not in visited and nb not in seen:
                seen.add(nb)
                stack.append(nb)
    return seen


def find_hamiltonian_path_pruned(cell_set, start, require_closed=False, time_budget_steps=3_000_000):
    """TĀDA PATI metode kā find_hamiltonian_path, TIKAI ar SAVIENOJAMĪBAS
    APGRIEŠANU (connectivity pruning) - pirms izvēlas kandidātu, pārbauda,
    vai PĒC pārvietošanās uz to atlikušās NEAPMEKLĒTĀS šūnas JOPROJĀM veido
    VIENU savienotu grupu. Šis paņēmiens ir DAUDZ lēnāks katrā solī (katrai
    kandidātu pārbaudei vajag BFS pa visu atlikušo tīklu), tāpēc der TIKAI
    kā REZERVES METODE grūtiem (šauriem) gadījumiem, kur parastā (ātrā)
    versija neizdodas - LIELIEM ATVĒRTIEM APGABALIEM šī versija ir
    NEPRAKTISKI lēna, tāpēc main() to izmanto TIKAI ja parastā neizdodas."""
    total = len(cell_set)
    visited = {start}
    path = [start]
    cand_stack = [None]
    idx_stack = [0]
    steps = 0

    while True:
        steps += 1
        if steps > time_budget_steps:
            return None

        if len(path) == total:
            current = path[-1]
            found = (start in neighbors(current, cell_set)) if require_closed else True
            if found:
                return path
        else:
            depth = len(path) - 1
            if cand_stack[depth] is None:
                current = path[-1]
                raw_candidates = [nb for nb in neighbors(current, cell_set) if nb not in visited]
                candidates = []
                for nb in raw_candidates:
                    remaining_needed = total - len(path) - 1
                    if remaining_needed == 0:
                        candidates.append(nb)
                        continue
                    visited.add(nb)
                    reachable = remaining_reachable_from(cell_set, visited, nb)
                    reachable.discard(nb)
                    still_needed = cell_set - visited
                    visited.discard(nb)
                    if reachable == still_needed:
                        candidates.append(nb)
                candidates.sort(key=lambda nb: count_free_neighbors(nb, cell_set, visited))
                cand_stack[depth] = candidates
                idx_stack[depth] = 0

            candidates = cand_stack[depth]
            if idx_stack[depth] < len(candidates):
                nb = candidates[idx_stack[depth]]
                idx_stack[depth] += 1
                visited.add(nb)
                path.append(nb)
                cand_stack.append(None)
                idx_stack.append(0)
                continue

        if len(path) <= 1:
            return None
        cand_stack.pop()
        idx_stack.pop()
        removed = path.pop()
        visited.remove(removed)


def render_ffseciba(cell_set, path):
    order = {cell: i for i, cell in enumerate(path)}
    max_row = max(r for r, c in cell_set)
    max_col = max(c for r, c in cell_set)
    lines = []
    for r in range(max_row + 1):
        fields = []
        for c in range(max_col + 1):
            if (r, c) in order:
                fields.append(str(order[(r, c)]))
            else:
                fields.append(" ")
        lines.append(",".join(fields) + ",")
    return "\n".join(lines)


def main():
    image_path = sys.argv[1] if len(sys.argv) > 1 else IMAGE_DEFAULT
    out_path = sys.argv[2] if len(sys.argv) > 2 else OUTPUT_DEFAULT

    img = Image.open(image_path).convert("L")
    arr = np.array(img)
    black_mask = arr < BLACK_THRESHOLD

    if not HAVE_SKIMAGE:
        print("[BRĪDINĀJUMS] scikit-image nav pieejams (pip install scikit-image) - "
              "tievās līnijas/spirāle netiks iztieknotas, kas var radīt savienojamības "
              "problēmas. Turpinu ar neapstrādātu līniju masku.")

    surface_cells, sensor_cells, n_rows, n_cols = detect_surface_and_sensors(black_mask, PIXELS_PER_BLOCK)
    print(f"[INFO] Rūtiņu tīkls: {n_rows} rindas x {n_cols} kolonnas "
          f"({PIXELS_PER_BLOCK}x{PIXELS_PER_BLOCK}px/bloks)")
    print(f"[INFO] Virsmas rūtiņas (jāapmeklē): {len(surface_cells)}")
    print(f"[INFO] Sensoru rūtiņas (izslēgtas): {len(sensor_cells)}")

    if not surface_cells:
        print("[KĻŪDA] Nav atrasta neviena virsmas rūtiņa!")
        return

    # sākuma rūtiņa: ja eksistē rūtiņa ar TIEŠI 1 kaimiņu (piem. spirāles
    # gals) - tā ir OBLIGĀTA jebkura atvērta Hamiltona ceļa galapunkts,
    # tāpēc sākot TIEŠI no tās meklēšana ir daudz efektīvāka. Citādi -
    # pirmā rūtiņa lasīšanas secībā.
    degree1 = [c for c in surface_cells if len(neighbors(c, surface_cells)) == 1]
    start = degree1[0] if degree1 else min(surface_cells)
    if degree1:
        print(f"[INFO] Atrasta piespiedu galapunkta rūtiņa (1 kaimiņš): {start} - sāku no tās.")

    path = find_hamiltonian_path(surface_cells, start, require_closed=True)
    closed = True
    if path is None:
        print("[INFO] Neizdevās atrast SLĒGTU ciklu - mēģinu ATVĒRTU ceļu.")
        path = find_hamiltonian_path(surface_cells, start, require_closed=False)
        closed = False

    if path is None:
        print("[INFO] Ātrā meklēšana neizdevās - mēģinu LĒNĀKU, bet DROŠU "
              "(savienojamības apgriešanu izmantojošu) meklēšanu (var aizņemt kādu brīdi)...")
        path = find_hamiltonian_path_pruned(surface_cells, start, require_closed=True)
        closed = True
        if path is None:
            path = find_hamiltonian_path_pruned(surface_cells, start, require_closed=False)
            closed = False

    if path is None:
        print("[KĻŪDA] Neizdevās atrast derīgu Hamiltona ceļu/ciklu šai virsmai "
              "(iespējams, sensori/forma sadala virsmu tā, ka tas ir patiešām neiespējami).")
        return

    print(f"[OK] Atrasts {'SLĒGTS cikls' if closed else 'ATVĒRTS ceļš'} "
          f"({len(path)}/{len(surface_cells)} rūtiņas).")

    result = render_ffseciba(surface_cells, path)
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(result)
    print(f"[OK] Saglabāts {out_path}")


if __name__ == "__main__":
    main()
