#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Diagnostikas rīks serpentina_hc_v12.py "palagu" būvēšanai - PIRMS ilgas
(un, ja neizdodas, rezultātā tukšas) meklēšanas pasaka PRECĪZI, kura
KONKRĒTĀ bloku pāreja NEVAR savienoties ar pašreizējo gatavo failu
katalogu - neatkarīgi no tā, kāda būtu izvēle citās ķēdes vietās.

METODE: pretstatā find_serpentine_ring_from_shelf (kas mēģina atrast
VIENU DERĪGU KOPĒJU izvēli visai ķēdei ar backtracking un nejaušību),
šis rīks pārbauda katru pāreju ATSEVIŠĶI un IZSMEĻOŠI (visas iespējamās
kandidātu kombinācijas tajā VIENĀ pārejā) - tas ir DAUDZ ātrāk, un ja
KĀDA pāreja neizdodas ŠEIT, tas ir PIERĀDĪJUMS (nevis minējums), ka
VISA ķēde nekad nevarēs savienoties, lai kā arī nemēģinātu.

Pārbauda 4 veidu pārejas:
  1. TURP ķēde (bloks[i] izeja -> bloks[i+1] ieeja)
  2. TURP->ATPAK pāreja PĒDĒJĀ blokā (tā paša ieraksta izeja -> paša ieeja)
  3. ATPAK ķēde (bloks[i+1] izeja -> bloks[i] ieeja, pretējā virzienā)
  4. NOSLĒGUMS, ja vajag slēgtu ciklu (bloks[0] atpak_izeja -> paša turp_ieeja)

PIEZĪME: 1. un 3. pārbaude ir "vaļīga" (necessary, but not sufficient) -
tā PIERĀDA neiespējamību, ja neizdodas, bet PAT JA visas 4 pārbaudes
izdodas, pilna ķēde tik un tā var neizdoties (jo TURP un ATPAK izvēle
katrā blokā ir SAISTĪTA - tas pats ieraksts jālieto abām). Bet, ja KĀDA
no šīm 4 pārbaudēm NEIZDODAS, tas ir GALĪGS pierādījums - nav jēgas
tērēt laiku pilnai meklēšanai.

LIETOŠANA:
    python3 diagnose_ffseciba.py [gatavo_failu_direktorija] [FFseciba_fails] [slēgts|atvērts]
    (noklusējumi: "gatavie_abt", "FFseciba.txt", "slēgts")
"""

import os
import sys
import time
import itertools

import serpentina_hc_v12 as core


def abs_pt(local_pt, pos, block_size):
    return (local_pt[0] + pos[0] * block_size, local_pt[1] + pos[1] * block_size)


def find_any_connection(cands_a, key_a, pos_a, cands_b, key_b, pos_b, block_size):
    """Pārbauda, vai EKSISTĒ VISMAZ VIENA kombinācija starp cands_a un cands_b,
    kur cands_a[key_a] (pēc pos_a nobīdes) savienojas ar cands_b[key_b] (pēc
    pos_b nobīdes). Atgriež (True, piemērs) vai (False, None)."""
    for ea in cands_a:
        p_a = abs_pt(ea[key_a], pos_a, block_size)
        for eb in cands_b:
            p_b = abs_pt(eb[key_b], pos_b, block_size)
            if core.is_valid_knight_move(p_a, p_b):
                return True, (ea, eb)
    return False, None


def diagnose(gatavie_dir, ffseciba_file, require_closing=True):
    core.GATAVIE_DIR = gatavie_dir
    core.MANIFEST_PATH = os.path.join(gatavie_dir, "manifest.txt")

    manifest = core.load_manifest()
    block_size = core.detect_block_size(manifest)
    ordered_steps, positions = core.parse_ff_seciba_grid(ffseciba_file)
    if not ordered_steps:
        return
    N = len(positions)

    problems_open = core.check_chain_adjacency(positions, require_closing=False)
    if problems_open:
        print("[KĻŪDA] Šī ķēde satur soļus starp NEBLAKUS esošiem laukuma laukiem - "
              "vispirms izlabo FFseciba.txt.")
        return

    level_candidates = core.build_level_candidate_lists(manifest, positions)

    print(f"[INFO] Katalogs: {gatavie_dir}/ ({len(manifest)} ieraksti), bloka izmērs: "
          f"{block_size}x{block_size}, {N} pozīcijas, mērķis: "
          f"{'SLĒGTS cikls' if require_closing else 'ATVĒRTS ceļš'}")
    for level, cands in enumerate(level_candidates):
        print(f"   Bloks #{ordered_steps[level]} pozīcijā {positions[level]}: "
              f"{len(cands)} kandidāti (pēc malu tipa filtra)")
    print("=" * 70)

    all_ok = True

    # 1. TURP ķēde
    print("\n--- 1. TURP ķēde (bloks[i] izeja -> bloks[i+1] ieeja) ---")
    for level in range(N - 1):
        ok, example = find_any_connection(
            level_candidates[level], "turp_exit", positions[level],
            level_candidates[level + 1], "turp_entry", positions[level + 1],
            block_size)
        status = "OK" if ok else "!! TRŪKST !!"
        print(f"  #{ordered_steps[level]} -> #{ordered_steps[level+1]}: "
              f"{len(level_candidates[level])}x{len(level_candidates[level+1])} "
              f"kombinācijas pārbaudītas -> {status}")
        if not ok:
            all_ok = False
            print(f"     Nepieciešams: bloks #{ordered_steps[level]} kāda TURP izeja, kas ar "
                  f"derīgu zirdziņa gājienu sasniedz bloku #{ordered_steps[level+1]} KĀDU TURP ieeju.")
            print(f"     Nevienā no {len(level_candidates[level])} x {len(level_candidates[level+1])} "
                  f"pārbaudītajām kombinācijām tas nesanāk.")

    # 2. TURP->ATPAK pāreja pēdējā blokā (paša ieraksta izeja->ieeja)
    print("\n--- 2. TURP->ATPAK pāreja PĒDĒJĀ blokā (tā paša ieraksta izeja -> paša ieeja) ---")
    last = N - 1
    self_ok_list = [e for e in level_candidates[last]
                     if core.is_valid_knight_move(
                         abs_pt(e["turp_exit"], positions[last], block_size),
                         abs_pt(e["atpak_entry"], positions[last], block_size))]
    status = "OK" if self_ok_list else "!! TRŪKST !!"
    print(f"  Bloks #{ordered_steps[last]}: {len(self_ok_list)}/{len(level_candidates[last])} "
          f"kandidāti paši savienojas -> {status}")
    if not self_ok_list:
        all_ok = False
        print(f"     NEVIENAM no {len(level_candidates[last])} kandidātiem šai pozīcijai "
              f"turp_izeja nesavienojas ar paša atpak_ieeju.")

    # 3. ATPAK ķēde (pretējā virzienā)
    print("\n--- 3. ATPAK ķēde (bloks[i+1] izeja -> bloks[i] ieeja, pretējā virzienā) ---")
    for level in range(N - 1):
        ok, example = find_any_connection(
            level_candidates[level + 1], "atpak_exit", positions[level + 1],
            level_candidates[level], "atpak_entry", positions[level],
            block_size)
        status = "OK" if ok else "!! TRŪKST !!"
        print(f"  #{ordered_steps[level+1]} -> #{ordered_steps[level]}: "
              f"{len(level_candidates[level+1])}x{len(level_candidates[level])} "
              f"kombinācijas pārbaudītas -> {status}")
        if not ok:
            all_ok = False
            print(f"     Nepieciešams: bloks #{ordered_steps[level+1]} kāda ATPAK izeja, kas ar "
                  f"derīgu zirdziņa gājienu sasniedz bloku #{ordered_steps[level]} KĀDU ATPAK ieeju.")

    # 4. Noslēgums (ja slēgts cikls)
    if require_closing:
        print("\n--- 4. NOSLĒGUMS (bloks[0] atpak_izeja -> paša turp_ieeja, tai pašai izvēlei) ---")
        close_ok_list = [e for e in level_candidates[0]
                          if core.is_valid_knight_move(
                              abs_pt(e["atpak_exit"], positions[0], block_size),
                              abs_pt(e["turp_entry"], positions[0], block_size))]
        status = "OK" if close_ok_list else "!! TRŪKST !!"
        print(f"  Bloks #{ordered_steps[0]}: {len(close_ok_list)}/{len(level_candidates[0])} "
              f"kandidāti paši noslēdzas -> {status}")
        if not close_ok_list:
            all_ok = False
            print(f"     NEVIENAM no {len(level_candidates[0])} kandidātiem šai pozīcijai "
                  f"atpak_izeja nesavienojas ar paša turp_ieeju.")

    print("\n" + "=" * 70)
    if not all_ok:
        print("[SECINĀJUMS] Atrasts VISMAZ VIENS PIERĀDĀMS trūkums (skat. '!! TRŪKST !!' augstāk).\n"
              "             Pilna ķēde NEKAD nevarēs savienoties, kamēr šis konkrētais\n"
              "             trūkums nav novērsts - nepalīdzēs ne vairāk seed, ne lielāks budžets.\n"
              "             Vajag jaunu(-us) 6x6 ciklu(-us), kas satur TIEŠI šo trūkstošo\n"
              "             malu/punktu kombināciju (skat. 'Nepieciešams:' rindas augstāk).")
        return

    print("[INFO] VISAS PĀRU pārejas iespējamas (necessary conditions izpildās) - bet tas\n"
          "       NEGARANTĒ pilnas ķēdes savienojamību, jo TURP un ATPAK izvēle katrā\n"
          "       blokā ir SAISTĪTA (viens un tas pats ieraksts jālieto abām reizē).\n"
          "       Mēģinu IZSMEĻOŠU (nevis nejaušu) pilnas ķēdes pārbaudi...")

    exhaustive_check(level_candidates, positions, ordered_steps, block_size, require_closing)


def exhaustive_check(level_candidates, positions, ordered_steps, block_size, require_closing,
                      max_product=20_000_000):
    """PILNĪGI IZSMEĻOŠA (bez nejaušības, bez budžeta ierobežojuma) pārbaude, vai
    EKSISTĒ VISMAZ VIENA derīga izvēle katrai pozīcijai, kas apmierina VISUS
    nosacījumus VIENLAICĪGI (turp ķēde UN atpak ķēde UN pašsavienojamība UN
    noslēgums, ja vajag) - VIENAS UN TĀS PAŠAS izvēles katrā pozīcijā.

    Atšķirībā no find_serpentine_ring_from_shelf (nejauša, ar budžeta griestiem),
    šis vai nu ATRISINA, vai PIERĀDA, ka risinājuma NAV - GALĪGI, nevis "varbūt
    vienkārši paveicās mazāk".

    Der TIKAI ja kandidātu skaitu reizinājums ir saprātīgs (noklusējums: līdz
    20 miljoniem) - citādi (lielām ķēdēm) tas prasītu nesamērīgi ilgu laiku, un
    tad jāpaļaujas uz find_serpentine_ring_from_shelf nejaušo meklēšanu."""
    N = len(positions)
    sizes = [len(c) for c in level_candidates]
    product = 1
    for s in sizes:
        product *= max(s, 1)
        if product > max_product:
            print(f"[INFO] Kandidātu kombināciju skaits ({sizes}) ir pārāk liels izsmeļošai "
                  f"pārbaudei (> {max_product:,}) - šai formai jāpaļaujas uz "
                  f"serpentina_hc_v12.py nejaušo meklēšanu (vairāk seed/budžets var palīdzēt "
                  f"vai nepalīdzēt, atkarībā no gadījuma).")
            return

    print(f"[INFO] Kombināciju skaits: {product:,} - pārbaudu VISAS (tas var aizņemt kādu brīdi)...")

    t0 = time.time()
    checked = 0
    for combo in itertools.product(*level_candidates):
        checked += 1
        ok = True
        # turp ķēde
        for level in range(N - 1):
            exit_abs = abs_pt(combo[level]["turp_exit"], positions[level], block_size)
            entry_abs = abs_pt(combo[level + 1]["turp_entry"], positions[level + 1], block_size)
            if not core.is_valid_knight_move(exit_abs, entry_abs):
                ok = False
                break
        if not ok:
            continue
        # pēdējā bloka turp->atpak pašsavienojamība
        last = N - 1
        if not core.is_valid_knight_move(
                abs_pt(combo[last]["turp_exit"], positions[last], block_size),
                abs_pt(combo[last]["atpak_entry"], positions[last], block_size)):
            continue
        # atpak ķēde
        for level in range(N - 1):
            exit_abs = abs_pt(combo[level + 1]["atpak_exit"], positions[level + 1], block_size)
            entry_abs = abs_pt(combo[level]["atpak_entry"], positions[level], block_size)
            if not core.is_valid_knight_move(exit_abs, entry_abs):
                ok = False
                break
        if not ok:
            continue
        # noslēgums
        if require_closing:
            if not core.is_valid_knight_move(
                    abs_pt(combo[0]["atpak_exit"], positions[0], block_size),
                    abs_pt(combo[0]["turp_entry"], positions[0], block_size)):
                continue

        print(f"\n[REZULTĀTS] ATRASTS derīgs risinājums pēc {checked:,}/{product:,} "
              f"pārbaudītām kombinācijām ({time.time()-t0:.1f}s)!")
        for level, e in enumerate(combo):
            print(f"   * Bloks #{ordered_steps[level]}: {e['file']}")
        print("\n[SECINĀJUMS] Risinājums PASTĀV - find_serpentine_ring_from_shelf to "
              "nejauši nepamanīja (neveiksmīga nejaušība/budžets), NEVIS ka tā nav. "
              "Palaid serpentina_hc_v12.py vēlreiz (vairāk seed mēģinājumu var palīdzēt) "
              "vai izmanto augstāk redzamo konkrēto failu izvēli tieši.")
        return

    print(f"\n[REZULTĀTS] Pārbaudītas VISAS {checked:,} kombinācijas ({time.time()-t0:.1f}s) - "
          f"NEVIENA neapmierina visus nosacījumus vienlaicīgi.")
    print("\n[GALĪGS SECINĀJUMS] Risinājuma AR PAŠREIZĒJO KATALOGU NAV - šī nav nejaušība "
          "vai budžeta problēma, tā ir PIERĀDĀMA neiespējamība. Nepieciešami jauni 6x6 "
          "cikli, kas ievieš CITU turp/atpak kombināciju kādā no iesaistītajām pozīcijām.")


def main():
    gatavie_dir = sys.argv[1] if len(sys.argv) > 1 else "gatavie_abt"
    ffseciba_file = sys.argv[2] if len(sys.argv) > 2 else "FFseciba.txt"
    mode = sys.argv[3] if len(sys.argv) > 3 else "slēgts"
    require_closing = mode.lower().startswith("s")
    diagnose(gatavie_dir, ffseciba_file, require_closing=require_closing)


if __name__ == "__main__":
    main()
